using System.Collections.Frozen;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaameTranslate.Core;

namespace GaameTranslate.Infrastructure;

/// <summary>Offline lexical hints, not sentence-level machine translation. No network or user data files.</summary>
public sealed class BuiltInDictionaryProvider : ITranslationProvider
{
    private static readonly Lazy<FrozenDictionary<string, string[]>> entries = new(Load);
    private static readonly Regex tokens = new(@"[A-Za-z]+(?:['’\-][A-Za-z]+)*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public string Name => ProviderCatalog.BuiltInDictionary;
    public static int EntryCount => entries.Value.Count;
    public static IReadOnlyList<string> Lookup(string word) => Find(word) ?? [];
    public static bool Supports(TranslationRequest request) => Languages.Base(request.Target) == "ru"
        && (Languages.Base(request.Source) == "en" || request.Source == "auto" && !request.Texts.Any(text => text.Any(c => char.IsLetter(c) && c > 127)))
        && request.Texts.All(text => text.Length <= 15000);

    public Task<IReadOnlyList<TranslationItem>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        => Task.Run(() => Translate(request, cancellationToken), cancellationToken);
    private static IReadOnlyList<TranslationItem> Translate(TranslationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Languages.Base(request.Target) != "ru" || request.Source != "auto" && Languages.Base(request.Source) != "en")
            throw new UserFacingException("The built-in dictionary supports English → Russian only. Choose these languages or set up a translation provider in Settings.");
        IReadOnlyList<TranslationItem> result = request.Texts.Select(text =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.Length > 15000) throw new UserFacingException("This text block is too long. Select a smaller area (up to 15,000 characters per block).");
            if (request.Source == "auto" && text.Any(c => char.IsLetter(c) && c > 127))
                throw new UserFacingException("The built-in dictionary supports English → Russian only. Choose these languages or set up a translation provider in Settings.");
            if (Find(text.Trim()) is { } exact) return Item(string.Join("; ", exact.Take(20)));
            var words = tokens.Matches(text);
            var translated = new System.Text.StringBuilder(); var position = 0; var found = 0;
            for (var i = 0; i < words.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var word = words[i]; translated.Append(text, position, word.Index - position);
                string[]? meanings = null; var last = i;
                // Prefer dictionary phrases, but never join across punctuation or line breaks.
                for (var end = Math.Min(words.Count - 1, i + 4); end >= i; end--)
                {
                    var phrase = text.Substring(word.Index, words[end].Index + words[end].Length - word.Index);
                    if (end > i && !Regex.IsMatch(phrase, @"^[A-Za-z'’\-]+(?: +[A-Za-z'’\-]+)+$")) continue;
                    meanings = Find(phrase); if (meanings is not null) { last = end; break; }
                }
                if (meanings is not null) { translated.Append(meanings[0]); found++; }
                else translated.Append(word.Value);
                position = words[last].Index + words[last].Length; i = last;
            }
            translated.Append(text, position, text.Length - position);
            if (words.Count == 1 && found == 0 && !request.PreserveUnknownText) throw new UserFacingException("This word is not in the built-in dictionary. Check the spelling or set up a translation provider in Settings.");
            return Item(translated.ToString());
        }).ToArray();
        return result;
    }
    private static TranslationItem Item(string text) => new(text, "en") { IsDictionaryTranslation = true };
    private static string[]? Find(string word)
    {
        var key = word.Trim().ToLowerInvariant().Replace('’', '\'');
        if (entries.Value.TryGetValue(key, out var exact)) return exact;
        // Conservative English inflection hints; exact entries always win.
        var candidates = new List<string>();
        if (key.Contains("eler", StringComparison.Ordinal)) candidates.Add(key.Replace("eler", "eller", StringComparison.Ordinal));
        if (key.EndsWith("'s")) candidates.Add(key[..^2]);
        if (key.Length > 4 && key.EndsWith("ies")) candidates.Add(key[..^3] + "y");
        if (key.Length > 4 && key.EndsWith("ing")) { candidates.Add(key[..^3]); candidates.Add(key[..^3] + "e"); if (key[^4] == key[^5]) candidates.Add(key[..^4]); }
        if (key.Length > 3 && key.EndsWith("ed")) { candidates.Add(key[..^1]); candidates.Add(key[..^2]); if (key.Length > 4 && key[^3] == key[^4]) candidates.Add(key[..^3]); }
        if (key.Length > 3 && key.EndsWith("es")) candidates.Add(key[..^2]);
        if (key.Length > 3 && key.EndsWith('s')) candidates.Add(key[..^1]);
        return candidates.Select(candidate => entries.Value.GetValueOrDefault(candidate)).FirstOrDefault(value => value is not null);
    }
    private static FrozenDictionary<string, string[]> Load()
    {
        using var stream = typeof(BuiltInDictionaryProvider).Assembly.GetManifestResourceStream("GaameTranslate.Infrastructure.Data.eng-rus.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(gzip)!.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
