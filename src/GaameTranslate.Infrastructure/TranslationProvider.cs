using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

/// <summary>Official HTTP APIs only. Credentials and source text never enter diagnostics.</summary>
public sealed class TranslationProvider(HttpClient client, AppSettings settings, ICredentialStore credentials) : ITranslationProvider
{
    public string Name => settings.Provider;
    public async Task<IReadOnlyList<TranslationItem>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Texts.Count == 0) return [];
        if (Name == ProviderCatalog.BuiltInDictionary) return await new BuiltInDictionaryProvider().TranslateAsync(request, cancellationToken);
        if (ProviderCatalog.IsLanguageModel(Name)) return await new LanguageModelClient(client, credentials).TranslateAsync(settings, request, cancellationToken);
        if (Name == "Yandex Translate" && request.Texts.Any(t => t.Length > 9000)) throw new UserFacingException("Для Яндекс Переводчика выберите фрагмент короче 9 000 символов или используйте языковую модель.");
        if (request.Texts.Any(t => t.Length > 15000)) throw new UserFacingException("This text block is too long. Select a smaller area (up to 15,000 characters per block).");
        var output = new List<TranslationItem>();
        // Stay below both provider item and request-size limits, including JSON escaping overhead.
        var batch = new List<string>(); var characters = 0;
        foreach (var text in request.Texts)
        {
            if (batch.Count > 0 && (batch.Count >= 40 || characters + text.Length > (Name == "Yandex Translate" ? 9000 : 16000)))
            {
                output.AddRange(await SendBatchAsync(request with { Texts = batch.ToArray() }, cancellationToken));
                batch.Clear(); characters = 0;
            }
            batch.Add(text); characters += text.Length;
        }
        if (batch.Count > 0) output.AddRange(await SendBatchAsync(request with { Texts = batch }, cancellationToken));
        return output;
    }
    private async Task<IReadOnlyList<TranslationItem>> SendBatchAsync(TranslationRequest input, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TranslationTimeoutSeconds));
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                using var message = BuildRequest(input);
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
                {
                    var retryAfter = response.Headers.RetryAfter;
                    var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMilliseconds(500 * (attempt + 1));
                    if (delay > TimeSpan.FromSeconds(8)) throw new UserFacingException("The provider is rate limiting requests. Wait a little before translating again.");
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, timeout.Token).ConfigureAwait(false);
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new UserFacingException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The translation provider rejected the key. Check your provider, key and Azure region in Settings.",
                    HttpStatusCode.TooManyRequests => "The translation provider is rate limiting requests. Try again later.",
                    (HttpStatusCode)456 => "Your DeepL character quota has been used. Check your API plan.",
                    HttpStatusCode.BadRequest => "The provider rejected this language pair or text. Check the installed models or provider language support.",
                    _ => L.M("The translation provider returned HTTP {0}. Check its availability and your configuration.", (int)response.StatusCode)
                });
                // Bound responses even when a local/custom server is misconfigured.
                await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                timeout.Token.ThrowIfCancellationRequested();
                return ParseResponse(json.RootElement, input);
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested) { throw new UserFacingException(L.M("Переводчик не ответил за {0} с. Проверьте сервис или увеличьте тайм-аут.", settings.TranslationTimeoutSeconds), e); }
        catch (HttpRequestException e) { throw new UserFacingException("Cannot reach the translation provider. Check your connection or start your local LibreTranslate service.", e); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new UserFacingException("The provider returned an invalid response. Check the selected API endpoint.", e); }
    }
    private HttpRequestMessage BuildRequest(TranslationRequest input)
    {
        var key = credentials.Read(Name) ?? "";
        if (Name != "LibreTranslate" && string.IsNullOrWhiteSpace(key)) throw new UserFacingException("Add your translation API key in Settings, or select a local LibreTranslate service.");
        if (Name == "Yandex Translate")
        {
            var body = new Dictionary<string, object> { ["texts"] = input.Texts, ["targetLanguageCode"] = input.Target, ["format"] = "PLAIN_TEXT" };
            if (input.Source != "auto") body["sourceLanguageCode"] = Languages.Base(input.Source);
            if (settings.YandexIamToken)
            {
                if (string.IsNullOrWhiteSpace(settings.YandexFolderId)) throw new UserFacingException("Для IAM-токена Яндекса укажите folder ID в настройках.");
            }
            if (!string.IsNullOrWhiteSpace(settings.YandexFolderId)) body["folderId"] = settings.YandexFolderId;
            var message = new HttpRequestMessage(HttpMethod.Post, "https://translate.api.cloud.yandex.net/translate/v2/translate") { Content = JsonContent.Create(body) };
            message.Headers.TryAddWithoutValidation("Authorization", (settings.YandexIamToken ? "Bearer " : "Api-Key ") + key);
            return message;
        }
        if (Name.StartsWith("DeepL", StringComparison.Ordinal))
        {
            var body = new Dictionary<string, object> { ["text"] = input.Texts, ["target_lang"] = input.Target == "en" ? "EN-US" : input.Target.ToUpperInvariant() };
            if (input.Source != "auto") body["source_lang"] = Languages.Base(input.Source).ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(input.Context)) body["context"] = input.Context[..Math.Min(5000, input.Context.Length)];
            var message = new HttpRequestMessage(HttpMethod.Post, Name == "DeepL Free" ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate") { Content = JsonContent.Create(body) };
            message.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + key);
            return message;
        }
        if (Name == "Azure")
        {
            var uri = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=" + Uri.EscapeDataString(input.Target);
            if (input.Source != "auto") uri += "&from=" + Uri.EscapeDataString(Languages.Base(input.Source));
            var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(input.Texts.Select(t => new { Text = t })) };
            message.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);
            if (!string.IsNullOrWhiteSpace(settings.AzureRegion)) message.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", settings.AzureRegion);
            return message;
        }
        var endpoint = AppSettings.ValidateEndpoint(settings.LibreEndpoint).AbsoluteUri.TrimEnd('/') + "/translate";
        var payload = new Dictionary<string, object> { ["q"] = input.Texts, ["source"] = input.Source == "auto" ? "auto" : Languages.Base(input.Source), ["target"] = input.Target, ["format"] = "text" };
        if (key.Length > 0) payload["api_key"] = key;
        return new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(payload) };
    }
    private IReadOnlyList<TranslationItem> ParseResponse(JsonElement json, TranslationRequest input)
    {
        List<TranslationItem> items;
        if (Name == "Yandex Translate") items = json.GetProperty("translations").EnumerateArray().Select(t => new TranslationItem(t.GetProperty("text").GetString()!, t.TryGetProperty("detectedLanguageCode", out var code) ? code.GetString()! : input.Source)).ToList();
        else if (Name.StartsWith("DeepL", StringComparison.Ordinal)) items = json.GetProperty("translations").EnumerateArray()
            .Select(t => new TranslationItem(t.GetProperty("text").GetString()!, t.TryGetProperty("detected_source_language", out var l) ? l.GetString()!.ToLowerInvariant() : input.Source)).ToList();
        else if (Name == "Azure") items = json.EnumerateArray().Select(t => new TranslationItem(t.GetProperty("translations")[0].GetProperty("text").GetString()!,
            t.TryGetProperty("detectedLanguage", out var l) ? l.GetProperty("language").GetString()! : input.Source)).ToList();
        else
        {
            var translated = json.GetProperty("translatedText");
            var texts = translated.ValueKind == JsonValueKind.Array ? translated.EnumerateArray().Select(t => t.GetString()!).ToArray() : [translated.GetString()!];
            var detected = json.TryGetProperty("detectedLanguage", out var d) ? d : default;
            items = texts.Select((t, i) => new TranslationItem(t, detected.ValueKind == JsonValueKind.Array && i < detected.GetArrayLength() ? detected[i].GetProperty("language").GetString()!
                : detected.ValueKind == JsonValueKind.Object ? detected.GetProperty("language").GetString()! : input.Source)).ToList();
        }
        if (items.Count != input.Texts.Count || items.Any(t => string.IsNullOrWhiteSpace(t.Text))) throw new UserFacingException("The provider returned incomplete translations. Try again with a smaller region.");
        return items;
    }
}

