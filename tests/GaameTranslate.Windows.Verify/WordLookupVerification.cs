using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaameTranslate.App;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using GaameTranslate.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaameTranslate.Verify;

internal static class WordLookupVerification
{
    public static async Task ShowFixtureAsync()
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = new StackPanel { Margin = new(36) };
        root.Children.Add(new TextBlock { Text = "Alt+W benchmark · local test sentence", Foreground = Brushes.DimGray, FontSize = 14, Margin = new(0, 0, 0, 24) });
        root.Children.Add(new TextBlock { Text = "Better not to get into the matter.", Foreground = Brushes.Black, FontFamily = new FontFamily("Segoe UI"), FontSize = 36 });
        root.Children.Add(new TextBlock { Text = "Place the pointer over Better and press Alt+W.", Foreground = Brushes.DimGray, FontSize = 14, Margin = new(0, 24, 0, 0) });
        var fixture = new Window { Title = "GaameTranslate · Alt+W test sentence", Width = 930, Height = 270, Background = Brushes.White, Foreground = Brushes.Black, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = root };
        fixture.Closed += (_, _) => closed.TrySetResult(); fixture.Show(); await closed.Task;
    }
    public static async Task<IReadOnlyList<string>> RunAsync(string output, bool live, bool realOcr = false, bool warmOcr = false)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
        var data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")); var store = new SettingsStore(data); var db = new LocalDatabase(data); var keys = new NoKeys();
        var preferences = new AppSettings { Provider = "LibreTranslate", AssistantProvider = "Ollama", AssistantModel = "qwen3:4b-instruct-2507-q4_K_M", SourceLanguage = "en", TargetLanguage = "ru", OcrLanguage = "en-US", OcrEngine = "PP-OCRv6 Medium", AutoExplainWords = true, TranslationTimeoutSeconds = 180 };
        await store.SaveAsync(preferences); using var api = new DelayedApi(); using var http = live ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(api) { Timeout = Timeout.InfiniteTimeSpan }; using var speech = new WindowsSpeechService();
        using var nativeOcr = realOcr ? new AdaptiveOcrProvider() : null;
        var warmup = Stopwatch.StartNew(); if (warmOcr && nativeOcr is not null) await nativeOcr.WarmUpAsync(preferences.OcrEngine, preferences.OcrUseGpu, preferences.OcrLanguage, CancellationToken.None); warmup.Stop();
        var vm = new MainViewModel(realOcr ? new DesktopCapture() : new CaptureFixture(), realOcr ? nativeOcr! : new OcrFixture(), db, store, keys, http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, keys, new OfflineDictionaryStore(Path.Combine(data, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(data, "media")));
        await vm.InitializeAsync();
        Window? fixture = null;
        if (realOcr)
        {
            var text = new TextBlock { Text = "Better not to get into the matter.", FontSize = 36, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black, Margin = new(40) };
            fixture = new Window { Title = "GaameTranslate · real OCR benchmark", WindowStyle = WindowStyle.None, Width = 900, Height = 160, Background = Brushes.White, Content = text, Topmost = true };
            fixture.Show(); await Task.Delay(100); fixture.UpdateLayout();
            var anchor = text.PointToScreen(new Point(50, 22)); NativeMethods.GetCursorPos(out var pointer); var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(fixture);
            fixture.Left += (pointer.X - anchor.X) / dpi.DpiScaleX; fixture.Top += (pointer.Y - anchor.Y) / dpi.DpiScaleY;
            fixture.Activate(); await Task.Delay(100);
        }
        TranslationSession? captured = null; TranslatedBlock? selected = null;
        vm.WordLookupRequested += (_, session, block) => { captured = session; selected = block; };
        var pipeline = Stopwatch.StartNew(); await vm.RunCaptureAsync("Word at cursor"); pipeline.Stop();
        Check(captured is not null && captured.TranslatedText.Length == 0 && api.WordCalls == 0 && api.ContextCalls == 0 && api.AssistantCalls == 0, "Alt+W opens the dictionary after OCR without waiting for any translation request");
        Check((await db.GetHistoryAsync()).Count == 0, "Untranslated lookup is not saved as a successful history entry");
        using (var overlay = new OverlayManager(vm, NullLogger<OverlayManager>.Instance)) { overlay.Show(captured!); Check(!overlay.IsVisible, "Untranslated lookup cannot create an empty screen overlay"); }
        var popup = new WordLookupWindow(vm, "better", captured!, selected!); var opening = Stopwatch.StartNew(); popup.Show();
        var model = (WordLookupViewModel)popup.DataContext;
        T Find<T>(string id) where T : DependencyObject => Descendants(popup).OfType<T>().Single(item => AutomationProperties.GetAutomationId(item) == id);
        await WaitFor(() => live ? model.CanSave : api.WordCalls == 1);
        if (!live)
        {
            Check(popup.IsVisible && !model.CanSave && api.ContextCalls == 0 && api.AssistantCalls == 0, "Popup is visible while the word request is pending and gives it priority");
            Check(api.FirstWord == "better", "First translation request contains only the selected word");
            api.WordGate.TrySetResult(); await WaitFor(() => model.CanSave && api.ContextCalls == 1 && api.AssistantCalls == 1);
        }
        var firstWordMs = opening.Elapsed.TotalMilliseconds;
        Check(model.CanSave && !string.IsNullOrWhiteSpace(model.Translation), "Word translation becomes usable before enrichment completes");
        if (live)
        {
            Save(popup, Path.Combine(output, "lookup-live-first-word.png"));
            await File.WriteAllTextAsync(Path.Combine(output, "live-first-word.json"), JsonSerializer.Serialize(new { ocr = realOcr ? warmOcr ? "Real GDI capture and PP-OCRv6 Medium, startup model preparation completed" : "Real GDI capture and PP-OCRv6 Medium, cold model" : "Deterministic OCR fixture, not a live OCR measurement", preparationMs = warmup.Elapsed.TotalMilliseconds, captureAndOcrMs = pipeline.Elapsed.TotalMilliseconds, ocrMs = captured!.Timings.OcrMs, firstWordMs, totalToFirstWordMs = pipeline.Elapsed.TotalMilliseconds + firstWordMs, word = model.Translation, backgroundPending = model.IsLoadingContext || model.IsLoadingExplanation }));
            await WaitFor(() => !model.IsLoadingContext && !model.IsLoadingExplanation, 200000); Save(popup, Path.Combine(output, "lookup-live-complete.png"));
            await File.WriteAllTextAsync(Path.Combine(output, "live-complete.json"), JsonSerializer.Serialize(new { firstWordMs, completeMs = opening.Elapsed.TotalMilliseconds, word = model.Translation, context = model.ContextTranslation, assistantError = model.ExplanationStatus }));
            Check(model.ContextTranslation.Length > 0, "Live local provider translates context independently");
            popup.Close(); fixture?.Close(); await vm.StopAsync(); return passed;
        }
        Check(model.Translation == "[FAST] лучше" && model.IsLoadingContext && model.IsLoadingExplanation && Find<Button>("lookup-save").IsEnabled, "Slow sentence and assistant cannot block saving the fast word");
        Check(Find<TextBlock>("lookup-translation").Text == model.Translation, "Actual dictionary window displays the first result while background calls are blocked");
        Save(popup, Path.Combine(output, "lookup-fast.png")); Find<Button>("lookup-save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => vm.Words.Count == 1);
        Check(vm.Words.Single().Translation == "[FAST] лучше" && vm.Words.Single().TranslatedContext.Length == 0, "Saving the first result does not wait for context or save a placeholder");
        api.ContextGate.TrySetResult(); await WaitFor(() => !model.IsLoadingContext);
        Check(model.ContextTranslation == "[CONTEXT] Лучше не вмешиваться." && model.Translation == "[FAST] лучше" && model.IsLoadingExplanation, "Sentence translation arrives independently while the fast word remains visible");
        api.AssistantGate.TrySetResult(); await WaitFor(() => !model.IsLoadingExplanation && model.Translation.StartsWith("[AI]"));
        await WaitForAsync(async () => (await db.GetHistoryAsync()).Count == 1);
        Check(model.BasicTranslation == "[FAST] лучше" && model.ContextTranslation == "[AI CONTEXT] Лучше не вмешиваться в это дело.", "Contextual meaning refines the result while retaining the original quick translation");
        Check((await db.GetWordsAsync()).Single().Translation == "[FAST] лучше", "Background refinement does not silently change an already saved card");
        Check((await db.GetHistoryAsync()).Single().TranslatedText == model.ContextTranslation, "Completed lookup persists the real sentence instead of an empty result");
        Save(popup, Path.Combine(output, "lookup-refined.png")); popup.Close();

        vm.Settings = preferences with { AutoExplainWords = false };
        var translatedBlock = selected! with { Translation = "Already translated context." };
        var fromOverlay = captured! with { Id = Guid.NewGuid(), CaptureType = "Screen translation", Blocks = [translatedBlock] };
        var calls = api.WordCalls;
        using (var saved = new WordLookupViewModel(vm, "better", fromOverlay, translatedBlock))
        { await saved.LoadAsync(); Check(api.WordCalls == calls && saved.TranslationSource == "Сохранённый перевод", "Saved vocabulary supplies a word without an HTTP request"); }
        using (var first = new WordLookupViewModel(vm, "wanderer", fromOverlay, translatedBlock)) await first.LoadAsync();
        var secondBlock = translatedBlock with { Original = translatedBlock.Original with { Text = "A different sentence with wanderer." } };
        using (var second = new WordLookupViewModel(vm, "wanderer", fromOverlay with { Id = Guid.NewGuid(), Blocks = [secondBlock] }, secondBlock)) await second.LoadAsync();
        Check(api.WandererCalls == 1, "Single-word cache is reused across different sentences");
        Check(api.ContextCalls == 1, "A dictionary opened from an existing overlay reuses its translated context");
        var assistantCalls = api.AssistantCalls;
        using (var manual = new WordLookupViewModel(vm, "manual", fromOverlay, translatedBlock))
        {
            await manual.LoadAsync(); Check(api.AssistantCalls == assistantCalls, "Disabling automatic explanations leaves fast lookup available");
            await manual.LoadAsync(true); Check(api.AssistantCalls == assistantCalls + 1 && manual.Translation.StartsWith("[AI]"), "Manual explanation refines an existing word on demand");
        }
        using (var stale = new WordLookupViewModel(vm, "slow", fromOverlay, translatedBlock))
        {
            var old = stale.LoadAsync(); await WaitFor(() => api.SlowCalls == 1); stale.Word = "replacement"; await stale.LoadAsync(); api.SlowGate.TrySetResult(); await old;
            Check(stale.Translation == "[FAST] replacement" && stale.CanSave, "Late response for an edited word cannot replace the current translation");
        }
        vm.Settings = preferences;
        using (var failure = new WordLookupViewModel(vm, "failure", fromOverlay, translatedBlock with { Translation = "", Original = translatedBlock.Original with { Text = "Unique sentence for failure." } }))
        {
            api.FailEnrichment = true; await failure.LoadAsync(); api.FailEnrichment = false;
            Check(failure.CanSave && failure.Translation == "[FAST] failure" && failure.ExplanationStatus.Length > 0 && failure.ContextStatus.Length > 0, "Assistant and context failures retain the usable first result");
        }
        using (var recovery = new WordLookupViewModel(vm, "noquick", fromOverlay, translatedBlock))
        { await recovery.LoadAsync(); Check(recovery.CanSave && recovery.Translation.StartsWith("[AI]"), "Assistant can still recover a meaning when the quick provider fails"); }
        api.BlockEnrichment = true;
        var closePopup = new WordLookupWindow(vm, "closing", fromOverlay, translatedBlock with { Translation = "", Original = translatedBlock.Original with { Text = "Unique sentence for closing." } }); closePopup.Show(); var closeModel = (WordLookupViewModel)closePopup.DataContext;
        await WaitFor(() => closeModel.CanSave && closeModel.IsLoadingExplanation && closeModel.IsLoadingContext); closePopup.Close();
        await WaitFor(() => api.CancelledEnrichment >= 2);
        Check(api.CancelledEnrichment >= 2, "Closing the popup cancels both unfinished enrichment requests");
        api.BlockEnrichment = false;
        var previousSession = captured!; await vm.RunCaptureAsync("Word at cursor"); var latestSession = vm.LastSession!;
        await vm.CompleteWordLookupContextAsync(previousSession, "Stale context must not replace the latest lookup", 1, CancellationToken.None);
        Check(vm.LastSession?.Id == latestSession.Id && vm.OutputText.Length == 0, "Late context cannot replace a newer Alt+W session");
        await vm.StopAsync(); return passed;
    }
    private static async Task WaitFor(Func<bool> condition, int ms = 10000) { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < ms) await Task.Delay(20); if (!condition()) throw new TimeoutException("Lookup condition did not complete"); await System.Windows.Threading.Dispatcher.Yield(); }
    private static async Task WaitForAsync(Func<Task<bool>> condition) { for (var retry = 0; retry < 200; retry++) { if (await condition()) return; await Task.Delay(20); } throw new TimeoutException("Lookup persistence did not complete"); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var item in Descendants(child)) yield return item; }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); var backing = new DrawingVisual(); using (var drawing = backing.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); image.Render(backing); image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class CaptureFixture : IScreenCapture
    {
        public Task<CapturedFrame> CaptureAsync(PixelRect bounds, CancellationToken token) => Task.FromResult(new CapturedFrame(new byte[(int)bounds.Width * (int)bounds.Height * 4], (int)bounds.Width, (int)bounds.Height, bounds));
    }
    private sealed class OcrFixture : IOcrProvider
    {
        public IReadOnlyList<LanguageChoice> AvailableLanguages => [new("en-US", "English")];
        public Task<OcrDocument> RecognizeAsync(CapturedFrame frame, string language, PreprocessingOptions options, CancellationToken token)
        {
            NativeMethods.GetCursorPos(out var point); var bounds = new PixelRect(point.X - 20, point.Y - 10, 80, 30); var word = new OcrWord("better", bounds);
            return Task.FromResult(new OcrDocument([new OcrBlock(0, "Better not to get into the matter.", bounds, [word])], "en-US"));
        }
    }
    private sealed class NoKeys : ICredentialStore { public string? Read(string name) => null; public void Write(string name, string value) { } public void Delete(string name) { } }
    private sealed class DelayedApi : HttpMessageHandler
    {
        public TaskCompletionSource WordGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContextGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AssistantGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SlowGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WordCalls, ContextCalls, AssistantCalls, WandererCalls, SlowCalls, CancelledEnrichment;
        public string FirstWord = "";
        public bool FailEnrichment, BlockEnrichment;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/chat"))
            {
                AssistantCalls++;
                if (FailEnrichment) return new(HttpStatusCode.BadRequest);
                if (BlockEnrichment) await BlockAsync(token);
                await AssistantGate.Task.WaitAsync(token);
                var content = JsonSerializer.Serialize(new { headword = "better", translation = "[AI] лучше не вмешиваться", definition = "[AI] Совет воздержаться от действия.", contextTranslation = "[AI CONTEXT] Лучше не вмешиваться в это дело.", explanation = "[AI TEST DOUBLE] Meaning depends on the sentence." });
                return Json(new { message = new { content } });
            }
            var text = json.RootElement.GetProperty("q")[0].GetString()!;
            if (text.Contains(' '))
            {
                ContextCalls++;
                if (FailEnrichment) return new(HttpStatusCode.BadRequest);
                if (BlockEnrichment) await BlockAsync(token);
                await ContextGate.Task.WaitAsync(token); return Json(new { translatedText = new[] { "[CONTEXT] Лучше не вмешиваться." } });
            }
            WordCalls++; if (FirstWord.Length == 0) FirstWord = text;
            if (text == "noquick") return new(HttpStatusCode.BadRequest);
            if (text == "better") await WordGate.Task.WaitAsync(token);
            if (text == "wanderer") WandererCalls++;
            if (text == "slow") { SlowCalls++; await SlowGate.Task; }
            return Json(new { translatedText = new[] { text == "better" ? "[FAST] лучше" : "[FAST] " + text } });
        }
        private async Task BlockAsync(CancellationToken token) { try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { Interlocked.Increment(ref CancelledEnrichment); throw; } }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
