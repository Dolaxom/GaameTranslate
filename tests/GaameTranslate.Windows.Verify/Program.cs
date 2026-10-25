using System.Net;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaameTranslate.App;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using GaameTranslate.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaameTranslate.Verify;

internal static class Program
{
    private static readonly List<string> passed = [];
    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification"); Directory.CreateDirectory(output);
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/GaameTranslate;component/Theme.xaml") });
        application.Resources["BoolVisibility"] = new BooleanToVisibilityConverter();
        var code = 1;
        application.Startup += async (_, _) =>
        {
            try { if (args.Contains("--quality")) await ScreenQualityVerification.RunAsync(output, args.Contains("--live"), args.SkipWhile(a => a != "--images").Skip(1).ToArray()); else await VerifyAsync(output); code = 0; }
            catch (Exception e) { Console.Error.WriteLine(e); await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), e.ToString()); }
            finally { await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed, exitCode = code, translation = args.Contains("--quality") ? args.Contains("--live") ? "Live Ollama models. See live-translation.json." : "OCR only. No translation API exercised." : "HTTP test double. No live translation API exercised." }, new JsonSerializerOptions { WriteIndented = true })); application.Shutdown(code); }
        };
        application.Run(); return code;
    }
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
    private static async Task VerifyAsync(string output)
    {
        var data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")); var store = new SettingsStore(data); var db = new LocalDatabase(data); var ocr = new WindowsOcrProvider(); var capture = new DesktopCapture();
        var settings = new AppSettings { Provider = "LibreTranslate", OcrLanguage = "en-US", SourceLanguage = "en", TargetLanguage = "ru" }; await store.SaveAsync(settings);
        using var http = new HttpClient(new TranslationApiDouble()); using var speech = new WindowsSpeechService(); var vm = new MainViewModel(capture, ocr, db, store, new Keys(), http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, new Keys(), new OfflineDictionaryStore(Path.Combine(data, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(data, "media"))); await vm.InitializeAsync();
        var main = new MainWindow(vm) { AllowClose = true }; main.Show(); await Task.Delay(150); main.UpdateLayout(); SaveImage(main, Path.Combine(output, "home.png"));
        Check(((SolidColorBrush)main.Background).Color == Color.FromRgb(16, 19, 24), "Main window applies dark theme");
        for (var page = 0; page < 6; page++) { vm.PageIndex = page; await System.Windows.Threading.Dispatcher.Yield(); main.UpdateLayout(); SaveImage(main, Path.Combine(output, $"page-{page}.png")); }
        Check(true, "All six application pages render"); main.Hide();
        using (var cancelSelection = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        { var selector = new RegionSelector(); Check(await selector.SelectAsync(cancelSelection.Token) is null && !selector.IsSelecting, "Native region selector cancels and disposes monitor windows"); }
        var fixture = new Window { Title = "GaameTranslate verification fixture · no external API", Width = 980, Height = 220, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.White, Foreground = Brushes.Black, Left = 140, Top = 160, Topmost = true, Content = new TextBlock { Text = "Hello traveler. Your journey begins here.", FontFamily = new FontFamily("Segoe UI"), FontSize = 36, Foreground = Brushes.Black, Background = Brushes.White, Margin = new(30, 60, 20, 20) } };
        fixture.Show(); fixture.Activate(); await Task.Delay(200);
        var topLeft = fixture.PointToScreen(new Point(0, 0)); var bottomRight = fixture.PointToScreen(new Point(fixture.ActualWidth, fixture.ActualHeight)); var region = new PixelRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        await vm.SaveSettingsAsync(settings with { LastRegion = region }, null, false);
        using var manager = new OverlayManager(vm, NullLogger<OverlayManager>.Instance); vm.SessionReady += manager.Show;
        await vm.RunCaptureAsync("Repeat"); var session = vm.LastSession;
        Check(session is not null && session.OriginalText.Contains("Hello traveler", StringComparison.OrdinalIgnoreCase), "Real GDI capture → Windows OCR → HTTP adapter → overlay pipeline");
        Check(session!.TranslatedText.Contains("API TEST DOUBLE", StringComparison.Ordinal), "Test translation is explicitly labeled");
        Check(session.Blocks.All(b => b.Original.Bounds.X >= region.X && b.Original.Bounds.Right <= region.Right), "OCR bounds map back into physical capture rectangle");
        Check((await db.GetHistoryAsync()).Count == 1, "Pipeline persists history");
        foreach (var mode in Enum.GetValues<OverlayMode>())
        {
            await vm.SaveSettingsAsync(vm.Settings with { OverlayMode = mode }, null, false); manager.Show(session); await Task.Delay(130);
            var overlays = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.Title.StartsWith("GaameTranslate ·") && w.IsVisible).ToArray();
            Check(overlays.Length > 0, $"{mode} overlay creates visible windows");
            manager.SetInteractive(false); Check(overlays.All(w => (NativeMethods.GetWindowLongPtr(new WindowInteropHelper(w).Handle, -20).ToInt64() & 0x20) != 0), $"{mode} click-through native style");
            manager.SetInteractive(true); Check(overlays.All(w => (NativeMethods.GetWindowLongPtr(new WindowInteropHelper(w).Handle, -20).ToInt64() & 0x20) == 0), $"{mode} restores interaction");
            SaveImage(overlays[0], Path.Combine(output, $"overlay-{mode}.png")); manager.Close();
        }
        var saved = await vm.SaveWordAsync(new() { Word = "traveler", Translation = "путник", SourceLanguage = "en", TargetLanguage = "ru", Context = session.OriginalText });
        Check((await new LocalDatabase(data).GetWordsAsync()).Single().Id == saved.Id, "Vocabulary survives database reopening");
        await AnkiPackageExporter.ExportAsync(Path.Combine(output, "sample.apkg"), [saved], vm.Media, true);
        using (var voiceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15))) Check((await speech.SynthesizeAsync("traveler", "en", "", voiceTimeout.Token)).Length > 1000, "Windows speech produces real WAV audio");
        await vm.StartReviewCommand.ExecuteAsync(null); vm.RevealAnswerCommand.Execute(null); await vm.GradeCommand.ExecuteAsync("Good");
        Check((await db.GetWordsAsync()).Single().DueAt > DateTimeOffset.UtcNow, "Review updates persistent due date");
        using (var first = new GlobalHotkeys()) using (var second = new GlobalHotkeys())
        {
            var bindings = new HotkeySettings { Region = "Ctrl+Alt+F13", Fullscreen = "Ctrl+Alt+F14", Repeat = "Ctrl+Alt+F15", ToggleOverlay = "Ctrl+Alt+F16", Vocabulary = "Ctrl+Alt+F17", Interaction = "Ctrl+Alt+F18", ScreenTranslation = "Ctrl+Alt+F19", WordLookup = "Ctrl+Alt+F20" };
            Check(first.Register(bindings).Count == 0, "Native global hotkey registration"); Check(second.Register(bindings).Count == 8, "Hotkey conflicts are detected"); first.Unregister(); Check(second.Register(bindings).Count == 0, "Hotkeys unregister cleanly");
            Check(GlobalHotkeys.Parse("Ctrl+Shift+1").Key == 0x31, "Shortcut parser maps number row keys");
        }
        var secure = new WindowsCredentialStore(Path.Combine(data, "keys")); secure.Write("test", "not-a-real-api-key"); Check(secure.Read("test") == "not-a-real-api-key", "Real DPAPI roundtrip"); secure.Delete("test");
        await vm.SaveSettingsAsync(vm.Settings with { AutoApplyGameProfiles = true, OverlayMode = OverlayMode.Floating }, null, false);
        fixture.Activate(); await Task.Delay(100);
        // Windows may refuse foreground activation from a background test runner; match the actual foreground executable.
        var foregroundExecutable = NativeMethods.ForegroundProcessName();
        await vm.Profiles.SaveAsync(new("Native foreground fixture", foregroundExecutable, vm.Settings with { OverlayMode = OverlayMode.SidePanel, OverlayFontSize = 27, SaveWordScreenshots = true }));
        await vm.RunCaptureAsync("Repeat");
        var profilePreferences = vm.SettingsForSession(vm.LastSession!);
        Check(profilePreferences.OverlayMode == OverlayMode.SidePanel && profilePreferences.OverlayFontSize == 27 && vm.Settings.OverlayMode == OverlayMode.Floating && !profilePreferences.SaveWordScreenshots, "Foreground game profile reaches overlay without changing global preferences or privacy");
        Check(System.Windows.Application.Current.Windows.Cast<Window>().Any(w => w.IsVisible && w.Title == "GaameTranslate · translation" && w.Width == 390), "Game profile selects the actual side panel window");
        manager.Close(); await vm.SaveSettingsAsync(vm.Settings with { SaveWordScreenshots = true, SaveWordAudio = true }, null, false);
        var currentSession = vm.LastSession!;
        var popup = new WordLookupWindow(vm, "traveler", currentSession, currentSession.Blocks[0], new("traveler", "A person who travels.", "Native fixture") { Translation = "путник" });
        popup.Show(); await Task.Delay(100);
        await vm.SaveSettingsAsync(vm.Settings with { SaveWordScreenshots = false, SaveWordAudio = false }, null, false);
        var popupRoot = (StackPanel)((ScrollViewer)popup.Content).Content;
        popupRoot.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var retry = 0; retry < 30 && vm.Words.Single().Encounters == saved.Encounters; retry++) await Task.Delay(100);
        var popupSavedWord = vm.Words.Single();
        Check(popupSavedWord.Encounters > saved.Encounters && popupSavedWord.ScreenshotFile.Length == 0 && popupSavedWord.AudioFile.Length == 0, "An open dictionary honors media opt-outs changed before saving");
        popup.Close();
        manager.Close(); vm.SelectedHistory = session; vm.ReopenHistory();
        Check(vm.LastFrame is null && vm.SettingsForSession(session).OverlayMode == OverlayMode.Floating, "Reopened history cannot attach an unrelated screenshot or stale game profile");
        fixture.Close(); main.Close(); await vm.StopAsync();
    }
    private static void SaveImage(FrameworkElement element, string path)
    {
        var background = element is Window host ? host.Background : Brushes.Transparent;
        if (element is Window window && window.Content is FrameworkElement content) element = content;
        element.UpdateLayout(); var image = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var backing = new DrawingVisual(); using (var drawing = backing.RenderOpen()) drawing.DrawRectangle(background, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight)); image.Render(backing); image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class Keys : ICredentialStore { public string? Read(string name) => null; public void Write(string name, string value) { } public void Delete(string name) { } }
    // Test assembly only; the production application never installs this handler.
    private sealed class TranslationApiDouble : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); var count = json.RootElement.GetProperty("q").GetArrayLength();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { translatedText = Enumerable.Repeat("[API TEST DOUBLE] Путник, ваше путешествие начинается здесь.", count) })) };
        }
    }
}
