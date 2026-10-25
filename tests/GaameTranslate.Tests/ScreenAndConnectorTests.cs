using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaameTranslate.Tests;

public sealed class ScreenAndConnectorTests
{
    [Fact]
    public async Task ProfilesKeepCredentialsOutAndFindExecutablesCaseInsensitively()
    {
        var root = Path.Combine(Path.GetTempPath(), "gaame-profiles-" + Guid.NewGuid().ToString("N"));
        try { var store = new GameProfileStore(root); await store.SaveAsync(new("Disco", "disco.exe", new() { Provider = "Yandex Translate", ContextHint = "literary detective dialogue" })); Assert.Equal("Disco", (await store.FindAsync("DISCO.EXE"))?.Name); await store.SaveAsync(new("Updated", "disco.exe", new() { Provider = "Ollama" })); Assert.Single(await store.LoadAsync()); Assert.Null(await store.FindAsync("other.exe")); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task AnkiSyncIsRepeatableAndMarksRemovedWithoutDeletingNotes()
    {
        var notes = new Dictionary<long, Dictionary<string, string>>(); var nextId = 1L; var deleted = false; var marked = false;
        using var http = new HttpClient(new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var action = body.RootElement.GetProperty("action").GetString(); var p = body.RootElement.GetProperty("params"); object? result = null;
            switch (action)
            {
                case "version": result = 6; break;
                case "modelNames": result = new[] { "GaameTranslate Sync v1" }; break;
                case "findNotes": result = notes.Keys.ToArray(); break;
                case "notesInfo": result = notes.Select(n => new { noteId = n.Key, fields = n.Value.ToDictionary(f => f.Key, f => (object)new { value = f.Value }) }).ToArray(); break;
                case "addNote": var fields = p.GetProperty("note").GetProperty("fields"); notes[nextId++] = fields.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!); result = nextId - 1; break;
                case "updateNoteFields": var note = p.GetProperty("note"); notes[note.GetProperty("id").GetInt64()] = note.GetProperty("fields").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!); break;
                case "deleteNotes": deleted = true; break;
                case "addTags": marked = true; break;
            }
            return Json(new { result, error = (string?)null });
        }));
        var client = new AnkiConnectClient(http, new LocalMediaStore(Path.GetTempPath())); var word = new VocabularyEntry { Word = "better", Translation = "лучше", SourceLanguage = "en", TargetLanguage = "ru" };
        Assert.Equal((1, 0, 0), await client.SyncAsync(new(), [word])); Assert.Equal((0, 1, 0), await client.SyncAsync(new(), [word with { Definition = "in context" }])); Assert.Single(notes); Assert.Equal((0, 0, 1), await client.SyncAsync(new(), [])); Assert.True(marked); Assert.False(deleted);
    }
    private sealed class Keys : ICredentialStore { public string? Read(string name) => "contract-test-key"; public void Write(string name, string value) { } public void Delete(string name) { } }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) => handle(message); }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    [Fact]
    public void JoinsHighlightedFragmentsBeforeParagraphsWithoutLosingQuote()
    {
        OcrBlock Line(int n, string text, PixelRect bounds) => new(n, text, bounds, [new(text, bounds)]) { LineBounds = [bounds] };
        var document = new OcrDocument([Line(0, "JOYCE MESSIER — Better", new(219, 586, 283, 33)), Line(1, "not to tie the forestay to the", new(499, 590, 286, 25)), Line(2, "backstay on this. I hope there is something else I can", new(254, 621, 541, 33)), Line(3, "help you with?", new(255, 657, 163, 29)), Line(4, "CONTINUE", new(255, 734, 193, 43))], "en");
        var grouped = ScreenAnalysis.GroupParagraphs(document); Assert.Equal(2, grouped.Blocks.Count); Assert.Equal("JOYCE MESSIER — Better not to tie the forestay to the backstay on this. I hope there is something else I can help you with?", grouped.Blocks[0].Text); Assert.Equal(3, grouped.Blocks[0].LineBounds.Count);
    }
    [Fact]
    public void ParagraphsKeepSpeakersAndColumnsSeparate()
    {
        var document = new OcrDocument([new(0, "JOYCE MESSIER — First speaker", new(10, 20, 300, 20), []), new(1, "continues her thought.", new(10, 46, 260, 20), []), new(2, "YOU — A different response", new(10, 72, 300, 20), []), new(3, "Other column", new(500, 20, 140, 20), [])], "en");
        var grouped = ScreenAnalysis.GroupParagraphs(document); Assert.Equal(3, grouped.Blocks.Count); Assert.Contains(grouped.Blocks, b => b.Text == "JOYCE MESSIER — First speaker continues her thought.");
    }
    [Fact]
    public void WordHitTestingUsesPhysicalBoundsAndTolerance()
    {
        var document = new OcrDocument([new(0, "better", new(-200, 100, 80, 20), [new("better", new(-200, 100, 80, 20))])], "en"); Assert.Equal("better", ScreenAnalysis.WordAt(document, -150, 108)?.Text); Assert.Null(ScreenAnalysis.WordAt(document, 100, 100));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task YandexUsesDocumentedAuthorizationAndFolderRules(bool iam)
    {
        using var http = new HttpClient(new Handler(async request => { Assert.Equal("https://translate.api.cloud.yandex.net/translate/v2/translate", request.RequestUri!.AbsoluteUri); Assert.Equal((iam ? "Bearer " : "Api-Key ") + "contract-test-key", request.Headers.GetValues("Authorization").Single()); using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal(iam, body.RootElement.TryGetProperty("folderId", out _)); Assert.False(body.RootElement.TryGetProperty("sourceLanguageCode", out _)); Assert.Equal("PLAIN_TEXT", body.RootElement.GetProperty("format").GetString()); return Json(new { translations = new[] { new { text = "Привет", detectedLanguageCode = "en" } } }); }));
        var result = await new TranslationProvider(http, new() { Provider = "Yandex Translate", YandexIamToken = iam, YandexFolderId = "folder" }, new Keys()).TranslateAsync(new(["Hello"], "auto", "ru"), default); Assert.Equal(new TranslationItem("Привет", "en"), Assert.Single(result));
    }
    [Fact]
    public async Task YandexBatchesUnderCharacterLimitAndKeepsOrder()
    {
        var requests = 0; using var http = new HttpClient(new Handler(async request => { requests++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var texts = body.RootElement.GetProperty("texts").EnumerateArray().Select(t => t.GetString()!).ToArray(); Assert.True(texts.Sum(t => t.Length) <= 9000); return Json(new { translations = texts.Select(t => new { text = "T" + t }) }); }));
        var input = Enumerable.Range(0, 4).Select(i => i + new string('a', 3500)).ToArray(); var result = await new TranslationProvider(http, new() { Provider = "Yandex Translate" }, new Keys()).TranslateAsync(new(input, "en", "ru"), default); Assert.Equal(2, requests); Assert.Equal(input.Select(t => "T" + t), result.Select(r => r.Text));
    }
    [Fact]
    public async Task OllamaTranslateGemmaGetsSingleProfessionalTranslationPrompt()
    {
        using var http = new HttpClient(new Handler(async request => { Assert.EndsWith("/api/chat", request.RequestUri!.AbsoluteUri); using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var messages = body.RootElement.GetProperty("messages"); Assert.Equal(1, messages.GetArrayLength()); Assert.Contains("professional English (en) to Russian (ru) translator", messages[0].GetProperty("content").GetString()); Assert.False(body.RootElement.TryGetProperty("format", out _)); return Json(new { message = new { content = "Лучше не усложнять." } }); }));
        var result = await new TranslationProvider(http, new() { Provider = "Ollama" }, new Keys()).TranslateAsync(new(["Better not complicate things."], "en", "ru"), default); Assert.Equal("Лучше не усложнять.", Assert.Single(result).Text);
    }
    [Theory]
    [InlineData("YandexGPT")]
    [InlineData("OpenAI compatible")]
    [InlineData("Ollama")]
    public async Task LanguageModelRestoresIdOrderAndIncludesContext(string provider)
    {
        using var http = new HttpClient(new Handler(async request => { using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); Assert.Contains("pirate captain", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()); if (provider == "YandexGPT") Assert.Equal("folder", request.Headers.GetValues("OpenAI-Project").Single()); var content = "{\"translations\":[{\"id\":1,\"text\":\"Два\",\"sourceLanguage\":\"en\"},{\"id\":0,\"text\":\"Один\",\"sourceLanguage\":\"en\"}]}"; return provider == "Ollama" ? Json(new { message = new { content } }) : Json(new { choices = new[] { new { message = new { content } } } }); }));
        var result = await new TranslationProvider(http, new() { Provider = provider, YandexFolderId = "folder", CompatibleModel = "test", OllamaModel = "qwen3:4b" }, new Keys()).TranslateAsync(new(["One", "Two"], "en", "ru", "pirate captain"), default); Assert.Equal(new[] { "Один", "Два" }, result.Select(r => r.Text));
    }
    [Fact]
    public async Task DuplicateLanguageModelIdsAreRejected()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new { message = new { content = "{\"translations\":[{\"id\":0,\"text\":\"a\"},{\"id\":0,\"text\":\"b\"}]}" } })))); await Assert.ThrowsAsync<UserFacingException>(() => new TranslationProvider(http, new() { Provider = "Ollama", OllamaModel = "qwen3:4b" }, new Keys()).TranslateAsync(new(["One", "Two"], "en", "ru"), default));
    }
    [Fact]
    public async Task GigaChatExchangesAuthorizationKeyForBearerToken()
    {
        var oauth = 0; using var http = new HttpClient(new Handler(async request => { if (request.RequestUri!.AbsolutePath.EndsWith("oauth")) { oauth++; Assert.Equal("Basic contract-test-key", request.Headers.GetValues("Authorization").Single()); Assert.True(Guid.TryParse(request.Headers.GetValues("RqUID").Single(), out _)); Assert.Contains("GIGACHAT_API_PERS", await request.Content!.ReadAsStringAsync()); return Json(new { access_token = "token", expires_at = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds() }); } Assert.Equal("Bearer token", request.Headers.GetValues("Authorization").Single()); return Json(new { choices = new[] { new { message = new { content = "{\"translations\":[{\"id\":0,\"text\":\"Привет\",\"sourceLanguage\":\"en\"}]}" } } } }); }));
        var provider = new TranslationProvider(http, new() { Provider = "GigaChat" }, new Keys()); await provider.TranslateAsync(new(["Hello"], "en", "ru"), default); await provider.TranslateAsync(new(["Again"], "en", "ru"), default); Assert.Equal(1, oauth);
    }
    [Fact]
    public async Task OfflineImportsAreTransactionalAndKeepUnicodeAndStructuredDefinitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "gaame-dictionary-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { var store = new OfflineDictionaryStore(root); var zip = Path.Combine(root, "Japanese.zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { await using var stream = archive.CreateEntry("term_bank_1.json").Open(); await JsonSerializer.SerializeAsync(stream, new object[] { new object[] { "日本語", "にほんご", "", "", 1, new object[] { new { content = new[] { new { content = "Japanese language" } } } } } }); } Assert.Equal(1, await store.ImportAsync(zip)); Assert.Contains("Japanese language", Assert.Single(await store.LookupAsync("日本語")).Definition); var malformed = Path.Combine(root, "Japanese.jsonl"); await File.WriteAllTextAsync(malformed, "{bad-json"); await Assert.ThrowsAsync<UserFacingException>(() => store.ImportAsync(malformed)); Assert.Single(await store.LookupAsync("日本語")); var cedict = Path.Combine(root, "cedict.txt"); await File.WriteAllTextAsync(cedict, "傳統 传统 [chuan2 tong3] /tradition/traditional/\n"); Assert.Equal(2, await store.ImportAsync(cedict)); Assert.Equal("chuan2 tong3", Assert.Single(await store.LookupAsync("传统")).Phonetic); }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task AnkiPackageContainsValidDatabaseStableGuidsAndMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "gaame-anki-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { var media = new LocalMediaStore(Path.Combine(root, "media")); var image = await media.SaveAsync([1, 2, 3], ".png"); var words = new[] { new VocabularyEntry { Word = "better", Translation = "лучше", SourceLanguage = "en", TargetLanguage = "ru", Definition = "A better option", ScreenshotFile = image } }; var package = Path.Combine(root, "words.apkg"); await AnkiPackageExporter.ExportAsync(package, words, media, true); using var zip = ZipFile.OpenRead(package); Assert.NotNull(zip.GetEntry("media")); Assert.NotNull(zip.GetEntry("0")); var db = Path.Combine(root, "collection.anki2"); zip.GetEntry("collection.anki2")!.ExtractToFile(db); await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString()); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", await command.ExecuteScalarAsync()); command.CommandText = "SELECT count(*) FROM cards"; Assert.Equal(2L, await command.ExecuteScalarAsync()); command.CommandText = "SELECT flds FROM notes"; var fields = (string)(await command.ExecuteScalarAsync())!; Assert.Equal(12, fields.Split('\u001f').Length); Assert.Contains("A better option", fields); }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
