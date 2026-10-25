using GaameTranslate.Core;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Small;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Medium;
using Windows.Data.Text;

namespace GaameTranslate.Windows;

public sealed class AdaptiveOcrProvider : IOcrProvider, IDisposable
{
    private readonly WindowsOcrProvider windows = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private PaddleOcrAll? paddle;
    private string loaded = "";
    public string Engine { get; set; } = "PP-OCRv6 Small";
    public bool UseGpu { get; set; } = true;
    public string LastEngine { get; private set; } = "";
    public IReadOnlyList<LanguageChoice> AvailableLanguages => new[] { new LanguageChoice("en", "English · PP-OCRv6"), new("ja", "日本語 · PP-OCRv6"), new("zh", "中文 · PP-OCRv6") }.Concat(windows.AvailableLanguages).DistinctBy(l => l.Code).ToArray();
    public async Task<OcrDocument> RecognizeAsync(CapturedFrame frame, string language, PreprocessingOptions options, CancellationToken token)
    {
        // PP-OCRv6 Chinese models support Chinese/Japanese/English. Never advertise Cyrillic support they do not have.
        if (Engine == "Windows" || (language != "auto" && Languages.Base(language) is not ("en" or "ja" or "zh")))
        { LastEngine = "Windows OCR"; return await windows.RecognizeAsync(frame, language, options, token); }
        var selected = Engine; var gpu = UseGpu; await gate.WaitAsync(token);
        try
        {
            var identity = selected + "|" + gpu;
            if (loaded != identity || paddle is null)
            {
                paddle?.Dispose(); paddle = null; loaded = "";
                var backend = gpu ? OcrBackend.Auto : OcrBackend.Cpu;
                var configuration = new PaddleOcrOptions
                {
                    LineWorkerCount = 2,
                    DetIntraOpThreads = 4,
                    UseDirectionClassification = true,
                    Detector = new() { Backend = backend, LimitSideLength = 1920, MaxSessionCacheEntries = 2, MaxPooledSessions = 2 },
                    Recognizer = new() { Backend = backend, MaxPooledSessions = 2 },
                    Classifier = new() { Backend = backend, MaxPooledSessions = 2 }
                };
                paddle = await Task.Run(() => PaddleOcrAll.Load(selected == "PP-OCRv6 Medium" ? ChineseV6MediumModels.Default : ChineseV6SmallModels.Default, configuration), token); loaded = identity;
            }
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); var width = frame.Width; var height = frame.Height; var bytes = frame.Bgra;
                if (options.Grayscale || options.EnhanceContrast || options.Threshold || options.Denoise) bytes = ImagePreprocessor.Process(frame, width, height, options, token);
                var recognized = paddle.Run(bytes, width, height, returnCtcAlignment: true, format: ImagePixelFormat.Bgra32); token.ThrowIfCancellationRequested();
                var blocks = new List<OcrBlock>();
                PixelRect Map(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
                {
                    var x = Math.Max(0, Math.Min(Math.Min(x1, x2), Math.Min(x3, x4))); var y = Math.Max(0, Math.Min(Math.Min(y1, y2), Math.Min(y3, y4))); var right = Math.Min(width, Math.Max(Math.Max(x1, x2), Math.Max(x3, x4))); var bottom = Math.Min(height, Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
                    return new(frame.Bounds.X + x, frame.Bounds.Y + y, Math.Max(0, right - x), Math.Max(0, bottom - y));
                }
                foreach (var line in recognized.Lines.OrderBy(l => l.Box.Y1).ThenBy(l => l.Box.X1))
                {
                    if (string.IsNullOrWhiteSpace(line.Text) || line.RecognitionScore < .35) continue; var box = line.Box; var bounds = Map(box.X1, box.Y1, box.X2, box.Y2, box.X3, box.Y3, box.X4, box.Y4);
                    var characters = line.EstimateCharacterBoxes(); var charBounds = new List<(int Start, int End, PixelRect Bounds)>(); var offset = 0;
                    foreach (var ch in characters) { charBounds.Add((offset, offset + ch.Text.Length, Map(ch.X1, ch.Y1, ch.X2, ch.Y2, ch.X3, ch.Y3, ch.X4, ch.Y4))); offset += ch.Text.Length; }
                    var tag = language == "auto" ? (line.Text.Any(c => c is >= '\u3040' and <= '\u30ff') ? "ja" : line.Text.Any(c => c is >= '\u4e00' and <= '\u9fff') ? "zh" : "en") : Languages.Base(language);
                    var tokens = new WordsSegmenter(tag).GetTokens(line.Text); var words = new List<Core.OcrWord>();
                    foreach (var word in tokens)
                    {
                        var start = (int)word.SourceTextSegment.StartPosition; var end = start + (int)word.SourceTextSegment.Length; var boxes = charBounds.Where(c => c.Start < end && c.End > start).Select(c => c.Bounds).ToArray();
                        if (boxes.Length > 0) words.Add(new(word.Text, ScreenAnalysis.Union(boxes)));
                    }
                    if (words.Count == 0) words.Add(new(line.Text, bounds));
                    blocks.Add(new(blocks.Count, TextProcessing.Normalize(line.Text), bounds, words, line.RecognitionScore is >= 0 and <= 1 ? line.RecognitionScore : null) { LineBounds = [bounds] });
                }
                var joined = string.Join(" ", blocks.Select(b => b.Text));
                var detected = language == "auto" ? (joined.Any(c => c is >= '\u3040' and <= '\u30ff') ? "ja" : joined.Any(c => c is >= '\u4e00' and <= '\u9fff') ? "zh" : "en") : language;
                return new OcrDocument(blocks, detected);
            }, token);
            LastEngine = selected + (gpu ? " · Auto GPU/CPU" : " · CPU"); return result;
        }
        finally { gate.Release(); }
    }
    public void Dispose() { paddle?.Dispose(); gate.Dispose(); }
}
