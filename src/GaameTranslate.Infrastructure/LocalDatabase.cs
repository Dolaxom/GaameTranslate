using System.Globalization;
using System.Text.Json;
using GaameTranslate.Core;
using Microsoft.Data.Sqlite;

namespace GaameTranslate.Infrastructure;

/// <summary>Short-lived pooled connections, parameterized queries, WAL and bounded busy timeout.</summary>
public sealed class LocalDatabase(string directory)
{
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "gaametranslate.db"), DefaultTimeout = 5 }.ToString();
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(ConnectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async Task InitializeAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        await using var connection = await OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS vocabulary (
                id INTEGER PRIMARY KEY AUTOINCREMENT, word_key TEXT NOT NULL, source TEXT NOT NULL,
                target TEXT NOT NULL, payload TEXT NOT NULL, due TEXT NOT NULL,
                UNIQUE(word_key, source, target));
            CREATE INDEX IF NOT EXISTS ix_vocabulary_due ON vocabulary(due);
            CREATE TABLE IF NOT EXISTS history (
                id TEXT PRIMARY KEY, created TEXT NOT NULL, original TEXT NOT NULL,
                translated TEXT NOT NULL, payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_history_created ON history(created DESC);
            PRAGMA user_version=1;
            """;
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task<VocabularyEntry> SaveWordAsync(VocabularyEntry entry, CancellationToken token = default)
    {
        entry = entry with { Word = TextProcessing.Normalize(entry.Word), SourceLanguage = entry.SourceLanguage.ToLowerInvariant(), TargetLanguage = entry.TargetLanguage.ToLowerInvariant() };
        if (entry.Word.Length == 0 || string.IsNullOrWhiteSpace(entry.Translation)) throw new UserFacingException("Enter a word and its translation before saving.");
        await using var connection = await OpenAsync(token);
        using var transaction = connection.BeginTransaction();
        await using var find = connection.CreateCommand(); find.Transaction = transaction;
        find.CommandText = "SELECT id, payload FROM vocabulary WHERE word_key=$key AND source=$source AND target=$target";
        find.Parameters.AddWithValue("$key", TextProcessing.WordKey(entry.Word)); find.Parameters.AddWithValue("$source", entry.SourceLanguage); find.Parameters.AddWithValue("$target", entry.TargetLanguage);
        long id = 0; VocabularyEntry? existing = null;
        await using (var reader = await find.ExecuteReaderAsync(token))
            if (await reader.ReadAsync(token)) { id = reader.GetInt64(0); existing = JsonSerializer.Deserialize<VocabularyEntry>(reader.GetString(1)); }
        if (existing is not null) entry = existing with { Id = id, Encounters = existing.Encounters + 1 };
        else
        {
            await using var insert = connection.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO vocabulary(word_key,source,target,payload,due) VALUES($key,$source,$target,'', $due); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$key", TextProcessing.WordKey(entry.Word)); insert.Parameters.AddWithValue("$source", entry.SourceLanguage); insert.Parameters.AddWithValue("$target", entry.TargetLanguage); insert.Parameters.AddWithValue("$due", Stamp(entry.DueAt));
            id = (long)(await insert.ExecuteScalarAsync(token))!;
            entry = entry with { Id = id };
        }
        await UpdateCoreAsync(connection, transaction, entry, token);
        transaction.Commit();
        return entry;
    }
    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static async Task UpdateCoreAsync(SqliteConnection connection, SqliteTransaction? transaction, VocabularyEntry entry, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE vocabulary SET word_key=$key, source=$source, target=$target, payload=$payload, due=$due WHERE id=$id";
        command.Parameters.AddWithValue("$key", TextProcessing.WordKey(entry.Word)); command.Parameters.AddWithValue("$source", entry.SourceLanguage); command.Parameters.AddWithValue("$target", entry.TargetLanguage);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(entry)); command.Parameters.AddWithValue("$due", Stamp(entry.DueAt)); command.Parameters.AddWithValue("$id", entry.Id);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task UpdateWordAsync(VocabularyEntry entry, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Word) || string.IsNullOrWhiteSpace(entry.Translation)) throw new UserFacingException("Word and translation cannot be empty.");
        await using var connection = await OpenAsync(token);
        try { await UpdateCoreAsync(connection, null, entry, token); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { throw new UserFacingException("That word is already saved for this language pair.", e); }
    }
    public async Task<IReadOnlyList<VocabularyEntry>> GetWordsAsync(string search = "", string? source = null, bool dueOnly = false, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token);
        await using var command = connection.CreateCommand();
        // Search parsed fields, not JSON syntax or escaped Unicode. Vocabulary is intentionally local.
        command.CommandText = "SELECT payload FROM vocabulary WHERE ($source IS NULL OR source=$source) AND ($due=0 OR due <= $now) ORDER BY id DESC";
        command.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value); command.Parameters.AddWithValue("$due", dueOnly ? 1 : 0); command.Parameters.AddWithValue("$now", Stamp(DateTimeOffset.UtcNow));
        var entries = new List<VocabularyEntry>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var e = JsonSerializer.Deserialize<VocabularyEntry>(reader.GetString(0))!;
            if (string.IsNullOrWhiteSpace(search) || new[] { e.Word, e.Translation, e.Context, e.Tags, e.Notes }.Any(s => s.Contains(search, StringComparison.OrdinalIgnoreCase))) entries.Add(e);
        }
        return entries;
    }
    public async Task DeleteWordAsync(long id, CancellationToken token = default) => await ExecuteAsync("DELETE FROM vocabulary WHERE id=$id", id, token);
    public async Task ClearVocabularyAsync(CancellationToken token = default) => await ExecuteAsync("DELETE FROM vocabulary", null, token);
    private async Task ExecuteAsync(string sql, object? id, CancellationToken token)
    {
        await using var connection = await OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(token);
    }
    public async Task AddHistoryAsync(TranslationSession session, int limit, CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token); using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO history(id,created,original,translated,payload) VALUES($id,$created,$original,$translated,$payload);
            DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY created DESC LIMIT $limit);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString()); command.Parameters.AddWithValue("$created", Stamp(session.CreatedAt));
        command.Parameters.AddWithValue("$original", session.OriginalText); command.Parameters.AddWithValue("$translated", session.TranslatedText);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session)); command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 0, 10000));
        await command.ExecuteNonQueryAsync(token); transaction.Commit();
    }
    public async Task<IReadOnlyList<TranslationSession>> GetHistoryAsync(string search = "", CancellationToken token = default)
    {
        await using var connection = await OpenAsync(token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM history WHERE instr(lower(original),lower($search))>0 OR instr(lower(translated),lower($search))>0 ORDER BY created DESC LIMIT 500";
        command.Parameters.AddWithValue("$search", search);
        var result = new List<TranslationSession>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(JsonSerializer.Deserialize<TranslationSession>(reader.GetString(0))!);
        return result;
    }
    public async Task DeleteHistoryAsync(Guid id, CancellationToken token = default) => await ExecuteAsync("DELETE FROM history WHERE id=$id", id.ToString(), token);
    public async Task ClearHistoryAsync(CancellationToken token = default) => await ExecuteAsync("DELETE FROM history", null, token);
}
