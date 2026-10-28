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

internal static class OnboardingVerification
{
    public static async Task<IReadOnlyList<string>> RunAsync(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
        async Task Settle() { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        async Task Wait(Func<bool> done) { for (var i = 0; i < 150 && !done(); i++) await Task.Delay(20); if (!done()) throw new TimeoutException("Onboarding UI did not settle"); await Settle(); }
        var data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")); var store = new SettingsStore(data); var keys = new Keys(); using var api = new Api(); using var http = new HttpClient(api); using var speech = new WindowsSpeechService();
        var vm = new MainViewModel(new DesktopCapture(), new WindowsOcrProvider(), new LocalDatabase(data), store, keys, http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, keys, new OfflineDictionaryStore(Path.Combine(data, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(data, "media")));
        await vm.InitializeAsync(); UiText.SetLanguage("en"); var main = new MainWindow(vm) { AllowClose = true }; main.Show(); await Settle();
        T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
        void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.NotifyStartup(); await Settle();
        Check(Find<Border>(main, "startup-dictionary-tip").IsVisible && api.Requests == 0, "Dictionary startup notice appears without API calls"); Save(main, Path.Combine(output, "en-startup.png"));
        vm.DismissStartupTipCommand.Execute(null); await Settle(); Check(!Find<Border>(main, "startup-dictionary-tip").IsVisible, "Startup notice can be dismissed without changing settings");
        vm.NotifyStartup(); UiText.SetLanguage("ru"); await Settle(); Save(main, Path.Combine(output, "ru-startup.png"));
        Check(Descendants(main).OfType<TextBlock>().Any(t => t.Text == "Можно начать со словаря"), "Startup notice switches to Russian");
        vm.OpenQuickStartCommand.Execute(null); await Settle(); var guide = Application.Current.Windows.OfType<QuickStartWindow>().Single(); Save(guide, Path.Combine(output, "ru-guide.png"));
        vm.OpenQuickStartCommand.Execute(null); Check(Application.Current.Windows.OfType<QuickStartWindow>().Count() == 1, "Repeated help actions reuse a single guide window");
        UiText.SetLanguage("en"); await Settle(); Check(guide.Title == "A quick start, at your pace", "An open guide changes language without reopening"); Save(guide, Path.Combine(output, "en-guide.png"));
        var guideContent = (FrameworkElement)guide.Content;
        Check(Find<Button>(guide, "guide-sample").TranslatePoint(new Point(0, Find<Button>(guide, "guide-sample").ActualHeight), guideContent).Y <= guideContent.ActualHeight, "Guide actions fit inside the window while its text scrolls");
        vm.InputText = "My unsaved text"; Click(guide, "guide-sample"); await Settle();
        Check(vm.PageIndex == 1 && vm.InputText == "My unsaved text" && api.Requests == 0, "Sample action preserves existing text and does not translate or capture automatically");
        vm.InputText = ""; vm.TrySampleCommand.Execute(null); await vm.TranslateTextCommand.ExecuteAsync(null);
        Check(vm.OutputText.Any(c => c is >= 'А' and <= 'я') && api.Requests == 0, "The prepared English sample can be translated offline");
        vm.OpenSettingsCommand.Execute(null); await Settle(); var settings = Descendants(main).OfType<SettingsView>().Single();
        var provider = Find<ComboBox>(settings, "provider"); provider.SelectedValue = "LibreTranslate";
        Find<TextBox>(settings, "translation-libre").Text = "http://127.0.0.1:5001";
        vm.OpenQuickStartCommand.Execute(null); await Settle(); guide = Application.Current.Windows.OfType<QuickStartWindow>().Single(); Click(guide, "guide-shortcuts"); await Settle();
        Check(settings.HasUnsavedChanges && Find<TextBox>(settings, "translation-libre").Text.EndsWith(":5001") && Find<Button>(settings, "settings-tab-shortcuts").Tag?.ToString() == "selected", "Guide navigation opens the right settings tab and retains connection drafts");
        settings.OpenTab("translation"); UiText.SetLanguage("en");
        foreach (var service in ProviderCatalog.All)
        {
            provider.SelectedValue = service.Name; await Settle();
            Check(Find<TextBlock>(settings, "translation-guide-steps").Text == UiText.Get(ProviderSetupGuides.Get(service.Name).Steps), "Three-step help follows connector " + service.Name);
        }
        provider.SelectedValue = "LibreTranslate"; await Settle(); Save(main, Path.Combine(output, "en-connector.png"));
        Check(Find<TextBlock>(settings, "translation-health").Text == "Not checked yet", "Saved credentials are not presented as a verified connection");
        Click(settings, "translation-test"); await Wait(() => Find<TextBlock>(settings, "translation-health").Text.Contains("Connection works"));
        Check(Find<TextBlock>(settings, "translation-health").Text.Contains("save to apply"), "A successful draft check tells the user to save");
        Find<TextBox>(settings, "translation-libre").Text = "http://127.0.0.1:5002";
        Check(Find<TextBlock>(settings, "translation-health").Text == "Not checked yet", "Editing the endpoint removes the old successful status");
        api.Fail = true; Click(settings, "translation-test"); await Wait(() => Find<TextBlock>(settings, "translation-health").Text.Contains("Needs attention"));
        Check(Find<TextBlock>(settings, "translation-result").Text.Length > 0, "Connection failure keeps the actual error beside the setup guide");
        api.Fail = false; api.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); Click(settings, "translation-test"); await Wait(() => Find<TextBlock>(settings, "translation-health").Text.Contains("Checking"));
        Find<TextBox>(settings, "translation-libre").Text = "http://127.0.0.1:5003"; api.Gate.SetResult();
        await Wait(() => Find<Button>(settings, "translation-test").Content.ToString() == "Check connection"); api.Gate = null;
        Check(Find<TextBlock>(settings, "translation-health").Text == "Not checked yet", "A late success cannot verify parameters changed during the check");
        Click(settings, "translation-test"); await Wait(() => Find<TextBlock>(settings, "translation-health").Text.Contains("Connection works"));
        Click(settings, "settings-save"); await Wait(() => !settings.HasUnsavedChanges);
        UiText.SetLanguage("en"); await Settle();
        Check(Find<TextBlock>(settings, "translation-health").Text == "Connection checked · ready", "Saving a verified configuration removes the save reminder");
        vm.NotifyStartup(); Check(!vm.IsStartupTipVisible, "Configured translation does not show the dictionary startup notice");
        await vm.LookupAsync("warmprobe", "en", "ru", "", default); var cachedCalls = api.Requests;
        await vm.SaveSettingsAsync(vm.Settings with { ShowStartupTips = false }, null, false);
        await vm.LookupAsync("warmprobe", "en", "ru", "", default);
        Check(vm.InterfaceOnlySettingsChange && api.Requests == cachedCalls, "Changing only startup tips keeps the fast translation cache");
        await vm.SaveSettingsAsync(vm.Settings with { Provider = ProviderCatalog.BuiltInDictionary, ShowStartupTips = false }, null, false); vm.NotifyStartup();
        Check(!vm.IsStartupTipVisible && !(await store.LoadAsync()).ShowStartupTips, "Disabling startup tips is persisted and respected");
        vm.OpenQuickStartCommand.Execute(null); await Settle(); Check(Application.Current.Windows.OfType<QuickStartWindow>().Any(), "The guide remains available when startup tips are disabled"); Application.Current.Windows.OfType<QuickStartWindow>().Single().Close();
        main.Width = 960; main.Height = 650; vm.PageIndex = 5; settings.RefreshIfClean(); settings.OpenTab("translation"); UiText.SetLanguage("ru"); await Settle(); Save(main, Path.Combine(output, "ru-small-settings.png"));
        Check(Find<Button>(settings, "settings-save").IsVisible, "Small-window help keeps the settings actions visible");
        main.Close(); await vm.StopAsync(); UiText.SetLanguage("ru"); return passed;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var item in Descendants(child)) yield return item; }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); } bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class Keys : ICredentialStore { public string? Read(string name) => null; public void Write(string name, string value) { } public void Delete(string name) { } }
    private sealed class Api : HttpMessageHandler
    {
        public int Requests; public bool Fail; public TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++; if (Gate is not null) await Gate.Task;
            return new HttpResponseMessage(Fail ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent("{\"translatedText\":[\"[TEST] Привет, путешественник!\"]}") };
        }
    }
}
