using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Xunit;

namespace GaameTranslate.Tests;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaameTranslate.Tests", Guid.NewGuid().ToString("N"));
    private LocalDatabase db = null!;
    public async Task InitializeAsync() { db = new(directory); await db.InitializeAsync(); }
    public Task DisposeAsync() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); return Task.CompletedTask; }
    private static VocabularyEntry Word(string word = "旅人") => new() { Word = word, Translation = "путник", SourceLanguage = "ja", TargetLanguage = "ru", Context = "旅人ですね。", Notes = "notes", Tags = "RPG" };
    [Fact] public async Task VocabularyPersistsAcrossInstances()
    {
        var saved = await db.SaveWordAsync(Word()); var reopened = new LocalDatabase(directory); var entries = await reopened.GetWordsAsync("旅人");
        Assert.Single(entries); Assert.Equal(saved, entries[0]);
    }
    [Fact] public async Task DuplicateIncrementsEncountersWithoutOverwritingNotes()
    {
        await db.SaveWordAsync(Word(" Hello ")); await db.SaveWordAsync(Word("hello") with { Notes = "different" });
        var entry = Assert.Single(await db.GetWordsAsync()); Assert.Equal(2, entry.Encounters); Assert.Equal("notes", entry.Notes);
    }
    [Fact] public async Task SeparateLanguagePairsAreNotDuplicates()
    {
        await db.SaveWordAsync(Word()); await db.SaveWordAsync(Word() with { TargetLanguage = "en" }); Assert.Equal(2, (await db.GetWordsAsync()).Count);
    }
    [Fact] public async Task EditReviewFilterAndDelete()
    {
        var word = await db.SaveWordAsync(Word()); await db.UpdateWordAsync(ReviewScheduler.Apply(word with { Translation = "traveler" }, ReviewGrade.Easy, DateTimeOffset.UtcNow));
        Assert.Empty(await db.GetWordsAsync(dueOnly:true)); Assert.Single(await db.GetWordsAsync("traveler", "ja"));
        await db.DeleteWordAsync(word.Id); Assert.Empty(await db.GetWordsAsync());
    }
    [Fact] public async Task ConcurrentDuplicateSavesRemainAtomic()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => db.SaveWordAsync(Word()))));
        Assert.Equal(8, Assert.Single(await db.GetWordsAsync()).Encounters);
    }
    public static TranslationSession Session(string source) => new(Guid.NewGuid(), DateTimeOffset.UtcNow, new(0,0,100,100), "en", "ru", "Test", "Region", [new(new(0, source, new(0,0,40,20), []), "Привет")], new(1,2,3,6));
    [Fact] public async Task HistoryRetentionSearchAndDelete()
    {
        await db.AddHistoryAsync(Session("first"), 2); await db.AddHistoryAsync(Session("second"), 2); var last = Session("third"); await db.AddHistoryAsync(last, 2);
        Assert.Equal(2, (await db.GetHistoryAsync()).Count); Assert.Empty(await db.GetHistoryAsync("first")); Assert.Single(await db.GetHistoryAsync("Привет").ContinueWith(t => t.Result.Where(s=>s.Id==last.Id).ToArray()));
        await db.DeleteHistoryAsync(last.Id); Assert.Single(await db.GetHistoryAsync()); await db.ClearHistoryAsync(); Assert.Empty(await db.GetHistoryAsync());
    }
    [Fact] public async Task ZeroRetentionStoresNothing() { await db.AddHistoryAsync(Session("secret"), 0); Assert.Empty(await db.GetHistoryAsync()); }
    [Fact] public async Task SettingsRoundTripAndAtomicReplacement()
    {
        var store = new SettingsStore(directory); var settings = new AppSettings { Provider = "LibreTranslate", SourceLanguage = "ja", LastRegion = new(-1920, 50, 500, 200) };
        await store.SaveAsync(settings); Assert.Equal(settings, await store.LoadAsync()); Assert.False(File.Exists(Path.Combine(directory,"settings.json.tmp")));
    }
    [Fact] public async Task CorruptSettingsArePreserved()
    {
        var path = Path.Combine(directory,"settings.json"); await File.WriteAllTextAsync(path,"{broken");
        await Assert.ThrowsAsync<UserFacingException>(()=>new SettingsStore(directory).LoadAsync()); Assert.Equal("{broken", await File.ReadAllTextAsync(path));
    }
    [Fact] public void CsvRoundTripHandlesCommasQuotesMultilineAndFormulaInjection()
    {
        var word = Word("=HYPERLINK(\"bad\")") with { Translation = "one, two", Context = "line one\nline two", Notes="\"quote\"" };
        var csv = VocabularyCsv.Export([word]); Assert.Contains("'=HYPERLINK", csv);
        var result = Assert.Single(VocabularyCsv.Import(csv)); Assert.Equal(word.Word,result.Word); Assert.Equal(word.Context,result.Context); Assert.Equal(word.Notes,result.Notes);
    }
    [Fact] public void InvalidCsvFailsBeforeImport() => Assert.Throws<UserFacingException>(()=>VocabularyCsv.Import("Word,Translation\nhello,world"));
}
