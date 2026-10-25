using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

public sealed class AnkiConnectClient(HttpClient http, LocalMediaStore media)
{
    private const string Model = "GaameTranslate Sync v1";
    private static readonly string[] Fields = ["GaameId", "Word", "Translation", "Context", "ContextTranslation", "Definition", "Pronunciation", "Grammar", "Examples", "Synonyms", "Explanation", "Image", "Audio"];
    public async Task<(int Added, int Updated, int MarkedRemoved)> SyncAsync(AppSettings settings, IReadOnlyList<VocabularyEntry> words, CancellationToken token = default)
    {
        var uri = AppSettings.ValidateEndpoint(settings.AnkiEndpoint); if (!uri.IsLoopback) throw new UserFacingException("AnkiConnect должен быть запущен на локальном компьютере.");
        async Task<JsonElement> Call(string action, object? parameters = null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { using var response = await http.PostAsJsonAsync(uri, new { action, version = 6, @params = parameters }, timeout.Token); response.EnsureSuccessStatusCode(); await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, timeout.Token); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token)); if (json.RootElement.GetProperty("error").ValueKind != JsonValueKind.Null) throw new UserFacingException("AnkiConnect: " + json.RootElement.GetProperty("error").GetString()); return json.RootElement.GetProperty("result").Clone(); }
            catch (HttpRequestException e) { throw new UserFacingException("Откройте Anki и установите AnkiConnect (2055492159), затем разрешите локальное подключение.", e); }
            catch (OperationCanceledException e) when (!token.IsCancellationRequested) { throw new UserFacingException("AnkiConnect не ответил. Проверьте окно Anki: возможно, оно ожидает подтверждения подключения.", e); }
        }
        if ((await Call("version")).GetInt32() < 6) throw new UserFacingException("Обновите дополнение AnkiConnect до API v6.");
        await Call("createDeck", new { deck = settings.AnkiDeck }); var models = await Call("modelNames");
        if (!models.EnumerateArray().Any(m => m.GetString() == Model)) await Call("createModel", new { modelName = Model, inOrderFields = Fields, css = ".card{font:20px 'Segoe UI',Arial;line-height:1.5;text-align:left}.word{font-size:32px;font-weight:600}.context{opacity:.7}img{max-width:100%}", cardTemplates = new[] { new { Name = "Word in context", Front = "<div class=context>{{Context}}</div><div class=word>{{Word}}</div>{{Audio}}", Back = "{{FrontSide}}<hr id=answer><h2>{{Translation}}</h2>{{Pronunciation}} · {{Grammar}}<p>{{Definition}}</p><p>{{ContextTranslation}}</p><p>{{Examples}}</p><p>{{Synonyms}}</p><p>{{Explanation}}</p>{{Image}}" } } });
        var ids = (await Call("findNotes", new { query = "note:\"" + Model + "\"" })).EnumerateArray().Select(e => e.GetInt64()).ToArray(); var existing = new Dictionary<string, long>();
        foreach (var batch in ids.Chunk(100)) { var notes = await Call("notesInfo", new { notes = batch }); foreach (var note in notes.EnumerateArray()) if (note.GetProperty("fields").TryGetProperty("GaameId", out var field)) existing[field.GetProperty("value").GetString() ?? ""] = note.GetProperty("noteId").GetInt64(); }
        var active = new HashSet<string>(); var added = 0; var updated = 0;
        static string Html(string text) => System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>");
        foreach (var word in words)
        {
            token.ThrowIfCancellationRequested(); var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TextProcessing.WordKey(word.Word) + "|" + word.SourceLanguage + "|" + word.TargetLanguage)))[..24]; active.Add(identity);
            async Task<string> Upload(string name, bool audio)
            {
                if (media.Resolve(name) is not { } file) return ""; var ankiName = "gaame_" + name; await Call("storeMediaFile", new { filename = ankiName, data = Convert.ToBase64String(await File.ReadAllBytesAsync(file, token)) }); return audio ? "[sound:" + ankiName + "]" : "<img src=\"" + ankiName + "\">";
            }
            var values = new Dictionary<string, string> { ["GaameId"] = identity, ["Word"] = Html(word.Word), ["Translation"] = Html(word.Translation), ["Context"] = Html(word.Context), ["ContextTranslation"] = Html(word.TranslatedContext), ["Definition"] = Html(word.Definition), ["Pronunciation"] = Html(word.Phonetic), ["Grammar"] = Html(word.PartOfSpeech), ["Examples"] = Html(word.Examples), ["Synonyms"] = Html(word.Synonyms), ["Explanation"] = Html(word.Explanation), ["Image"] = await Upload(word.ScreenshotFile, false), ["Audio"] = await Upload(word.AudioFile, true) };
            if (existing.TryGetValue(identity, out var id)) { await Call("updateNoteFields", new { note = new { id, fields = values } }); await Call("removeTags", new { notes = new[] { id }, tags = "GaameTranslate::removed" }); updated++; }
            else { await Call("addNote", new { note = new { deckName = settings.AnkiDeck, modelName = Model, fields = values, tags = new[] { "GaameTranslate" }, options = new { allowDuplicate = false } } }); added++; }
        }
        var removed = existing.Where(p => !active.Contains(p.Key)).Select(p => p.Value).ToArray(); foreach (var batch in removed.Chunk(100)) await Call("addTags", new { notes = batch, tags = "GaameTranslate::removed" });
        return (added, updated, removed.Length);
    }
}
