using System.Net;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Xunit;

namespace GaameTranslate.Tests;

public sealed class TranslationFailoverTests
{
    private static readonly TranslationRequest request = new(["hello", "sword"], "en", "ru");
    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(456)] [InlineData(503)]
    public async Task ProviderErrorsUseLocalDictionaryWithoutChangingMapping(int status)
    {
        var primary = new Stub((_, _) => throw new UserFacingException(UiText.M("The translation provider returned HTTP {0}. Check its availability and your configuration.", status)));
        var result = await new TranslationFailover().Wrap(primary, "config").TranslateAsync(request, default);
        Assert.Equal(2, result.Count); Assert.Contains("привет", result[0].Text); Assert.Contains("меч", result[1].Text);
        Assert.All(result, item => { Assert.True(item.IsDictionaryTranslation); Assert.True(item.IsDictionaryFallback); Assert.Equal("en", item.SourceLanguage); });
        Assert.Equal("Primary", primary.Name);
    }
    [Fact] public async Task NetworkAndInternalTimeoutErrorsFallBack()
    {
        foreach (var error in new Exception[] { new HttpRequestException("offline"), new OperationCanceledException("provider timeout"), new TimeoutException() })
        {
            var result = await new TranslationFailover().Wrap(new Stub((_, _) => throw error), "config").TranslateAsync(request, default);
            Assert.All(result, item => Assert.True(item.IsDictionaryFallback));
        }
    }
    [Fact] public async Task HungAdapterIsBoundedEvenIfItIgnoresCancellation()
    {
        var late = new TaskCompletionSource<IReadOnlyList<TranslationItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken adapterToken = default;
        var provider = new TranslationFailover(responseBudget: TimeSpan.FromMilliseconds(20)).Wrap(new Stub((_, token) => { adapterToken = token; return late.Task; }), "config");
        var result = await provider.TranslateAsync(request, default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(result[0].IsDictionaryFallback); Assert.True(adapterToken.IsCancellationRequested);
        late.SetException(new HttpRequestException("late error"));
    }
    [Fact] public async Task UserCancellationDoesNotTranslateOrOpenTheCircuit()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primary = new Stub(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return []; });
        var fallback = new TranslationFailover(); var provider = fallback.Wrap(primary, "config");
        var pending = provider.TranslateAsync(request, cancellation.Token); await entered.Task; cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        primary.Action = (_, _) => Task.FromResult<IReadOnlyList<TranslationItem>>([new("healthy", "en"), new("healthy", "en")]);
        Assert.False((await provider.TranslateAsync(request, default))[0].IsDictionaryFallback); Assert.Equal(2, primary.Calls);
    }
    [Fact] public async Task CooldownSkipsBrokenServiceAndAutomaticallyRetriesAfterRecovery()
    {
        var clock = new Clock(); var primary = new Stub((_, _) => throw new HttpRequestException());
        var provider = new TranslationFailover(clock).Wrap(primary, "config");
        await provider.TranslateAsync(request, default); await provider.TranslateAsync(request, default); Assert.Equal(1, primary.Calls);
        primary.Action = (_, _) => Task.FromResult<IReadOnlyList<TranslationItem>>([new("recovered", "en"), new("recovered", "en")]);
        clock.Advance(TimeSpan.FromSeconds(16));
        var result = await provider.TranslateAsync(request, default);
        Assert.Equal("recovered", result[0].Text); Assert.False(result[0].IsDictionaryFallback); Assert.Equal(2, primary.Calls);
    }
    [Fact] public async Task TemporaryDictionaryResultsDoNotPoisonProviderCache()
    {
        var primary = new Stub((_, _) => throw new HttpRequestException()); var manager = new TranslationFailover(); var cache = new TranslationCache(); var provider = manager.Wrap(primary, "config");
        Assert.True((await cache.TranslateAsync(provider, request, "config", default))[0].IsDictionaryFallback);
        manager.Clear(); primary.Action = (_, _) => Task.FromResult<IReadOnlyList<TranslationItem>>([new("real translation", "en"), new("real translation", "en")]);
        Assert.Equal("real translation", (await cache.TranslateAsync(provider, request, "config", default))[0].Text);
        await cache.TranslateAsync(provider, request, "config", default); Assert.Equal(2, primary.Calls);
    }
    [Fact] public async Task SeparateConfigurationsDoNotShareOutages()
    {
        var manager = new TranslationFailover(); var primary = new Stub((_, _) => throw new HttpRequestException());
        await manager.Wrap(primary, "broken-endpoint").TranslateAsync(request, default);
        primary.Action = (_, _) => Task.FromResult<IReadOnlyList<TranslationItem>>([new("new endpoint", "en"), new("new endpoint", "en")]);
        Assert.False((await manager.Wrap(primary, "other-endpoint").TranslateAsync(request, default))[0].IsDictionaryFallback);
    }
    [Theory]
    [InlineData("en", "de", "hello")]
    [InlineData("ja", "ru", "こんにちは")]
    [InlineData("auto", "ru", "привет")]
    public async Task UnsupportedLanguagePreservesTheActualProviderError(string source, string target, string text)
    {
        var original = new UserFacingException("provider failed"); var provider = new TranslationFailover().Wrap(new Stub((_, _) => throw original), "config");
        Assert.Same(original, await Assert.ThrowsAsync<UserFacingException>(() => provider.TranslateAsync(new([text], source, target), default)));
    }
    [Fact] public async Task MissingDictionaryWordIsHonestAndStillIdentifiesTheOutage()
    {
        var provider = new TranslationFailover().Wrap(new Stub((_, _) => throw new HttpRequestException()), "config");
        var error = await Assert.ThrowsAsync<UserFacingException>(() => provider.TranslateAsync(new(["qzxvillage"], "en", "ru"), default));
        Assert.True(error.IsDictionaryFallback); Assert.Contains("not in the built-in dictionary", error.Message);
    }
    [Fact] public async Task UnknownScreenLabelDoesNotDiscardTranslationsOfOtherBlocks()
    {
        var provider = new TranslationFailover().Wrap(new Stub((_, _) => throw new HttpRequestException()), "config");
        var result = await provider.TranslateAsync(new(["hello", "Qzxvillage", "sword"], "en", "ru") { PreserveUnknownText = true }, default);
        Assert.Contains("привет", result[0].Text); Assert.Equal("Qzxvillage", result[1].Text); Assert.Contains("меч", result[2].Text);
        Assert.All(result, item => Assert.True(item.IsDictionaryFallback));
    }
    [Fact] public async Task PreservedScreenTextCannotBecomeACachedWordTranslation()
    {
        var provider = new BuiltInDictionaryProvider(); var cache = new TranslationCache();
        var request = new TranslationRequest(["Qzxvillage"], "en", "ru") { PreserveUnknownText = true };
        Assert.Equal("Qzxvillage", (await cache.TranslateAsync(provider, request, "dictionary", default))[0].Text);
        await Assert.ThrowsAsync<UserFacingException>(() => cache.TranslateAsync(provider, request with { PreserveUnknownText = false }, "dictionary", default));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task EmptyOrIncompleteResponsesUseDictionary(bool empty)
    {
        var provider = new TranslationFailover().Wrap(new Stub((_, _) => Task.FromResult<IReadOnlyList<TranslationItem>>(empty ? [new("", "en"), new("", "en")] : [])), "config");
        Assert.All(await provider.TranslateAsync(request, default), item => Assert.True(item.IsDictionaryFallback));
    }
    private sealed class Stub(Func<TranslationRequest, CancellationToken, Task<IReadOnlyList<TranslationItem>>> action) : ITranslationProvider
    {
        public string Name => "Primary"; public int Calls { get; private set; }
        public Func<TranslationRequest, CancellationToken, Task<IReadOnlyList<TranslationItem>>> Action { get; set; } = action;
        public Task<IReadOnlyList<TranslationItem>> TranslateAsync(TranslationRequest request, CancellationToken token) { Calls++; return Action(request, token); }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
