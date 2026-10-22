using System.Text.Json;
using System.Windows.Media.Imaging;
using GaameTranslate.Core;
using GaameTranslate.Windows;

namespace GaameTranslate.App;

internal static class NativeSmokeTest
{
    public static async Task<string> RunAsync(IOcrProvider ocr, IScreenCapture capture, ICredentialStore credentials)
    {
        var language = ocr.AvailableLanguages.FirstOrDefault(l => Languages.Base(l.Code) == "en") ?? ocr.AvailableLanguages.FirstOrDefault();
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1000, 180));
            dc.DrawText(new FormattedText("Hello traveler. Your journey begins here.", System.Globalization.CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 38, Brushes.Black, 1), new Point(24, 50));
        }
        var bitmap = new RenderTargetBitmap(1000, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var bytes = new byte[1000 * 180 * 4]; bitmap.CopyPixels(bytes, 4000, 0);
        var document = language is null ? null : await ocr.RecognizeAsync(new(bytes, 1000, 180, new(-100, 50, 1000, 180)), language.Code, new() { Scale = 1 }, CancellationToken.None);
        var display = DesktopCapture.Displays[0]; var screen = await capture.CaptureAsync(new(display.Bounds.X + 10, display.Bounds.Y + 10, 320, 120), CancellationToken.None);
        const string credentialName = "SmokeTest"; credentials.Write(credentialName, "smoke-test-value"); var roundTrip = credentials.Read(credentialName) == "smoke-test-value"; credentials.Delete(credentialName);
        return JsonSerializer.Serialize(new { languages = ocr.AvailableLanguages, ocrText = document?.Text, ocrBlocks = document?.Blocks.Count, ocrBoundsMapped = document?.Blocks.All(b => b.Bounds.X >= -100 && b.Bounds.Y >= 50), captureWidth = screen.Width, captureHeight = screen.Height, captureBytes = screen.Bgra.Length, dpapiRoundTrip = roundTrip, displays = DesktopCapture.Displays }, new JsonSerializerOptions { WriteIndented = true });
    }
}