/// <summary>Bounded process-local cache. No recognized text is persisted here.</summary>
public sealed class TranslationCache
{
    private readonly ConcurrentDictionary<string, TranslationItem> cache = new();
    public async Task<IReadOnlyList<TranslationItem>> TranslateAsync(ITranslationProvider provider, TranslationRequest request, string configurationIdentity, CancellationToken token)
    {
        var keys = request.Texts.Select(t => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { provider.Name, configurationIdentity, request.Source, request.Target, request.Context, request.PreserveUnknownText ? "preserve-unknown" : "strict-word", t }))))).ToArray();
        // Read hits before adding; clearing the bounded cache cannot invalidate a result in flight.
        var results = keys.Select(k => cache.TryGetValue(k, out var hit) ? hit : null).ToArray();
        var missing = Enumerable.Range(0, keys.Length).Where(i => results[i] is null).ToArray();
        if (missing.Length > 0)
        {
            var fetched = await provider.TranslateAsync(request with { Texts = missing.Select(i => request.Texts[i]).ToArray() }, token).ConfigureAwait(false);
            if (fetched.Count != missing.Length) throw new UserFacingException("The provider returned an unexpected number of translations.");
            if (cache.Count + missing.Length > 2000) cache.Clear();
            for (var j = 0; j < missing.Length; j++)
            {
                // A temporary fallback must not mask the recovered provider behind a permanent cache hit.
                if (!fetched[j].IsDictionaryFallback) cache[keys[missing[j]]] = fetched[j];
                results[missing[j]] = fetched[j];
            }
        }
        token.ThrowIfCancellationRequested();
        return results.Select(r => r!).ToArray();
    }
    public void Clear() => cache.Clear();
}
