using System.Text.RegularExpressions;

namespace GaameTranslate.Core;

public static partial class ScreenAnalysis
{
    [GeneratedRegex(@"^(?:[A-ZА-ЯЁ][A-ZА-ЯЁ\s.'’\-]{2,40}\s*[—–:]|\d+[.)]\s|[•●▪]\s)")]
    private static partial Regex NewSpeaker();
    public static OcrDocument GroupParagraphs(OcrDocument document)
    {
        var groups = new List<List<OcrBlock>>();
        foreach (var line in MergeLineFragments(document).OrderBy(b => b.Bounds.Y).ThenBy(b => b.Bounds.X))
        {
            var candidate = groups.Where(g => CanJoin(g, line)).OrderBy(g => line.Bounds.Y - g[^1].Bounds.Bottom).FirstOrDefault();
            if (candidate is null) groups.Add([line]); else candidate.Add(line);
        }
        var result = groups.OrderBy(g => g.Min(b => b.Order)).Select((g, i) =>
        {
            var rect = Union(g.Select(b => b.Bounds)); var text = g[0].Text;
            foreach (var next in g.Skip(1))
            {
                var cjk = Languages.Base(document.Language) is "ja" or "zh";
                text = text.EndsWith('-') && !cjk && next.Text.Length > 0 && char.IsLower(next.Text[0]) ? text[..^1] + next.Text : text + (cjk ? "" : " ") + next.Text;
            }
            return new OcrBlock(i, TextProcessing.Normalize(text), rect, g.SelectMany(b => b.Words).ToArray(), g.All(b => b.Confidence.HasValue) ? g.Average(b => b.Confidence!.Value) : null)
            { LineBounds = g.SelectMany(b => b.LineBounds.Count > 0 ? b.LineBounds : [b.Bounds]).ToArray() };
        }).ToArray();
        return document with { Blocks = result };
    }
    private static IReadOnlyList<OcrBlock> MergeLineFragments(OcrDocument document)
    {
        var rows = new List<OcrBlock>();
        foreach (var fragment in document.Blocks.OrderBy(b => b.Bounds.X).ThenBy(b => b.Bounds.Y))
        {
            var previous = rows.Where(b =>
            {
                var h = Math.Min(b.Bounds.Height, fragment.Bounds.Height);
                var overlap = Math.Min(b.Bounds.Bottom, fragment.Bounds.Bottom) - Math.Max(b.Bounds.Y, fragment.Bounds.Y);
                var gap = fragment.Bounds.X - b.Bounds.Right;
                return overlap >= h * .65 && gap >= -h * .25 && gap <= h * 1.15 && b.Bounds.Height <= fragment.Bounds.Height * 1.65 && fragment.Bounds.Height <= b.Bounds.Height * 1.65;
            }).OrderBy(b => fragment.Bounds.X - b.Bounds.Right).FirstOrDefault();
            if (previous is null) { rows.Add(fragment); continue; }
            var bounds = Union([previous.Bounds, fragment.Bounds]); var cjk = Languages.Base(document.Language) is "ja" or "zh";
            var joined = previous with { Text = previous.Text + (cjk ? "" : " ") + fragment.Text, Bounds = bounds, Words = previous.Words.Concat(fragment.Words).ToArray(), LineBounds = [bounds], Order = Math.Min(previous.Order, fragment.Order) };
            rows[rows.IndexOf(previous)] = joined;
        }
        return rows;
    }
    private static bool CanJoin(IReadOnlyList<OcrBlock> group, OcrBlock line)
    {
        var previous = group[^1]; var height = Math.Max(4, previous.Bounds.Height); var gap = line.Bounds.Y - previous.Bounds.Bottom;
        if (gap < -height * .15 || gap > height * .78 || line.Bounds.Height < height * .55 || line.Bounds.Height > height * 1.65 || NewSpeaker().IsMatch(line.Text)) return false;
        var alignment = Math.Abs(group[0].Bounds.X - line.Bounds.X);
        var overlap = Math.Min(previous.Bounds.Right, line.Bounds.Right) - Math.Max(previous.Bounds.X, line.Bounds.X);
        if (alignment > height * 2.2 || overlap < Math.Min(previous.Bounds.Width, line.Bounds.Width) * .4) return false;
        // Short UI labels/buttons and finished short utterances stay independent.
        if (previous.Text.Length < 12 && previous.Words.Count < 3) return false;
        return group.Count < 12;
    }
    public static PixelRect Union(IEnumerable<PixelRect> rectangles)
    {
        var all = rectangles.ToArray(); if (all.Length == 0) return new();
        var x = all.Min(r => r.X); var y = all.Min(r => r.Y); return new(x, y, all.Max(r => r.Right) - x, all.Max(r => r.Bottom) - y);
    }
    public static OcrWord? WordAt(OcrDocument document, double x, double y, double tolerance = 18) => document.Blocks.SelectMany(b => b.Words)
        .Select(w => (Word: w, Distance: Math.Sqrt(Math.Pow(Math.Max(Math.Max(w.Bounds.X - x, 0), x - w.Bounds.Right), 2) + Math.Pow(Math.Max(Math.Max(w.Bounds.Y - y, 0), y - w.Bounds.Bottom), 2))))
        .Where(w => w.Distance <= tolerance).OrderBy(w => w.Distance).ThenBy(w => w.Word.Bounds.Width).Select(w => w.Word).FirstOrDefault();
    public static string SampleBackground(CapturedFrame frame, PixelRect bounds)
    {
        var colors = new List<(byte B, byte G, byte R)>();
        var left = (int)(bounds.X - frame.Bounds.X); var top = (int)(bounds.Y - frame.Bounds.Y); var right = (int)(bounds.Right - frame.Bounds.X); var bottom = (int)(bounds.Bottom - frame.Bounds.Y);
        void Add(int x, int y) { x = Math.Clamp(x, 0, frame.Width - 1); y = Math.Clamp(y, 0, frame.Height - 1); var p = (y * frame.Width + x) * 4; colors.Add((frame.Bgra[p], frame.Bgra[p + 1], frame.Bgra[p + 2])); }
        for (var x = left; x <= right; x += Math.Max(1, (right - left) / 32)) { Add(x, top - 3); Add(x, bottom + 3); }
        for (var y = top; y <= bottom; y += Math.Max(1, (bottom - top) / 16)) { Add(left - 3, y); Add(right + 3, y); }
        var r = colors.Select(c => c.R).Order().ElementAt(colors.Count / 2); var g = colors.Select(c => c.G).Order().ElementAt(colors.Count / 2); var b = colors.Select(c => c.B).Order().ElementAt(colors.Count / 2);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
