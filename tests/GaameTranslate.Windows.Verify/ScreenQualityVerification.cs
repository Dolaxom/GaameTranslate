using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaameTranslate.App;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using GaameTranslate.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaameTranslate.Verify;

internal static class ScreenQualityVerification
{
    public static async Task RunAsync(string output, bool live, string[] images)
    {
        if (images.Length < 2) throw new ArgumentException("Pass English and Russian screenshots after --images.");
        var report = new List<object>(); using var ocr = new AdaptiveOcrProvider(); using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; using var speech = new WindowsSpeechService(); var data = Path.Combine(output, "data"); var credentials = new WindowsCredentialStore(Path.Combine(data, "credentials"));
        var settings = new AppSettings { Provider = "Ollama", OllamaModel = "translategemma:4b", AssistantProvider = "Ollama", AssistantModel = "qwen3:4b-instruct-2507-q4_K_M", SourceLanguage = "en", TargetLanguage = "ru", OverlayMode = OverlayMode.Replacement, TranslationTimeoutSeconds = 300 }; var store = new SettingsStore(data); await store.SaveAsync(settings);
        var vm = new MainViewModel(new DesktopCapture(), ocr, new LocalDatabase(data), store, credentials, http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, credentials, new OfflineDictionaryStore(Path.Combine(data, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(data, "media"))); await vm.InitializeAsync();
        foreach (var path in images)
        {
            using var input = File.OpenRead(path); var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0); var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); var frame = new CapturedFrame(bytes, bitmap.PixelWidth, bitmap.PixelHeight, new(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            foreach (var engine in new[] { "Windows", "PP-OCRv6 Small", "PP-OCRv6 Medium" })
            {
                ocr.Engine = engine; ocr.UseGpu = true; var watch = Stopwatch.StartNew(); var raw = await ocr.RecognizeAsync(frame, path == images[1] ? "ru" : "en", new(), CancellationToken.None); var elapsed = watch.ElapsedMilliseconds; var grouped = ScreenAnalysis.GroupParagraphs(raw);
                var name = Path.GetFileNameWithoutExtension(path) + "-" + engine.Replace(' ', '-'); await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new { requestedEngine = engine, actualEngine = ocr.LastEngine, elapsed, raw, grouped }, new JsonSerializerOptions { WriteIndented = true }));
                var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.DrawImage(bitmap, new Rect(0, 0, frame.Width, frame.Height)); foreach (var block in grouped.Blocks) { drawing.DrawRectangle(null, new Pen(Brushes.Lime, 2), new Rect(block.Bounds.X, block.Bounds.Y, block.Bounds.Width, block.Bounds.Height)); foreach (var word in block.Words) drawing.DrawRectangle(null, new Pen(Brushes.Cyan, .7), new Rect(word.Bounds.X, word.Bounds.Y, word.Bounds.Width, word.Bounds.Height)); } }
                var rendered = new RenderTargetBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual); Save(rendered, Path.Combine(output, name + ".png")); report.Add(new { image = Path.GetFileName(path), engine, elapsed, lines = raw.Blocks.Count, paragraphs = grouped.Blocks.Count, words = grouped.Blocks.Sum(b => b.Words.Count) }); Console.WriteLine($"{name}: {elapsed} ms / {raw.Blocks.Count} lines / {grouped.Blocks.Count} paragraphs");
                if (live && path == images[0] && engine == "PP-OCRv6 Medium")
                {
                    var quote = grouped.Blocks.FirstOrDefault(b => b.Text.Contains("forestay", StringComparison.OrdinalIgnoreCase)); if (quote is null) throw new InvalidOperationException("OCR did not recognize forestay in source screenshot");
                    var watchTranslation = Stopwatch.StartNew(); var translated = await new TranslationProvider(http, settings, credentials).TranslateAsync(new([quote.Text], "en", "ru"), CancellationToken.None); var gemmaMs = watchTranslation.ElapsedMilliseconds; watchTranslation.Restart();
                    var contextual = await new TranslationProvider(http, settings with { OllamaModel = "qwen3:4b-instruct-2507-q4_K_M" }, credentials).TranslateAsync(new([quote.Text], "en", "ru", "Literary detective role-playing game. A ship owner uses nautical metaphors while speaking politely to a detective."), CancellationToken.None); var qwenMs = watchTranslation.ElapsedMilliseconds;
                    var article = await vm.Dictionary.ExplainAsync(settings, "better", "en", "ru", quote.Text, CancellationToken.None);
                    await File.WriteAllTextAsync(Path.Combine(output, "live-translation.json"), JsonSerializer.Serialize(new { source = quote.Text, translation = translated, gemmaMs, contextual, qwenMs, article }, new JsonSerializerOptions { WriteIndented = true }));
                    var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, frame.Bounds, "en", "ru", "Ollama · qwen3:4b-instruct-2507-q4_K_M", "Screen translation", [new(quote, contextual[0].Text) { BackgroundColor = ScreenAnalysis.SampleBackground(frame, quote.Bounds) }], new(0, elapsed, qwenMs, qwenMs + elapsed));
                    using var manager = new OverlayManager(vm, NullLogger<OverlayManager>.Instance); manager.Show(session); await Task.Delay(150); var overlay = System.Windows.Application.Current.Windows.Cast<Window>().First(w => w.Title == "GaameTranslate · spatial overlay"); var canvas = (FrameworkElement)overlay.Content; canvas.UpdateLayout(); var display = DesktopCapture.Displays.First();
                    var composed = new DrawingVisual(); using (var drawing = composed.RenderOpen()) { drawing.DrawImage(bitmap, new Rect(0, 0, frame.Width, frame.Height)); drawing.DrawRectangle(new VisualBrush(canvas){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,canvas.ActualWidth,canvas.ActualHeight),Stretch=Stretch.Fill}, null, new Rect(0, 0, display.Bounds.Width, display.Bounds.Height)); }
                    var preview = new RenderTargetBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32); preview.Render(composed); Save(preview, Path.Combine(output, "screen-translation-live.png")); manager.Close();
                    var popup = new WordLookupWindow(vm, "better", session, session.Blocks[0], article); popup.Show(); await Task.Delay(150); var popupContent = (FrameworkElement)popup.Content; popupContent.UpdateLayout(); var popupImage = new RenderTargetBitmap((int)popupContent.ActualWidth, (int)popupContent.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    var backing=new DrawingVisual();using(var drawing=backing.RenderOpen())drawing.DrawRectangle(popup.Background,null,new Rect(0,0,popupContent.ActualWidth,popupContent.ActualHeight));popupImage.Render(backing); popupImage.Render(popupContent); Save(popupImage, Path.Combine(output, "dictionary-live.png")); popup.Close();
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "ocr-comparison.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Save(BitmapSource bitmap, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream); }
}
