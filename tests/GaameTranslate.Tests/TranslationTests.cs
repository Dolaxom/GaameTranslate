using System.Net;
using System.Text;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Xunit;

namespace GaameTranslate.Tests;

public class TranslationTests
{
    private sealed class Keys : ICredentialStore { public string? Read(string name) => "test-key"; public void Write(string n, string v) { } public void Delete(string n) { } }
    private sealed class Handler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handle(request, ++Count);
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    [Theory]
    [InlineData("DeepL Free", "{\"translations\":[{\"text\":\"Привет\",\"detected_source_language\":\"EN\"}]}")]
    [InlineData("Azure", "[{\"detectedLanguage\":{\"language\":\"en\"},\"translations\":[{\"text\":\"Привет\"}]}]")]
    [InlineData("LibreTranslate", "{\"translatedText\":[\"Привет\"],\"detectedLanguage\":[{\"language\":\"en\"}]}")]
    public async Task ParsesDocumentedResponseAndPreservesMapping(string provider, string response)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(response))); using var client = new HttpClient(handler);
        var result = await new TranslationProvider(client, new() { Provider = provider }, new Keys()).TranslateAsync(new(["Hello"], "auto", "ru"), default);
        Assert.Equal(new TranslationItem("Привет", "en"), Assert.Single(result));
    }
    [Fact]
    public async Task AuthFailureIsActionableAndNeverLeaksKey()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))); using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<UserFacingException>(() => new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["Hello"], "en", "ru"), default));
        Assert.Contains("key", error.Message); Assert.DoesNotContain("test-key", error.Message); Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task RetriesTransientFailuresWithinLimit()
    {
        var handler = new Handler((_, n) => Task.FromResult(n < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("{\"translations\":[{\"text\":\"Привет\"}]}"))); using var client = new HttpClient(handler);
        await new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["Hello"], "en", "ru"), default); Assert.Equal(3, handler.Count);
    }
    [Fact]
    public async Task InvalidResponseIsNotCachedAsSuccess()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{\"translations\":[]}"))); using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<UserFacingException>(() => new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["Hello"], "en", "ru"), default));
    }
    [Fact]
    public async Task CacheSeparatesLanguagesContextAndConfiguration()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{\"translations\":[{\"text\":\"Привет\"}]}"))); using var client = new HttpClient(handler);
        var provider = new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()); var cache = new TranslationCache(); var request = new TranslationRequest(["Hello"], "en", "ru");
        await cache.TranslateAsync(provider, request, "one", default); await cache.TranslateAsync(provider, request, "one", default); Assert.Equal(1, handler.Count);
        await cache.TranslateAsync(provider, request with { Context = "RPG" }, "one", default); await cache.TranslateAsync(provider, request, "two", default); Assert.Equal(3, handler.Count);
    }
    [Fact]
    public async Task SendsBatchAndDeepLContextWithoutCredentialsInUri()
    {
        var handler = new Handler(async (r, _) => { Assert.DoesNotContain("test-key", r.RequestUri!.ToString()); var body = await r.Content!.ReadAsStringAsync(); Assert.Contains("context", body); Assert.Contains("EN-US", body); Assert.Equal("DeepL-Auth-Key test-key", r.Headers.GetValues("Authorization").Single()); return Json("{\"translations\":[{\"text\":\"One\"},{\"text\":\"Two\"}]}"); });
        using var client = new HttpClient(handler); var result = await new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["a", "b"], "ja", "en", "RPG"), default); Assert.Equal(2, result.Count);
    }
    [Fact]
    public async Task RateLimitWithLongRetryAfterDoesNotSleepOrRetry()
    {
        var handler = new Handler((_, _) => { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1)); return Task.FromResult(response); });
        using var client = new HttpClient(handler); await Assert.ThrowsAsync<UserFacingException>(() => new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["hello"], "en", "ru"), default)); Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task LargeBatchSplitsAndKeepsOriginalOrdering()
    {
        var handler = new Handler(async (r, _) => { using var document = System.Text.Json.JsonDocument.Parse(await r.Content!.ReadAsStringAsync()); var texts = document.RootElement.GetProperty("text").EnumerateArray().Select(t => t.GetString()).ToArray(); return Json(System.Text.Json.JsonSerializer.Serialize(new { translations = texts.Select(t => new { text = "translated:" + t }) })); });
        using var client = new HttpClient(handler); var input = Enumerable.Range(0, 45).Select(n => n.ToString()).ToArray(); var result = await new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(input, "en", "ru"), default);
        Assert.Equal(2, handler.Count); Assert.Equal(input.Select(t => "translated:" + t), result.Select(r => r.Text));
    }
    [Fact]
    public async Task MalformedJsonIsReportedSafely()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{not-json"))); using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<UserFacingException>(() => new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["hello"], "en", "ru"), default)); Assert.Contains("invalid response", error.Message);
    }
    [Fact]
    public async Task CallerCancellationIsNotRewrittenAsTimeout()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var handler = new Handler((_, _) => Task.FromResult(Json("{}"))); using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new(["hello"], "en", "ru"), cancellation.Token));
    }
    [Fact]
    public async Task JapanesePayloadAndContextStayUnderDeepLBodyLimit()
    {
        var handler = new Handler(async (request, _) =>
        {
            Assert.True((await request.Content!.ReadAsByteArrayAsync()).Length < 128 * 1024);
            return Json("{\"translations\":[{\"text\":\"Result\"}]}");
        });
        using var client = new HttpClient(handler);
        await new TranslationProvider(client, new() { Provider = "DeepL Free" }, new Keys()).TranslateAsync(new([new string('漢', 15000)], "ja", "en", new string('漢', 10000)), default);
    }
}
