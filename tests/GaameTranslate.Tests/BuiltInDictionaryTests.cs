using System.Text.Json;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Xunit;

namespace GaameTranslate.Tests;

public sealed class BuiltInDictionaryTests
{
    [Fact] public void NewInstallIsReadyWithoutCredentials()
    {
        var settings = new AppSettings().Validate();
        Assert.Equal(ProviderCatalog.BuiltInDictionary, settings.Provider);
        Assert.False(ProviderCatalog.RequiresKey(settings));
        Assert.Equal("None", settings.AssistantProvider);
        Assert.False(settings.EnableOnlineDictionary);
        Assert.True(BuiltInDictionaryProvider.EntryCount > 50000);
    }
    [Theory]
    [InlineData("traveler", "путешественник")]
    [InlineData("JOURNEY", "путешествие")]
    [InlineData("sword", "меч")]
    [InlineData("key", "ключ")]
    public async Task KnownWordsHaveUsefulRussianMeanings(string word, string expected)
    {
        var result = Assert.Single(await new BuiltInDictionaryProvider().TranslateAsync(new([word], "auto", "ru"), default));
        Assert.Contains(expected, result.Text);
        Assert.Equal("en", result.SourceLanguage);
        Assert.True(result.IsDictionaryTranslation);
    }
    [Fact] public async Task PunctuationUnknownWordsAndLineBreaksArePreserved()
    {
        var result = await new BuiltInDictionaryProvider().TranslateAsync(new(["Hello, traveler!\nZqxvillage 42.", "sword"], "en", "ru"), default);
        Assert.Equal(2, result.Count);
        Assert.Contains(", ", result[0].Text); Assert.Contains("!\nZqxvillage 42.", result[0].Text);
        Assert.Matches("[А-Яа-яЁё]", result[0].Text);
    }
    [Fact] public async Task UnknownSingleWordIsNotReportedAsSuccessfulTranslation()
    {
        var error = await Assert.ThrowsAsync<UserFacingException>(() => new BuiltInDictionaryProvider().TranslateAsync(new(["qzxvillage"], "en", "ru"), default));
        Assert.Contains("not in the built-in dictionary", error.LocalizedMessage.Key);
    }
    [Fact] public async Task DictionaryPhrasesWinOverIndividualWords()
    {
        var result = Assert.Single(await new BuiltInDictionaryProvider().TranslateAsync(new(["Good morning, traveler!"], "en", "ru"), default));
        Assert.StartsWith("доброго утра, ", result.Text.ToLowerInvariant());
        Assert.EndsWith("!", result.Text);
    }
    [Theory]
    [InlineData("traveller's")]
    [InlineData("travellers")]
    public void CommonInflectionsCanUseTheBaseWord(string word)
        => Assert.Contains("путешественник", string.Join("; ", BuiltInDictionaryProvider.Lookup(word)));
    [Theory]
    [InlineData("ja", "ru", "hello")]
    [InlineData("en", "de", "hello")]
    [InlineData("ru", "en", "привет")]
    [InlineData("auto", "ru", "こんにちは")]
    [InlineData("auto", "ru", "привет")]
    public async Task UnsupportedLanguagesAreActionable(string source, string target, string text)
    {
        var error = await Assert.ThrowsAsync<UserFacingException>(() => new BuiltInDictionaryProvider().TranslateAsync(new([text], source, target), default));
        Assert.Contains("English → Russian only", error.LocalizedMessage.Key);
    }
    [Fact] public async Task CancellationIsHonored()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BuiltInDictionaryProvider().TranslateAsync(new(["hello"], "en", "ru"), cancellation.Token));
    }
    [Fact] public async Task CachedAndSerializedResultsRetainDictionaryProvenance()
    {
        var provider = new BuiltInDictionaryProvider(); var cache = new TranslationCache();
        var request = new TranslationRequest(["hello"], "en", "ru");
        await cache.TranslateAsync(provider, request, "offline", default);
        var item = Assert.Single(await cache.TranslateAsync(provider, request, "offline", default));
        Assert.True(JsonSerializer.Deserialize<TranslationItem>(JsonSerializer.Serialize(item))!.IsDictionaryTranslation);
        var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, new(), "en", "ru", provider.Name, "Text", [], new(0, 0, 0, 0)) { IsDictionaryTranslation = true };
        Assert.True(JsonSerializer.Deserialize<TranslationSession>(JsonSerializer.Serialize(session))!.IsDictionaryTranslation);
    }
    [Fact] public async Task ProviderAdapterUsesNoNetworkForBuiltInMode()
    {
        using var http = new HttpClient(new NoNetwork());
        var result = await new TranslationProvider(http, new AppSettings(), new NoKeys()).TranslateAsync(new(["hello"], "en", "ru"), default);
        Assert.True(Assert.Single(result).IsDictionaryTranslation);
    }
    [Fact] public void RussianLettersArePreservedDuringDictionaryConversion()
    {
        Assert.Contains(BuiltInDictionaryProvider.Lookup("green"), value => value.Contains('ё'));
        Assert.Contains(BuiltInDictionaryProvider.Lookup("hero"), value => value.Contains('й'));
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Offline translation attempted network access.");
    }
    private sealed class NoKeys : ICredentialStore
    {
        public string? Read(string name) => throw new InvalidOperationException("Offline translation attempted credential access.");
        public void Write(string name, string value) => throw new NotSupportedException();
        public void Delete(string name) => throw new NotSupportedException();
    }
}
