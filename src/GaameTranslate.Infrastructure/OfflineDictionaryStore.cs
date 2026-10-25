using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaameTranslate.Core;
using Microsoft.Data.Sqlite;

namespace GaameTranslate.Infrastructure;

public sealed partial class OfflineDictionaryStore(string directory)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "dictionaries.db"), DefaultTimeout = 10 }.ToString();
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        Directory.CreateDirectory(directory); var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE IF NOT EXISTS entries(id INTEGER PRIMARY KEY, word_key TEXT NOT NULL,headword TEXT NOT NULL,reading TEXT NOT NULL,definition TEXT NOT NULL,source TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_dictionary_word ON entries(word_key);"; await command.ExecuteNonQueryAsync(token); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async Task<IReadOnlyList<DictionaryArticle>> LookupAsync(string word, CancellationToken token = default)
    {
        var keys = Forms(word).Select(TextProcessing.WordKey).Distinct().ToArray(); await using var connection = await OpenAsync(token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT headword,reading,definition,source FROM entries WHERE word_key IN (" + string.Join(',', keys.Select((_, i) => "$k" + i)) + ") ORDER BY CASE WHEN word_key=$k0 THEN 0 ELSE 1 END,id LIMIT 8";
        for (var i = 0; i < keys.Length; i++) command.Parameters.AddWithValue("$k" + i, keys[i]); var output = new List<DictionaryArticle>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) output.Add(new(reader.GetString(0), reader.GetString(2), reader.GetString(3)) { Phonetic = reader.GetString(1) }); return output;
    }
    public async Task<IReadOnlyList<(string Name, long Count)>> SourcesAsync(CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = "SELECT source,count(*) FROM entries GROUP BY source ORDER BY source"; var result = new List<(string, long)>(); await using var reader = await command.ExecuteReaderAsync(token); while (await reader.ReadAsync(token)) result.Add((reader.GetString(0), reader.GetInt64(1))); return result;
    }
    public async Task RemoveAsync(string source, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM entries WHERE source=$source"; command.Parameters.AddWithValue("$source", source); await command.ExecuteNonQueryAsync(token);
    }
    public async Task<int> ImportAsync(string path, CancellationToken token = default)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant(); if (extension is not (".zip" or ".dsl" or ".ifo" or ".jsonl" or ".txt")) throw new UserFacingException("Поддерживаются Yomitan .zip, Lingvo .dsl, Kaikki .jsonl, CC-CEDICT .txt и StarDict .ifo с соседними .idx/.dict файлами.");
        if (new FileInfo(path).Length > 256 * 1024 * 1024) throw new UserFacingException("Выберите словарь размером до 256 МБ.");
        await gate.WaitAsync(token);
        try
        {
            await using var connection = await OpenAsync(token); using var transaction = connection.BeginTransaction(); var source = Path.GetFileNameWithoutExtension(path);
            await using var insert = connection.CreateCommand(); insert.Transaction = transaction; insert.CommandText = "INSERT INTO entries(word_key,headword,reading,definition,source) VALUES($key,$word,$reading,$definition,$source)";
            foreach (var name in new[] { "$key", "$word", "$reading", "$definition", "$source" }) insert.Parameters.Add(name, SqliteType.Text);
            var count = 0;
            void Add(string word, string reading, string definition)
            {
                token.ThrowIfCancellationRequested(); word = word.Trim(); definition = Plain(definition);
                if (word.Length is 0 or > 256 || definition.Length == 0) return;
                if (++count > 1000000) throw new UserFacingException("Словарь содержит более миллиона статей. Импорт отменён.");
                insert.Parameters["$key"].Value = TextProcessing.WordKey(word); insert.Parameters["$word"].Value = word; insert.Parameters["$reading"].Value = reading; insert.Parameters["$definition"].Value = definition[..Math.Min(32000, definition.Length)]; insert.Parameters["$source"].Value = source; insert.ExecuteNonQuery();
            }
            await using (var remove = connection.CreateCommand()) { remove.Transaction = transaction; remove.CommandText = "DELETE FROM entries WHERE source=$source"; remove.Parameters.AddWithValue("$source", source); await remove.ExecuteNonQueryAsync(token); }
            if (extension == ".zip")
            {
                using var archive = ZipFile.OpenRead(path); var banks = archive.Entries.Where(e => e.Name.StartsWith("term_bank_", StringComparison.Ordinal) && e.Name.EndsWith(".json", StringComparison.Ordinal)).ToArray();
                if (banks.Length == 0) throw new UserFacingException("В архиве нет term_bank_*.json. Выберите словарь в формате Yomitan.");
                if (banks.Sum(e => e.Length) > 768L * 1024 * 1024 || banks.Any(e => e.Length > 128L * 1024 * 1024)) throw new UserFacingException("Распакованный словарь слишком велик.");
                foreach (var bank in banks)
                {
                    using var stream = bank.Open(); using var json = JsonDocument.Parse(stream);
                    foreach (var row in json.RootElement.EnumerateArray()) if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 6) Add(row[0].GetString() ?? "", row[1].GetString() ?? "", Glossary(row[5]));
                }
            }
            else if (extension == ".dsl")
            {
                var bytes = await File.ReadAllBytesAsync(path, token); var encoding = bytes.Take(100).Any(b => b == 0) ? Encoding.Unicode : Encoding.UTF8; using var reader = new StringReader(encoding.GetString(bytes).TrimStart('\uFEFF'));
                var heads = new List<string>(); var definition = new StringBuilder(); string? line;
                void Flush() { foreach (var head in heads) Add(head, "", DslTags().Replace(definition.ToString(), "")); heads.Clear(); definition.Clear(); }
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
                    if (char.IsWhiteSpace(line[0])) definition.AppendLine(line.Trim());
                    else { if (definition.Length > 0) Flush(); heads.Add(line.Trim()); }
                }
                Flush();
            }
            else if (extension == ".jsonl")
            {
                using var reader = File.OpenText(path); string? line;
                while ((line = await reader.ReadLineAsync(token)) is not null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue; if (line.Length > 2 * 1024 * 1024) throw new UserFacingException("Слишком большая статья JSONL.");
                    using var json = JsonDocument.Parse(line); var entry = json.RootElement; if (!entry.TryGetProperty("word", out var word)) continue;
                    var definitions = new List<string>(); if (entry.TryGetProperty("senses", out var senses)) foreach (var sense in senses.EnumerateArray()) if (sense.TryGetProperty("glosses", out var glosses)) definitions.AddRange(glosses.EnumerateArray().Select(g => g.GetString() ?? ""));
                    var phonetic = entry.TryGetProperty("sounds", out var sounds) ? string.Join(" ", sounds.EnumerateArray().Where(s => s.TryGetProperty("ipa", out _)).Select(s => s.GetProperty("ipa").GetString()).Distinct()) : "";
                    Add(word.GetString() ?? "", phonetic, string.Join("\n", definitions));
                }
            }
            else if (extension == ".txt")
            {
                using var reader = File.OpenText(path); string? line;
                while ((line = await reader.ReadLineAsync(token)) is not null)
                {
                    if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
                    var open = line.IndexOf('['); var close = line.IndexOf(']', open < 0 ? 0 : open); var slash = line.IndexOf('/', close < 0 ? 0 : close);
                    if (open < 0 || close < 0 || slash < 0) continue; var words = line[..open].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); var reading = line[(open + 1)..close]; var definition = line[(slash + 1)..].TrimEnd('/').Replace('/', '\n');
                    foreach (var word in words.Distinct()) Add(word, reading, definition);
                }
            }
            else
            {
                var basePath = path[..^4]; var metadata = await File.ReadAllTextAsync(path, token); var wide = metadata.Contains("idxoffsetbits=64", StringComparison.Ordinal); var types = metadata.Split('\n').FirstOrDefault(l => l.StartsWith("sametypesequence=", StringComparison.Ordinal))?.Split('=')[1].Trim();
                if (types is not null && types.Any(char.IsUpper)) throw new UserFacingException("Этот StarDict содержит бинарные статьи. Используйте текстовый словарь.");
                var idx = ReadMaybeGzip(basePath + ".idx"); var dict = ReadMaybeGzip(basePath + ".dict"); var position = 0;
                while (position < idx.Length)
                {
                    var end = Array.IndexOf(idx, (byte)0, position); if (end < 0) throw new UserFacingException("Повреждён индекс StarDict."); var word = Encoding.UTF8.GetString(idx, position, end - position); position = end + 1; var needed = wide ? 12 : 8; if (position + needed > idx.Length) throw new UserFacingException("Повреждён индекс StarDict.");
                    var offset = wide ? BinaryPrimitives.ReadUInt64BigEndian(idx.AsSpan(position, 8)) : BinaryPrimitives.ReadUInt32BigEndian(idx.AsSpan(position, 4)); position += wide ? 8 : 4; var length = BinaryPrimitives.ReadUInt32BigEndian(idx.AsSpan(position, 4)); position += 4;
                    if (offset > (ulong)dict.LongLength || length > (ulong)dict.LongLength - offset) throw new UserFacingException("Статья выходит за границы .dict."); var text = Encoding.UTF8.GetString(dict, (int)offset, (int)length).Replace('\0', '\n'); if (types is null && text.Length > 0 && char.IsLetter(text[0])) text = text[1..]; Add(word, "", text);
                }
            }
            if (count == 0) throw new UserFacingException("В словаре не найдены текстовые статьи. Изменения отменены."); transaction.Commit(); return count;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException) { throw new UserFacingException("Файл словаря повреждён или имеет неподдерживаемую структуру. Предыдущие статьи сохранены.", e); }
        finally { gate.Release(); }
    }
    private static byte[] ReadMaybeGzip(string path)
    {
        var chosen = File.Exists(path) ? path : path + (path.EndsWith(".dict", StringComparison.Ordinal) ? ".dz" : ".gz"); if (!File.Exists(chosen)) throw new UserFacingException("Для StarDict нужны соседние .idx и .dict (либо .gz/.dz) файлы.");
        using var stream = File.OpenRead(chosen); using var gzip = chosen == path ? null : new GZipStream(stream, CompressionMode.Decompress); using var result = new MemoryStream(); var input = (Stream?)gzip ?? stream; var buffer = new byte[81920]; int read;
        while ((read = input.Read(buffer)) > 0) { if (result.Length + read > 768L * 1024 * 1024) throw new UserFacingException("Распакованный словарь слишком велик."); result.Write(buffer, 0, read); }
        return result.ToArray();
    }
    private static string Glossary(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Array => string.Join("\n", element.EnumerateArray().Select(Glossary)),
        JsonValueKind.Object => element.TryGetProperty("content", out var content) ? Glossary(content) : element.TryGetProperty("text", out var text) ? Glossary(text) : "",
        _ => ""
    };
    [GeneratedRegex("<[^>]+>")] private static partial Regex HtmlTags();
    [GeneratedRegex(@"\[/?[a-zA-Z][^\]]*\]")] private static partial Regex DslTags();
    private static string Plain(string text) => WebUtility.HtmlDecode(HtmlTags().Replace(text, " ")).Trim();
    private static IEnumerable<string> Forms(string text)
    {
        var word = TextProcessing.Normalize(text).Trim(' ', '.', ',', '!', '?', ';', ':', '"', '“', '”', '«', '»'); yield return word;
        var lower = word.ToLowerInvariant(); var irregular = lower switch { "better" or "best" => "good", "worse" or "worst" => "bad", "went" or "gone" => "go", "was" or "were" or "been" => "be", "bought" => "buy", "thought" => "think", "children" => "child", _ => null }; if (irregular is not null) yield return irregular;
        foreach (var suffix in new[] { "ing", "ed", "es", "s" }) if (lower.Length > suffix.Length + 2 && lower.EndsWith(suffix, StringComparison.Ordinal)) { var stem = lower[..^suffix.Length]; yield return stem; if (suffix is "ing" or "ed") yield return stem + "e"; }
    }
}
