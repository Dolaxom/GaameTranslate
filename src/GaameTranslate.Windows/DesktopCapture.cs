using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GaameTranslate.Core;
using Forms = System.Windows.Forms;

namespace GaameTranslate.Windows;

public sealed record DisplayInfo(int Index, string Name, PixelRect Bounds, PixelRect WorkArea, bool Primary)
{
    public override string ToString() => $"Display {Index + 1} · {(int)Bounds.Width} × {(int)Bounds.Height}" + (Primary ? " · primary" : "");
}
public sealed class DesktopCapture : IScreenCapture
{
    public static IReadOnlyList<DisplayInfo> Displays => Forms.Screen.AllScreens.Select((s, i) => new DisplayInfo(i, s.DeviceName,
        new(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), new(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height), s.Primary)).ToArray();
    public static PixelRect DesktopBounds
    {
        get { var r = Forms.SystemInformation.VirtualScreen; return new(r.X, r.Y, r.Width, r.Height); }
    }
    public static DisplayInfo CursorDisplay()
    {
        NativeMethods.GetCursorPos(out var p);
        return Displays.FirstOrDefault(s => p.X >= s.Bounds.X && p.X < s.Bounds.Right && p.Y >= s.Bounds.Y && p.Y < s.Bounds.Bottom) ?? Displays[0];
    }
    public Task<CapturedFrame> CaptureAsync(PixelRect bounds, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var intersection = bounds.Intersect(DesktopBounds);
        if (!intersection.IsValid || intersection.Width < bounds.Width - 1 || intersection.Height < bounds.Height - 1) throw new UserFacingException("The selected region is no longer on the desktop. Select a new area after changing monitors.");
        var width = checked((int)Math.Ceiling(bounds.Width)); var height = checked((int)Math.Ceiling(bounds.Height));
        if ((long)width * height > 40_000_000) throw new UserFacingException("This capture is too large. Select a region or a single monitor (up to 40 megapixels).");
        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen((int)bounds.X, (int)bounds.Y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[checked(width * height * 4)];
                for (var y = 0; y < height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
                for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                cancellationToken.ThrowIfCancellationRequested();
                return new CapturedFrame(pixels, width, height, bounds);
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ExternalException)
        { throw new UserFacingException("Windows could not capture this screen. Use a visible, borderless game window. Protected content and some exclusive fullscreen games cannot be captured.", e); }
    }, cancellationToken);
}
