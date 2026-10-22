using System.Runtime.InteropServices.WindowsRuntime;
using GaameTranslate.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GaameTranslate.Windows;

public sealed class WindowsOcrProvider : IOcrProvider
{
    public IReadOnlyList<LanguageChoice> AvailableLanguages => OcrEngine.AvailableRecognizerLanguages.Select(l => new LanguageChoice(l.LanguageTag, l.DisplayName)).ToArray();
    public Task<OcrDocument> RecognizeAsync(CapturedFrame frame, string language, PreprocessingOptions options, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var available = AvailableLanguages;
        var requested = language;
        if (language == "auto")
        {
            var preferred = OcrEngine.TryCreateFromUserProfileLanguages();
            requested = preferred?.RecognizerLanguage.LanguageTag ?? "auto";
        }
        var selected = Languages.ResolveOcr(requested, available.Select(l => l.Code));
        var engine = OcrEngine.TryCreateFromLanguage(new Language(selected)) ?? throw new UserFacingException("Windows could not load this OCR language pack. Reinstall it in Windows language settings.");
        var max = (int)OcrEngine.MaxImageDimension;
        var scale = Math.Min(options.Scale, Math.Min((double)max / frame.Width, (double)max / frame.Height));
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale)); var height = Math.Max(1, (int)Math.Round(frame.Height * scale));
        // Avoid allocating several hundred MB when an unusually high maximum is exposed.
        if ((long)width * height > 20_000_000)
        {
            scale *= Math.Sqrt(20_000_000d / ((long)width * height));
            width = Math.Max(1, (int)(frame.Width * scale)); height = Math.Max(1, (int)(frame.Height * scale));
        }
        var pixels = ImagePreprocessor.Process(frame, width, height, options, cancellationToken);
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        bitmap.CopyFromBuffer(pixels.AsBuffer());
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        PixelRect Map(global::Windows.Foundation.Rect r) => new(frame.Bounds.X + r.X * frame.Width / width,
            frame.Bounds.Y + r.Y * frame.Height / height, r.Width * frame.Width / width, r.Height * frame.Height / height);
        var blocks = new List<OcrBlock>();
        foreach (var line in result.Lines)
        {
            var words = line.Words.Select(w => new Core.OcrWord(w.Text, Map(w.BoundingRect))).ToArray();
            if (words.Length == 0) continue;
            var left = words.Min(w => w.Bounds.X); var top = words.Min(w => w.Bounds.Y);
            blocks.Add(new(blocks.Count, TextProcessing.Normalize(line.Text), new(left, top, words.Max(w => w.Bounds.Right) - left, words.Max(w => w.Bounds.Bottom) - top), words));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new OcrDocument(blocks, selected, result.TextAngle);
    }, cancellationToken);
}

public static class ImagePreprocessor
{
    public static byte[] Process(CapturedFrame frame, int width, int height, PreprocessingOptions options, CancellationToken token)
    {
        var output = new byte[checked(width * height * 4)];
        // Bilinear enlargement helps small glyphs without hard thresholding by default.
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var fy = Math.Clamp((y + .5) * frame.Height / height - .5, 0, frame.Height - 1); var y0 = (int)fy; var y1 = Math.Min(y0 + 1, frame.Height - 1); var wy = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var fx = Math.Clamp((x + .5) * frame.Width / width - .5, 0, frame.Width - 1); var x0 = (int)fx; var x1 = Math.Min(x0 + 1, frame.Width - 1); var wx = fx - x0;
                var index = (y * width + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    double Sample(int sx, int sy)
                    {
                        if (!options.Denoise) return frame.Bgra[(sy * frame.Width + sx) * 4 + c];
                        var sum = 0; var count = 0;
                        for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                            { sum += frame.Bgra[(Math.Clamp(sy + dy, 0, frame.Height - 1) * frame.Width + Math.Clamp(sx + dx, 0, frame.Width - 1)) * 4 + c]; count++; }
                        return (double)sum / count;
                    }
                    var value = (Sample(x0, y0) * (1 - wx) + Sample(x1, y0) * wx) * (1 - wy) + (Sample(x0, y1) * (1 - wx) + Sample(x1, y1) * wx) * wy;
                    if (options.EnhanceContrast) value = (value - 128) * 1.35 + 128;
                    output[index + c] = (byte)Math.Clamp(value, 0, 255);
                }
                if (options.Grayscale || options.Threshold)
                {
                    var gray = (byte)(output[index] * .114 + output[index + 1] * .587 + output[index + 2] * .299);
                    if (options.Threshold) gray = gray >= 140 ? (byte)255 : (byte)0;
                    output[index] = output[index + 1] = output[index + 2] = gray;
                }
                output[index + 3] = 255;
            }
        }
        return output;
    }
}
