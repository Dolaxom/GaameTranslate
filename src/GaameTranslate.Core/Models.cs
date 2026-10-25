using System.Text;
using System.Text.RegularExpressions;

namespace GaameTranslate.Core;

/// <summary>All capture and OCR coordinates are physical desktop pixels, including negative origins.</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsValid => double.IsFinite(X + Y + Width + Height) && Width >= 2 && Height >= 2;
    public PixelRect ToScreen(PixelRect capture, double scale = 1) =>
        new(capture.X + X / scale, capture.Y + Y / scale, Width / scale, Height / scale);
    public PixelRect Intersect(PixelRect other)
    {
        var x = Math.Max(X, other.X); var y = Math.Max(Y, other.Y);
        return new(x, y, Math.Max(0, Math.Min(Right, other.Right) - x), Math.Max(0, Math.Min(Bottom, other.Bottom) - y));
    }
}

public sealed record CapturedFrame(byte[] Bgra, int Width, int Height, PixelRect Bounds);
public sealed record OcrWord(string Text, PixelRect Bounds);
public sealed record OcrBlock(int Order, string Text, PixelRect Bounds, IReadOnlyList<OcrWord> Words, double? Confidence = null)
{
    public IReadOnlyList<PixelRect> LineBounds { get; init; } = [];
}
public sealed record OcrDocument(IReadOnlyList<OcrBlock> Blocks, string Language, double? TextAngle = null)
{
    public string Text => string.Join(Environment.NewLine, Blocks.Select(b => b.Text));
}
public sealed record TranslationItem(string Text, string SourceLanguage);
public sealed record TranslationRequest(IReadOnlyList<string> Texts, string Source, string Target, string Context = "");
public sealed record TranslatedBlock(OcrBlock Original, string Translation)
{
    public string? BackgroundColor { get; init; }
}
public sealed record PipelineTimings(double CaptureMs, double OcrMs, double TranslationMs, double TotalMs);
public sealed record TranslationSession(Guid Id, DateTimeOffset CreatedAt, PixelRect CaptureBounds,
    string SourceLanguage, string TargetLanguage, string Provider, string CaptureType,
    IReadOnlyList<TranslatedBlock> Blocks, PipelineTimings Timings)
{
    public string OriginalText => string.Join(Environment.NewLine, Blocks.Select(b => b.Original.Text));
    public string TranslatedText => string.Join(Environment.NewLine, Blocks.Select(b => b.Translation));
}
public enum OverlayMode { Floating, Spatial, Replacement, SidePanel }
public enum ReviewGrade { Again, Hard, Good, Easy }
public sealed record VocabularyEntry
{
    public long Id { get; init; }
    public string Word { get; init; } = "";
    public string Translation { get; init; } = "";
    public string SourceLanguage { get; init; } = "";
    public string TargetLanguage { get; init; } = "";
    public string Context { get; init; } = "";
    public string TranslatedContext { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Tags { get; init; } = "";
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public int Encounters { get; init; } = 1;
    public int Reviews { get; init; }
    public double IntervalDays { get; init; }
    public double Ease { get; init; } = 2.5;
    public DateTimeOffset DueAt { get; init; } = DateTimeOffset.UtcNow;
    public string Lemma { get; init; } = "";
    public string Phonetic { get; init; } = "";
    public string PartOfSpeech { get; init; } = "";
    public string Level { get; init; } = "";
    public string Definition { get; init; } = "";
    public string Synonyms { get; init; } = "";
    public string Examples { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string DictionarySource { get; init; } = "";
    public string ScreenshotFile { get; init; } = "";
    public string AudioFile { get; init; } = "";
}
public sealed record DictionaryArticle(string Headword, string Definition, string Source)
{
    public string Phonetic { get; init; } = "";
    public string PartOfSpeech { get; init; } = "";
    public string Level { get; init; } = "";
    public string Translation { get; init; } = "";
    public string ContextTranslation { get; init; } = "";
    public string Synonyms { get; init; } = "";
    public string Examples { get; init; } = "";
    public string Explanation { get; init; } = "";
    public bool Generated { get; init; }
}
public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}
public static class Languages
{
    public static readonly IReadOnlyList<LanguageChoice> Translation = [
        new("en", "English"), new("ru", "Русский"), new("ja", "日本語"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("zh", "中文"), new("ko", "한국어"),
        new("it", "Italiano"), new("pt", "Português"), new("uk", "Українська"), new("pl", "Polski")];
    public static string Base(string tag) => tag.Split('-')[0].ToLowerInvariant();
    public static string ResolveOcr(string requested, IEnumerable<string> installed)
    {
        var tags = installed.ToArray();
        if (requested == "auto") return tags.FirstOrDefault() ?? throw new UserFacingException("Install an OCR language in Windows Settings → Time & language → Language & region.");
        return tags.FirstOrDefault(t => t.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? tags.FirstOrDefault(t => Base(t) == Base(requested))
            ?? throw new UserFacingException($"The OCR pack for '{requested}' is not installed. Add it in Windows language settings, then restart GaameTranslate.");
    }
}
public static partial class TextProcessing
{
    [GeneratedRegex(@"[^\S\r\n]+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"(?<=[.!?。！？])\s+|(?<=[。！？])")]
    private static partial Regex Sentences();
    public static string Normalize(string text) => string.Join("\n", text.Normalize(NormalizationForm.FormKC)
        .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0));
    public static string WordKey(string text) => Normalize(text).ToUpperInvariant();
    public static IReadOnlyList<string> SplitSentences(string text) => Sentences().Split(text).Select(Normalize).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    // Windows OCR supplies language-specific word segmentation. Never split Japanese/Chinese on spaces.
    public static IReadOnlyList<string> SelectableWords(OcrBlock block) => block.Words.Select(w => w.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
}
public static class ReviewScheduler
{
    public static VocabularyEntry Apply(VocabularyEntry entry, ReviewGrade grade, DateTimeOffset now)
    {
        var ease = Math.Clamp(entry.Ease + (grade switch { ReviewGrade.Again => -.2, ReviewGrade.Hard => -.15, ReviewGrade.Easy => .15, _ => 0 }), 1.3, 3.5);
        var days = grade switch
        {
            ReviewGrade.Again => 10d / 1440,
            ReviewGrade.Hard => Math.Max(1, entry.IntervalDays * 1.2),
            ReviewGrade.Good => entry.Reviews == 0 ? 1 : Math.Max(2, entry.IntervalDays * ease),
            ReviewGrade.Easy => entry.Reviews == 0 ? 4 : Math.Max(4, entry.IntervalDays * ease * 1.3),
            _ => throw new ArgumentOutOfRangeException(nameof(grade))
        };
        days = Math.Min(3650, days);
        return entry with { Reviews = entry.Reviews + 1, IntervalDays = days, Ease = ease, DueAt = now.AddDays(days) };
    }
}
public sealed class UserFacingException(string message, Exception? inner = null) : Exception(message, inner);
