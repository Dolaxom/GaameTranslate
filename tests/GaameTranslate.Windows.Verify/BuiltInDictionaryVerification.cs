using System.IO;
using System.Net;
using System.Net.Http;
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

internal static class BuiltInDictionaryVerification
{
    public static async Task<IReadOnlyList<string>> RunAsync(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
        async Task Settle() { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        var directory = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory); var database = new LocalDatabase(directory); var keys = new Keys();
        using var api = new Api(); using var http = new HttpClient(api); using var speech = new WindowsSpeechService();
        var vm = new MainViewModel(new Capture(), new Ocr(), database, store, keys, http, new TranslationCache(), NullLogger<MainViewModel>.Instance,
            new DictionaryService(http, keys, new OfflineDictionaryStore(Path.Combine(directory, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(directory, "media")));
        await vm.InitializeAsync(); UiText.SetLanguage("en");
        Check(vm.Settings.Provider == ProviderCatalog.BuiltInDictionary && vm.NeedsProviderSetup, "Clean installation selects the offline dictionary and explains its limitations");
        var main = new MainWindow(vm) { AllowClose = true }; main.Show(); await Settle();
        var notice = Descendants(main).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "dictionary-mode-notice");
        Check(notice.IsVisible && Descendants(notice).OfType<TextBlock>().Any(t => t.Text.Contains("Quality is limited")), "Persistent first-run notice is visible in English");
        main.Width = 960; main.Height = 650; vm.PageIndex = 1; await Settle(); Save(main, Path.Combine(output, "en-small-window.png"));
        Check(notice.IsVisible && notice.ActualHeight > 50, "Notice wraps in the minimum application size");
        main.Width = 1180; main.Height = 820;
        vm.InputText = "Hello, traveler!"; await vm.TranslateTextCommand.ExecuteAsync(null);
        Check(vm.OutputText.Any(c => c is >= 'А' and <= 'я') && vm.LastSession?.IsDictionaryTranslation == true && api.Requests == 0, "First text translation succeeds without credentials or network");
        Check((await database.GetHistoryAsync()).Single().IsDictionaryTranslation, "History retains dictionary provenance");
        await Settle(); Save(main, Path.Combine(output, "en-text.png"));
        UiText.SetLanguage("ru"); await Settle(); Save(main, Path.Combine(output, "ru-text.png"));
        Check(Descendants(notice).OfType<TextBlock>().Any(t => t.Text.Contains("Качество ограничено")), "The existing notice switches to Russian");
        await vm.RunCaptureAsync("Screen translation");
        var session = vm.LastSession!;
        Check(session.IsDictionaryTranslation && session.Provider == ProviderCatalog.BuiltInDictionary && session.TranslatedText.Length > 0, "Screen pipeline uses the dictionary and records the actual source");
        using (var overlays = new OverlayManager(vm, NullLogger<OverlayManager>.Instance))
        {
            foreach (var mode in Enum.GetValues<OverlayMode>())
            {
                await vm.SaveSettingsAsync(vm.Settings with { OverlayMode = mode }, null, false);
                overlays.Show(session with { CaptureType = "Repeat" }); await Settle();
                Check(Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).Any(w => Descendants(w).Any(e => AutomationProperties.GetAutomationId(e) == "overlay-dictionary-notice")), mode + " overlay displays the dictionary warning");
                overlays.Close();
            }
        }
        TranslationSession? cursorSession = null;
        vm.WordLookupRequested += (_, origin, _) => cursorSession = origin;
        await vm.RunCaptureAsync("Word at cursor");
        Check(cursorSession?.IsDictionaryTranslation == true, "Word-under-cursor capture opens a dictionary session");
        using (var lookup = new WordLookupViewModel(vm, "traveler", cursorSession!, cursorSession!.Blocks[0]))
        {
            await lookup.LoadAsync();
            Check(lookup.Translation.Contains("путешественник") && lookup.CanSave && lookup.ContextTranslation.Length > 0 && api.Requests == 0, "Word and context are available offline and can be saved");
            await vm.SaveWordAsync(new VocabularyEntry { Word = lookup.Word, Translation = lookup.Translation, SourceLanguage = "en", TargetLanguage = "ru", Context = cursorSession.OriginalText });
            Check(vm.Words.Single().Translation.Contains("путешественник"), "Offline lookup can be saved as a vocabulary card");
        }
        var popup = new WordLookupWindow(vm, "journey", cursorSession, cursorSession.Blocks[0]); popup.Show(); await Task.Delay(150); await Settle();
        Check(Descendants(popup).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "lookup-dictionary-notice" && t.IsVisible), "Dictionary popup keeps its quality notice visible");
        Save(popup, Path.Combine(output, "ru-word.png")); UiText.SetLanguage("en"); await Settle(); Save(popup, Path.Combine(output, "en-word.png")); popup.Close();
        vm.PageIndex = 5; await Settle(); Save(main, Path.Combine(output, "en-settings.png"));
        var settings = Descendants(main).OfType<SettingsView>().Single();
        Check(!Descendants(settings).OfType<PasswordBox>().Any(p => p.IsVisible) && !Descendants(settings).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "translation-test" && b.IsVisible), "Built-in settings need no API key or connection test");
        await vm.SaveSettingsAsync(vm.Settings with { Provider = "Yandex Translate" }, null, false);
        var fallback = await vm.LookupAsync("sword", "en", "ru", "", default);
        Check(fallback.IsDictionaryTranslation && api.Requests == 0, "A provider without its required key falls back immediately without HTTP");
        var previous = vm.Settings;
        await vm.SaveSettingsAsync(previous with { Provider = "LibreTranslate" }, null, false);
        vm.InputText = "Hello, traveler!"; await vm.TranslateTextCommand.ExecuteAsync(null); await Settle();
        Check(!vm.NeedsProviderSetup && !notice.IsVisible && vm.LastSession?.IsDictionaryTranslation == false && vm.OutputText == "[LIVE-ADAPTER TEST DOUBLE] translation" && api.Requests == 1, "Configuring a provider removes the notice and bypasses cached dictionary results");
        api.Failure = "offline";
        await vm.SaveSettingsAsync(vm.Settings with { ContextHint = "failover test" }, null, false);
        vm.InputText = "Hello, traveler!"; await vm.TranslateTextCommand.ExecuteAsync(null); await Settle();
        Check(vm.LastSession is { IsDictionaryFallback: true, IsDictionaryTranslation: true, Provider: ProviderCatalog.BuiltInDictionary } && vm.OutputText.Contains("привет"), "A configured but offline service produces a real dictionary translation");
        Check(vm.Settings.Provider == "LibreTranslate" && notice.IsVisible && vm.UsedDictionaryFallback, "Service failure displays a backup notice without changing saved provider settings");
        Check((await database.GetHistoryAsync()).First().IsDictionaryFallback, "History preserves runtime-fallback provenance");
        var beforeCooldown = api.Requests;
        Check((await vm.LookupAsync("sword", "en", "ru", "", default)).IsDictionaryFallback && api.Requests == beforeCooldown, "Subsequent word requests bypass the broken service during cooldown");
        var runtimeSession = cursorSession with { Provider = "LibreTranslate", IsDictionaryTranslation = false, IsDictionaryFallback = false, Blocks = cursorSession.Blocks.Select(b => b with { Translation = "" }).ToArray() };
        UiText.SetLanguage("en");
        var fallbackPopup = new WordLookupWindow(vm, "journey", runtimeSession, runtimeSession.Blocks[0]); fallbackPopup.Show();
        var fallbackModel = (WordLookupViewModel)fallbackPopup.DataContext;
        for (var retry = 0; retry < 100 && !fallbackModel.CanSave; retry++) await Task.Delay(20);
        await Settle();
        Check(fallbackModel.CanSave && fallbackModel.HasProviderFallback && Descendants(fallbackPopup).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "lookup-dictionary-notice" && t.IsVisible && t.Text.Contains("Service failed")), "An already open word popup reveals its runtime-fallback notice and remains usable");
        Save(fallbackPopup, Path.Combine(output, "en-failover-word.png")); UiText.SetLanguage("ru"); await Settle(); Save(fallbackPopup, Path.Combine(output, "ru-failover-word.png"));
        Check(fallbackModel.DictionaryNotice.Text.Contains("Сервис недоступен"), "Runtime-fallback notice changes to Russian in place");
        api.Failure = "success";
        await vm.SaveSettingsAsync(vm.Settings with { ContextHint = "popup recovery" }, null, false);
        await fallbackModel.LoadAsync(); await Settle();
        Check(!fallbackModel.HasProviderFallback && fallbackModel.ContextTranslation == "[LIVE-ADAPTER TEST DOUBLE] translation" && !Descendants(fallbackPopup).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "lookup-dictionary-notice").IsVisible, "Refreshing an existing popup after service recovery replaces dictionary context and removes the warning"); fallbackPopup.Close();
        api.Failure = "offline";
        await vm.SaveSettingsAsync(vm.Settings with { ContextHint = "screen outage" }, null, false);
        await vm.LookupAsync("sword", "en", "ru", "", default); beforeCooldown = api.Requests;
        await vm.RunCaptureAsync("Screen translation");
        Check(vm.LastSession?.IsDictionaryFallback == true && api.Requests == beforeCooldown, "Screen translation also uses the offline fallback during an outage");
        using (var overlays = new OverlayManager(vm, NullLogger<OverlayManager>.Instance))
        {
            overlays.Show(vm.LastSession!); await Settle();
            Check(Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).Any(w => Descendants(w).OfType<TextBlock>().Any(t => t.Text.Contains("Сервис недоступен"))), "Screen overlay identifies a service failure rather than claiming setup is missing"); overlays.Close();
        }
        UiText.SetLanguage("en");
        bool connectionFailed = false;
        try { await vm.TestConnectionAsync(vm.Settings, null, false, default); } catch (UserFacingException) { connectionFailed = true; }
        Check(connectionFailed, "Connection testing reports the failed service instead of disguising fallback as a successful connection");
        api.Failure = "hang";
        await vm.SaveSettingsAsync(vm.Settings with { ContextHint = "hung service" }, null, false);
        var timeoutWatch = System.Diagnostics.Stopwatch.StartNew();
        var timed = await vm.LookupAsync("journey", "en", "ru", "", default);
        Check(timed.IsDictionaryFallback && timeoutWatch.Elapsed < TimeSpan.FromSeconds(9), "A hung HTTP service falls back within the five-second response budget");
        api.Failure = "success";
        await vm.SaveSettingsAsync(vm.Settings with { ContextHint = "recovered service" }, null, false);
        vm.InputText = "Hello, traveler!"; await vm.TranslateTextCommand.ExecuteAsync(null); await Settle();
        Check(!vm.UsedDictionaryFallback && !notice.IsVisible && vm.OutputText == "[LIVE-ADAPTER TEST DOUBLE] translation", "A healthy service replaces fallback output and removes the warning");
        main.Close(); await vm.StopAsync(); UiText.SetLanguage("ru"); return passed;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var item in Descendants(child)) yield return item; }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(visual); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class Keys : ICredentialStore { public string? Read(string name) => null; public void Write(string name, string value) { } public void Delete(string name) { } }
    private sealed class Api : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string Failure { get; set; } = "success";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (Failure == "offline") throw new HttpRequestException("Test server offline");
            if (Failure == "hang") await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"translatedText\":[\"[LIVE-ADAPTER TEST DOUBLE] translation\"]}") };
        }
    }
    private sealed class Capture : IScreenCapture { public Task<CapturedFrame> CaptureAsync(PixelRect bounds, CancellationToken token) => Task.FromResult(new CapturedFrame(new byte[4], 1, 1, bounds)); }
    private sealed class Ocr : IOcrProvider
    {
        public IReadOnlyList<LanguageChoice> AvailableLanguages => [];
        public Task<OcrDocument> RecognizeAsync(CapturedFrame frame, string language, PreprocessingOptions options, CancellationToken token)
        {
            NativeMethods.GetCursorPos(out var cursor);
            var bounds = new PixelRect(cursor.X - 120, cursor.Y - 10, 230, 30);
            return Task.FromResult(new OcrDocument([new(0, "Hello, traveler!", bounds, [new("Hello", new(cursor.X - 120, cursor.Y - 10, 80, 30)), new("traveler", new(cursor.X - 15, cursor.Y - 10, 100, 30))]), new(1, "Qzxvillage", new(cursor.X + 200, cursor.Y + 100, 100, 30), [])], "en"));
        }
    }
}
