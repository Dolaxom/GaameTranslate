using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaameTranslate.Core;
using Microsoft.Data.Sqlite;

namespace GaameTranslate.Infrastructure;

/// <summary>Portable Anki schema-11 package. Scheduling starts fresh; vocabulary IDs give notes stable GUIDs.</summary>
public static class AnkiPackageExporter
{
    private const long ModelId = 1743300201000, DeckId = 1743300202000;
    private static readonly string[] Fields = ["Word", "Translation", "Context", "ContextTranslation", "Definition", "Pronunciation", "Grammar", "Examples", "Synonyms", "Explanation", "Image", "Audio"];
    public static async Task ExportAsync(string destination, IReadOnlyList<VocabularyEntry> words, LocalMediaStore media, bool reverse = false, CancellationToken token = default)
    {
        var temp = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp"; var database = temp + ".anki2";
        try
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal); var now = DateTimeOffset.UtcNow; var timestamp = now.ToUnixTimeSeconds();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(token); await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE col(id integer PRIMARY KEY,crt integer NOT NULL,mod integer NOT NULL,scm integer NOT NULL,ver integer NOT NULL,dty integer NOT NULL,usn integer NOT NULL,ls integer NOT NULL,conf text NOT NULL,models text NOT NULL,decks text NOT NULL,dconf text NOT NULL,tags text NOT NULL);
                    CREATE TABLE notes(id integer PRIMARY KEY,guid text NOT NULL,mid integer NOT NULL,mod integer NOT NULL,usn integer NOT NULL,tags text NOT NULL,flds text NOT NULL,sfld integer NOT NULL,csum integer NOT NULL,flags integer NOT NULL,data text NOT NULL);
                    CREATE TABLE cards(id integer PRIMARY KEY,nid integer NOT NULL,did integer NOT NULL,ord integer NOT NULL,mod integer NOT NULL,usn integer NOT NULL,type integer NOT NULL,queue integer NOT NULL,due integer NOT NULL,ivl integer NOT NULL,factor integer NOT NULL,reps integer NOT NULL,lapses integer NOT NULL,left integer NOT NULL,odue integer NOT NULL,odid integer NOT NULL,flags integer NOT NULL,data text NOT NULL);
                    CREATE TABLE revlog(id integer PRIMARY KEY,cid integer NOT NULL,usn integer NOT NULL,ease integer NOT NULL,ivl integer NOT NULL,lastIvl integer NOT NULL,factor integer NOT NULL,time integer NOT NULL,type integer NOT NULL);
                    CREATE TABLE graves(usn integer NOT NULL,oid integer NOT NULL,type integer NOT NULL);
                    CREATE INDEX ix_notes_csum ON notes(csum);CREATE INDEX ix_cards_nid ON cards(nid);CREATE INDEX ix_cards_sched ON cards(did,queue,due);
                    """;
                await command.ExecuteNonQueryAsync(token);
                var templates = new List<object> { new { name = "Word in context", ord = 0, qfmt = "<div class='context'>{{Context}}</div><div class='word'>{{Word}}</div>{{Audio}}", afmt = "{{FrontSide}}<hr id=answer><div class='translation'>{{Translation}}</div>{{Pronunciation}} · {{Grammar}}<p>{{Definition}}</p><p>{{ContextTranslation}}</p><p>{{Examples}}</p><p>{{Synonyms}}</p><p>{{Explanation}}</p>{{Image}}", bqfmt = "", bafmt = "", did = (long?)null } };
                if (reverse) templates.Add(new { name = "Reverse", ord = 1, qfmt = "<div class='word'>{{Translation}}</div>", afmt = "{{FrontSide}}<hr id=answer><div class='translation'>{{Word}}</div>{{Audio}}<p>{{Context}}</p>{{Image}}", bqfmt = "", bafmt = "", did = (long?)null });
                var model = new { id = ModelId, name = "GaameTranslate Context v2", type = 0, mod = timestamp, usn = -1, sortf = 0, did = DeckId, tmpls = templates, flds = Fields.Select((name, ord) => new { name, ord, sticky = false, rtl = false, font = "Arial", size = 20, media = Array.Empty<string>() }), css = ".card{font:20px 'Segoe UI',Arial;text-align:left;line-height:1.5;background:#171d26;color:#edf3f7;padding:24px}.word{font-size:32px;font-weight:600}.translation{font-size:27px;color:#76d9b0}.context{color:#a8b8c9;margin-bottom:20px}img{max-width:100%;border-radius:10px}hr{border:0;border-top:1px solid #3c4755;margin:20px 0}", latexPre = "", latexPost = "", latexsvg = false, req = reverse ? new object[] { new object[] { 0, "all", new[] { 0 } }, new object[] { 1, "all", new[] { 1 } } } : new object[] { new object[] { 0, "all", new[] { 0 } } }, tags = Array.Empty<string>(), vers = Array.Empty<string>() };
                var deck = new { id = DeckId, name = "GaameTranslate", desc = "Words and context saved from games", mod = timestamp, usn = -1, dyn = 0, conf = 1, extendNew = 10, extendRev = 50, collapsed = false, browserCollapsed = false, newToday = new[] { 0, 0 }, revToday = new[] { 0, 0 }, lrnToday = new[] { 0, 0 }, timeToday = new[] { 0, 0 } };
                command.CommandText = "INSERT INTO col VALUES(1,$crt,$mod,$mod,11,0,-1,0,$conf,$models,$decks,$dconf,'{}')";
                command.Parameters.AddWithValue("$crt", timestamp); command.Parameters.AddWithValue("$mod", now.ToUnixTimeMilliseconds()); command.Parameters.AddWithValue("$conf", JsonSerializer.Serialize(new { nextPos = words.Count + 1, curDeck = DeckId, activeDecks = new[] { DeckId }, newSpread = 0, collapseTime = 1200, estTimes = true, dueCounts = true, sortType = "noteFld", sortBackwards = false, addToCur = true, schedVer = 2 }));
                command.Parameters.AddWithValue("$models", JsonSerializer.Serialize(new Dictionary<string, object> { [ModelId.ToString()] = model })); command.Parameters.AddWithValue("$decks", JsonSerializer.Serialize(new Dictionary<string, object> { [DeckId.ToString()] = deck, ["1"] = new { id = 1, name = "Default", desc = "", mod = timestamp, usn = -1, dyn = 0, conf = 1, extendNew = 10, extendRev = 50, collapsed = false, newToday = new[] { 0, 0 }, revToday = new[] { 0, 0 }, lrnToday = new[] { 0, 0 }, timeToday = new[] { 0, 0 } } }));
                command.Parameters.AddWithValue("$dconf", JsonSerializer.Serialize(new Dictionary<string, object> { ["1"] = new { id = 1, name = "Default", mod = timestamp, usn = -1, maxTaken = 60, autoplay = true, timer = 0, replayq = true, @new = new { bury = true, delays = new[] { 1, 10 }, initialFactor = 2500, ints = new[] { 1, 4, 7 }, order = 1, perDay = 20 }, rev = new { bury = true, ease4 = 1.3, fuzz = .05, ivlFct = 1, maxIvl = 36500, perDay = 200 }, lapse = new { delays = new[] { 10 }, leechAction = 0, leechFails = 8, minInt = 1, mult = 0 } } }));
                await command.ExecuteNonQueryAsync(token); command.Parameters.Clear(); using var transaction = connection.BeginTransaction(); command.Transaction = transaction;
                var noteId = now.ToUnixTimeMilliseconds(); var cardId = noteId + words.Count + 1;
                foreach (var word in words)
                {
                    token.ThrowIfCancellationRequested(); string Html(string text) => WebUtility.HtmlEncode(text).Replace("\n", "<br>");
                    string Media(string file, bool audio)
                    {
                        var path = media.Resolve(file); if (path is null) return ""; if (!files.ContainsKey(file)) files[file] = path; return audio ? $"[sound:{file}]" : $"<img src=\"{file}\">";
                    }
                    var values = new[] { Html(word.Word), Html(word.Translation), Html(word.Context), Html(word.TranslatedContext), Html(word.Definition), Html(word.Phonetic), Html(word.PartOfSpeech + " " + word.Level), Html(word.Examples), Html(word.Synonyms), Html(word.Explanation), Media(word.ScreenshotFile, false), Media(word.AudioFile, true) };
                    var stable = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(word.SourceLanguage + "|" + word.TargetLanguage + "|" + TextProcessing.WordKey(word.Word))))[..16];
                    command.CommandText = "INSERT INTO notes VALUES($id,$guid,$mid,$mod,-1,$tags,$fields,$sort,$checksum,0,'')"; command.Parameters.Clear(); command.Parameters.AddWithValue("$id", noteId); command.Parameters.AddWithValue("$guid", stable); command.Parameters.AddWithValue("$mid", ModelId); command.Parameters.AddWithValue("$mod", timestamp); command.Parameters.AddWithValue("$tags", " " + string.Join(' ', word.Tags.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Replace("\u001f", ""))) + " "); command.Parameters.AddWithValue("$fields", string.Join('\u001f', values)); command.Parameters.AddWithValue("$sort", word.Word); command.Parameters.AddWithValue("$checksum", (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(SHA1.HashData(Encoding.UTF8.GetBytes(word.Word)))); await command.ExecuteNonQueryAsync(token);
                    for (var ordinal = 0; ordinal < (reverse ? 2 : 1); ordinal++) { command.CommandText = "INSERT INTO cards VALUES($id,$nid,$did,$ord,$mod,-1,0,0,$due,0,0,0,0,0,0,0,0,'')"; command.Parameters.Clear(); command.Parameters.AddWithValue("$id", cardId++); command.Parameters.AddWithValue("$nid", noteId); command.Parameters.AddWithValue("$did", DeckId); command.Parameters.AddWithValue("$ord", ordinal); command.Parameters.AddWithValue("$mod", timestamp); command.Parameters.AddWithValue("$due", noteId % 1000000); await command.ExecuteNonQueryAsync(token); }
                    noteId++;
                }
                transaction.Commit();
            }
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(database, "collection.anki2", CompressionLevel.Optimal); var map = new Dictionary<string, string>(); var index = 0;
                foreach (var file in files) { zip.CreateEntryFromFile(file.Value, index.ToString(), CompressionLevel.Optimal); map[index.ToString()] = file.Key; index++; }
                var mapping = zip.CreateEntry("media"); await using var stream = mapping.Open(); await JsonSerializer.SerializeAsync(stream, map, cancellationToken: token);
            }
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(database)) File.Delete(database); if (File.Exists(temp)) File.Delete(temp); }
    }
}
