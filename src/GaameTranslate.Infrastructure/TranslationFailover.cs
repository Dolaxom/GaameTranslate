using System.Collections.Concurrent;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

/// <summary>Bounded provider attempts with a local-only fallback and a short recovery cooldown.</summary>
public sealed class TranslationFailover(TimeProvider? timeProvider = null, TimeSpan? responseBudget = null, TimeSpan? retryDelay = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan budget = responseBudget ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan cooldown = retryDelay ?? TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<string, DateTimeOffset> retryAfter = new();
    private readonly BuiltInDictionaryProvider dictionary = new();
    public void Clear() => retryAfter.Clear();
    public ITranslationProvider Wrap(ITranslationProvider provider, string identity)
        => provider.Name == ProviderCatalog.BuiltInDictionary ? provider : new Provider(this, provider, identity);

    private async Task<IReadOnlyList<TranslationItem>> TranslateAsync(ITranslationProvider primary, string identity, TranslationRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Texts.Count == 0) return [];
        // An unsupported language must retain the real provider error, never produce an unrelated Russian result.
        if (!BuiltInDictionaryProvider.Supports(request)) return await primary.TranslateAsync(request, token);
        if (retryAfter.TryGetValue(identity, out var retry) && retry > clock.GetUtcNow()) return await FallbackAsync(request, token);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<IReadOnlyList<TranslationItem>>? pending = null;
        try
        {
            pending = primary.TranslateAsync(request, attempt.Token);
            var result = await pending.WaitAsync(budget, token);
            token.ThrowIfCancellationRequested();
            if (result.Count != request.Texts.Count || result.Any(item => string.IsNullOrWhiteSpace(item.Text)))
                throw new UserFacingException("The provider returned an unexpected number of translations.");
            retryAfter.TryRemove(identity, out _);
            return result;
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is UserFacingException or HttpRequestException or TimeoutException or OperationCanceledException)
        {
            attempt.Cancel();
            retryAfter[identity] = clock.GetUtcNow() + cooldown;
            return await FallbackAsync(request, token);
        }
        finally
        {
            // A misbehaving adapter may ignore cancellation. Observe late faults after timeout or user cancellation.
            if (pending is not null && !pending.IsCompletedSuccessfully) _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private async Task<IReadOnlyList<TranslationItem>> FallbackAsync(TranslationRequest request, CancellationToken token)
    {
        try { return (await dictionary.TranslateAsync(request, token)).Select(item => item with { IsDictionaryFallback = true }).ToArray(); }
        catch (UserFacingException error)
        {
            throw new UserFacingException(L.M("The translation service failed, and the built-in dictionary could not translate this selection. {0}", error.LocalizedMessage), error) { IsDictionaryFallback = true };
        }
    }
    private sealed class Provider(TranslationFailover owner, ITranslationProvider primary, string identity) : ITranslationProvider
    {
        public string Name => primary.Name;
        public Task<IReadOnlyList<TranslationItem>> TranslateAsync(TranslationRequest request, CancellationToken token) => owner.TranslateAsync(primary, identity, request, token);
    }
}
