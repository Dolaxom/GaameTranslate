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

internal static class SettingsVerification
{
    public static async Task<IReadOnlyList<string>> RunAsync(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
        var directory = Path.Combine(output, "settings-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory); var initial = new AppSettings { Provider = "LibreTranslate", SourceLanguage = "en", TargetLanguage = "ru", OllamaModel = "preserved-custom-model", AssistantProvider = "None" }; await store.SaveAsync(initial);
        var keys = new MemoryKeys(); var api = new RecordingApi(); using var http = new HttpClient(api); using var speech = new WindowsSpeechService();
        var vm = new MainViewModel(new DesktopCapture(), new WindowsOcrProvider(), new LocalDatabase(directory), store, keys, http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, keys, new OfflineDictionaryStore(Path.Combine(directory, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(directory, "media")));
        await vm.InitializeAsync(); var window = new MainWindow(vm) { AllowClose = true }; window.Show(); vm.PageIndex = 5; await Task.Delay(100);
        var view = Descendants(window).OfType<SettingsView>().Single();
        T Find<T>(string id) where T : DependencyObject => Descendants(view).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
        void Click(string id) => Find<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Settle() { await System.Windows.Threading.Dispatcher.Yield(); window.UpdateLayout(); }
        async Task WaitFor(Func<bool> condition)
        {
            for (var retry = 0; retry < 160 && !condition(); retry++) await Task.Delay(25);
            if (!condition()) throw new TimeoutException("UI action did not finish"); await Settle();
        }
        await Settle();
        Check(!view.HasUnsavedChanges && !Find<Button>("settings-save").IsEnabled, "Opening settings does not create unsaved changes");
        Check(!Find<PasswordBox>("translation-key").IsVisible, "Local LibreTranslate hides optional credentials");
        Check((string)Find<Button>("settings-tab-translation").Tag == "selected", "Current settings tab has a visible selected state");
        foreach (var page in new[] { "translation", "screen", "dictionary", "shortcuts", "games", "app" })
        {
            Click("settings-tab-" + page); await Settle(); Save(window, Path.Combine(output, "settings-" + page + ".png"));
            var position = Find<Button>("settings-save").TranslatePoint(new Point(0, 0), window);
            Check(position.Y + Find<Button>("settings-save").ActualHeight < window.ActualHeight, "Save remains visible on settings tab " + page);
        }
        Click("settings-tab-translation"); Find<TextBox>("translation-libre").Text = "http://127.0.0.1:5001";
        Find<TextBox>("context").Text = "Fantasy dialogue"; await Settle();
        Check(view.HasUnsavedChanges && Find<Button>("settings-save").IsEnabled, "Editing a setting enables explicit save and discard");
        vm.PageIndex = 0; vm.PageIndex = 5; await Settle();
        Check(ReferenceEquals(view, Descendants(window).OfType<SettingsView>().Single()) && Find<TextBox>("context").Text == "Fantasy dialogue", "Unsaved draft survives leaving and returning to settings");
        Click("translation-test"); await WaitFor(() => Find<TextBlock>("translation-result").Text.StartsWith("✓"));
        Check(api.LastUri?.Port == 5001 && (await store.LoadAsync()).LibreEndpoint == initial.LibreEndpoint, "Connection check uses the draft address without persisting it");
        Find<TextBox>("historyLimit").Text = "invalid";
        Click("translation-test"); await WaitFor(() => Find<TextBlock>("translation-result").Text.StartsWith("✓"));
        Check(api.LastUri?.Port == 5001, "Unrelated unfinished settings do not block a connection check");
        Click("settings-save"); await Settle();
        Check(Find<TextBox>("historyLimit").IsVisible && (await store.LoadAsync()) == initial, "Validation opens the correct tab and advanced section without partial saves");
        Find<TextBox>("historyLimit").Text = "500"; Click("settings-tab-translation");
        var provider = Find<ComboBox>("provider"); provider.SelectedValue = "Yandex Translate"; await Settle();
        Check(Find<PasswordBox>("translation-key").IsVisible && !Find<TextBox>("translation-ollamaModel").IsVisible, "Cloud service reveals its key and hides unrelated model fields");
        Find<PasswordBox>("translation-key").Password = "test-yandex-key";
        Click("translation-test"); await WaitFor(() => Find<TextBlock>("translation-result").Text.StartsWith("✓"));
        Check(api.LastAuthorization == "Api-Key test-yandex-key" && keys.Read("Yandex Translate") is null, "Draft API key is used for testing without writing credentials");
        provider.SelectedValue = "GigaChat"; Find<PasswordBox>("translation-key").Password = "test-giga-key";
        provider.SelectedValue = "Yandex Translate";
        Check(Find<PasswordBox>("translation-key").Password == "test-yandex-key", "Switching providers preserves each unsaved API key");
        provider.SelectedValue = "OpenAI compatible"; Find<TextBox>("translation-compatible").Text = "https://api.example.test/v1"; await Settle();
        Check(Find<PasswordBox>("translation-key").IsVisible, "Remote compatible APIs show required credentials directly");
        Find<TextBox>("translation-compatible").Text = "http://127.0.0.1:1234/v1";
        Click("settings-tab-dictionary"); Find<ComboBox>("assistant").SelectedValue = "YandexGPT"; await Settle();
        Check(Find<TextBox>("assistant-gpt-folder").IsVisible && !Find<TextBox>("assistant-assistantModel").IsVisible, "Dictionary assistant shows only its relevant connection fields");
        Find<ComboBox>("assistant").SelectedValue = "None"; Click("settings-tab-screen"); Find<Slider>("font").Value = 25; Find<Slider>("opacity").Value = 82;
        Click("settings-tab-translation"); provider.SelectedValue = "LibreTranslate"; Click("settings-save"); await WaitFor(() => !view.HasUnsavedChanges);
        var saved = await store.LoadAsync();
        Check(saved.LibreEndpoint.EndsWith(":5001") && saved.OverlayFontSize == 25 && saved.OverlayOpacity == .82 && saved.OllamaModel == "preserved-custom-model", "Saving persists changes across tabs and preserves inactive provider settings");
        Check(keys.Read("Yandex Translate") == "test-yandex-key" && keys.Read("GigaChat") == "test-giga-key", "Explicit save stores keys for both edited providers");
        Find<TextBox>("context").Text = "Throw this edit away"; Click("settings-discard"); await Settle();
        Check(!view.HasUnsavedChanges && Find<TextBox>("context").Text == saved.ContextHint, "Discard restores saved values instead of factory defaults");
        provider = Find<ComboBox>("provider"); provider.SelectedValue = "Yandex Translate";
        Click("translation-remove-key"); Click("settings-save"); await WaitFor(() => !view.HasUnsavedChanges);
        Check(keys.Read("Yandex Translate") is null && keys.Read("GigaChat") == "test-giga-key", "Key removal applies only to the explicitly selected provider");
        provider.SelectedValue = "LibreTranslate"; Click("settings-save"); await WaitFor(() => !view.HasUnsavedChanges);
        var beforeInvalid = await store.LoadAsync();
        Click("settings-tab-shortcuts"); await Settle(); Find<TextBox>("key-ScreenTranslation").Focus(); await Settle();
        Check(vm.IsRecordingShortcut, "Editing a shortcut suspends application shortcut dispatch");
        Find<Button>("settings-tab-translation").Focus(); await Settle();
        Check(!vm.IsRecordingShortcut, "Leaving shortcut entry restores application shortcut dispatch");
        Find<TextBox>("key-Region").Text = Find<TextBox>("key-ScreenTranslation").Text; Click("settings-save"); await Settle();
        Check((await store.LoadAsync()) == beforeInvalid && Find<TextBlock>("settings-feedback").Text.Contains("сочетание"), "Duplicate shortcuts show actionable feedback without saving partial settings");
        Click("settings-discard"); Click("settings-tab-translation");
        api.Delay = true; Click("translation-test"); await WaitFor(() => Find<Button>("translation-test").Content?.ToString() == "Отменить проверку"); Click("translation-test");
        await WaitFor(() => Find<TextBlock>("translation-result").Text == "Проверка отменена."); api.Delay = false;
        Check(!view.HasUnsavedChanges, "Connection checks can be cancelled without changing settings");
        provider = Find<ComboBox>("provider"); provider.SelectedValue = "Ollama"; await Settle(); Save(window, Path.Combine(output, "settings-ollama.png"));
        window.Width = 960; window.Height = 650;
        foreach (var page in new[] { "translation", "screen", "dictionary", "shortcuts", "games", "app" })
        {
            Click("settings-tab-" + page); await Settle(); Save(window, Path.Combine(output, "settings-narrow-" + page + ".png"));
            var button = Find<Button>("settings-save"); var p = button.TranslatePoint(new Point(0, 0), window);
            Check(p.X >= 0 && p.Y >= 0 && p.X + button.ActualWidth <= window.ActualWidth && p.Y + button.ActualHeight <= window.ActualHeight, "Narrow layout retains save action on " + page);
        }
        GaameTranslate.App.App.ApplyTheme(true); Click("settings-tab-translation"); await Settle(); Save(window, Path.Combine(output, "settings-light.png"));
        Check(((SolidColorBrush)window.Background).Color == Color.FromRgb(244, 246, 248), "Settings inherit the real light theme");
        GaameTranslate.App.App.ApplyTheme(false); window.Close(); await vm.StopAsync(); return passed;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        image.Render(background); image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class MemoryKeys : ICredentialStore
    {
        private readonly Dictionary<string, string> keys = new();
        public string? Read(string name) => keys.GetValueOrDefault(name);
        public void Write(string name, string value) => keys[name] = value;
        public void Delete(string name) => keys.Remove(name);
    }
    private sealed class RecordingApi : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public bool Delay { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastUri = request.RequestUri; LastAuthorization = request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.Single() : null;
            if (Delay) await Task.Delay(Timeout.Infinite, token);
            return new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.Host.Contains("yandex") ? "{\"translations\":[{\"text\":\"[API TEST DOUBLE] Проверка подключения\",\"detectedLanguageCode\":\"en\"}]}" : "{\"translatedText\":[\"[API TEST DOUBLE] Проверка подключения\"]}") };
        }
    }
}
