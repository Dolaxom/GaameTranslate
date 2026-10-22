namespace GaameTranslate.Core;

public static class OverlayPlacement
{
    /// <summary>Find the closest readable candidate; a null result tells the UI to use a single panel.</summary>
    public static PixelRect? Place(PixelRect desired, PixelRect viewport, IReadOnlyList<PixelRect> occupied)
    {
        if (desired.Width > viewport.Width || desired.Height > viewport.Height) return null;
        var initial = new PixelRect(Math.Clamp(desired.X, viewport.X, viewport.Right - desired.Width), Math.Clamp(desired.Y, viewport.Y, viewport.Bottom - desired.Height), desired.Width, desired.Height);
        var xs = new[] { initial.X, viewport.X, viewport.Right - desired.Width }.Concat(occupied.SelectMany(r => new[] { r.Right + 8, r.X - desired.Width - 8 })).Distinct();
        var ys = new[] { initial.Y, viewport.Y, viewport.Bottom - desired.Height }.Concat(occupied.SelectMany(r => new[] { r.Bottom + 6, r.Y - desired.Height - 6 })).Distinct().ToArray();
        return xs.SelectMany(x => ys.Select(y => new PixelRect(x, y, desired.Width, desired.Height)))
            .Where(r => r.X >= viewport.X && r.Y >= viewport.Y && r.Right <= viewport.Right && r.Bottom <= viewport.Bottom)
            .Where(r => occupied.All(o => r.X >= o.Right + 2 || r.Right + 2 <= o.X || r.Y >= o.Bottom + 2 || r.Bottom + 2 <= o.Y))
            .OrderBy(r => Math.Abs(r.X - initial.X) + Math.Abs(r.Y - initial.Y)).Select(r => (PixelRect?)r).FirstOrDefault();
    }
}
