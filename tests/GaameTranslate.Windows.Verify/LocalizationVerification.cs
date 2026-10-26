using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaameTranslate.App;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using GaameTranslate.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaameTranslate.Verify;

internal static class LocalizationVerification
{
    public static async Task<IReadOnlyList<string>> RunAsync(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
        async Task Settle() { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        var data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")); var store = new SettingsStore(data); var keys = new Keys(); using var api = new Api(); using var http = new HttpClient(api); using var speech = new WindowsSpeechService();
        await store.SaveAsync(new AppSettings { InterfaceLanguage = "ru", SourceLanguage = "auto", AssistantProvider = "None" });
        var vm = new MainViewModel(new DesktopCapture(), new WindowsOcrProvider(), new LocalDatabase(data), store, keys, http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, keys, new OfflineDictionaryStore(Path.Combine(data, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(data, "media")));
        await vm.InitializeAsync(); UiText.SetLanguage("ru");
        await vm.SaveWordAsync(new VocabularyEntry { Word = "Save", Translation = "Home", Context = "Do not translate user content.", Notes = "Моя заметка", SourceLanguage = "en", TargetLanguage = "ru" });
        var main = new MainWindow(vm) { AllowClose = true }; main.Show(); await Settle();
        T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
        bool Text(Window root, string text) => Descendants(root).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == text);
        void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Text(main, "Меньше догадок."), "Home opens fully in Russian");
        var rootContent = main.Content; UiText.SetLanguage("en"); await Settle();
        Check(Text(main, "A little less guessing.") && ReferenceEquals(rootContent, main.Content), "Language switch updates the existing main window without rebuilding it");
        var missingResources = new HashSet<string>();
        void Audit(DependencyObject root)
        {
            foreach (var element in Descendants(root))
            foreach (var property in new[] { TextBlock.TextProperty, ContentControl.ContentProperty, HeaderedContentControl.HeaderProperty, Window.TitleProperty, AutomationProperties.NameProperty, FrameworkElement.ToolTipProperty })
            {
                if (BindingOperations.GetMultiBinding(element, property) is not { } binding || binding.Bindings.FirstOrDefault() is not Binding { Source: UiMessage message }) continue;
                if (message.Key.Any(char.IsLetter) && !UiText.Catalogue.ContainsKey(message.Key)) missingResources.Add(message.Key);
            }
        }
        foreach (var language in new[] { "ru", "en" })
        {
            UiText.SetLanguage(language);
            for (var page = 0; page < 6; page++) { vm.PageIndex = page; await Settle(); Audit(main); Save(main, Path.Combine(output, $"{language}-page-{page}.png")); }
            Check(true, "All six main pages render in " + language);
        }
        var settings = Descendants(main).OfType<SettingsView>().Single();
        Click(settings, "settings-tab-translation"); Find<TextBox>(settings, "context").Text = "Unsaved game context";
        Find<PasswordBox>(settings, "translation-key").Password = "draft-test-key";
        var editor = new WordEditorWindow(vm) { Owner = main }; editor.Show(); await Settle();
        Find<TextBox>(editor, "word-editor-word").Text = "Не менять / Keep this";
        Find<TextBox>(editor, "word-editor-translation").Text = "Home";
        Find<ComboBox>(editor, "word-editor-source").SelectedValue = "en";
        UiText.SetLanguage("ru"); await Settle();
        Check(Find<Button>(editor, "word-editor-save").Content.ToString() == "Добавить слово", "Already open card form switches to Russian");
        Save(editor, Path.Combine(output, "ru-word-editor.png"));
        UiText.SetLanguage("en"); await Settle();
        Check(Find<Button>(editor, "word-editor-save").Content.ToString() == "Add word" && editor.Title == "Add word", "Card form buttons and title switch to English"); Audit(editor);
        Check(Find<TextBox>(editor, "word-editor-word").Text == "Не менять / Keep this" && Find<TextBox>(editor, "word-editor-translation").Text == "Home" && (string)Find<ComboBox>(editor, "word-editor-source").SelectedValue == "en", "Language switching preserves typed card content and chosen languages");
        Save(editor, Path.Combine(output, "en-word-editor.png"));
        Find<TextBox>(editor, "word-editor-word").Clear(); Click(editor, "word-editor-save"); await Settle();
        Check(Find<TextBlock>(editor, "word-editor-feedback").Text == "Enter a word or expression.", "Validation is displayed in English");
        UiText.SetLanguage("ru"); await Settle();
        Check(Find<TextBlock>(editor, "word-editor-feedback").Text == "Введите слово или выражение.", "An existing validation message switches language in place"); editor.Close();
        Check(settings.HasUnsavedChanges && Find<TextBox>(settings, "context").Text == "Unsaved game context" && Find<PasswordBox>(settings, "translation-key").Password == "draft-test-key", "Language switching preserves settings and API-key drafts");
        vm.PageIndex = 2; await Settle(); var selected = vm.SelectedWord?.Id;
        UiText.SetLanguage("en"); await Settle();
        Check(vm.SelectedWord?.Id == selected && Text(main, "Home") && vm.Words.Single().Notes == "Моя заметка", "Vocabulary selection and stored text survive an interface language change");
        var error = new UserFacingException(UiText.M("The translation provider returned HTTP {0}. Check its availability and your configuration.", 403));
        Check(error.Message.Contains("HTTP 403") && error.Message.StartsWith("The translation"), "Provider errors use the current English interface language");
        UiText.SetLanguage("ru"); Check(error.Message.StartsWith("Переводчик") && error.Message.Contains("403"), "Existing provider errors preserve their arguments when changing language");
        vm.Status = UiText.M("Imported {0} words. Existing words update their encounter count.", 7); await Settle();
        Check(Text(main, "Импортировано слов: 7. У существующих слов обновлён счётчик встреч."), "Parameterized status is localized in Russian");
        UiText.SetLanguage("en"); await Settle(); Check(Text(main, "Imported 7 words. Existing words update their encounter count."), "Existing parameterized status switches to English");
        var original = new OcrBlock(0, "User sentence", new PixelRect(200, 250, 260, 40), [new("Save", new(200, 250, 60, 40))]);
        var block = new TranslatedBlock(original, "Home"); var session = new TranslationSession(Guid.NewGuid(), DateTimeOffset.UtcNow, original.Bounds, "en", "ru", "LibreTranslate", "Repeat", [block], new(0, 0, 0, 0));
        var lookup = new WordLookupWindow(vm, "Save", session, block, new("Save", "User definition", "My dictionary") { Translation = "Home" }); lookup.Show(); await Settle();
        Check(Find<Button>(lookup, "lookup-save").Content.ToString() == "☆ Save", "Dictionary popup actions use English"); Audit(lookup);
        UiText.SetLanguage("ru"); await Settle();
        Check(Find<Button>(lookup, "lookup-save").Content.ToString() == "☆ Сохранить" && Find<TextBlock>(lookup, "lookup-translation").Text == "Home", "Open dictionary localizes its actions while preserving the actual translation");
        Save(lookup, Path.Combine(output, "ru-lookup.png")); UiText.SetLanguage("en"); await Settle(); Save(lookup, Path.Combine(output, "en-lookup.png"));
        await vm.LookupWordAsync("cacheprobe", "en", "ru", vm.Settings, CancellationToken.None);
        var requests = api.Requests; await vm.SaveSettingsAsync(vm.Settings with { InterfaceLanguage = "en" }, null, false);
        await vm.LookupWordAsync("cacheprobe", "en", "ru", vm.Settings, CancellationToken.None);
        Check(api.Requests == requests && vm.InterfaceOnlySettingsChange && lookup.IsVisible, "An interface-only change retains the fast word cache and open dictionary"); lookup.Close();
        using (var overlay = new OverlayManager(vm, NullLogger<OverlayManager>.Instance))
        {
            overlay.Show(session); await Settle(); var window = Application.Current.Windows.Cast<Window>().Single(w => w.IsVisible && w.Title == UiText.Get("GaameTranslate · translation"));
            Check(Descendants(window).OfType<Button>().Any(b => b.Content?.ToString() == "Copy"), "Screen overlay opens with English controls"); Audit(window);
            UiText.SetLanguage("ru"); await Settle(); Check(Descendants(window).OfType<Button>().Any(b => b.Content?.ToString() == "Копировать") && window.Title == "GaameTranslate · перевод", "Open screen overlay controls and title switch to Russian");
            overlay.Close();
        }
        await vm.SaveSettingsAsync(vm.Settings with { InterfaceLanguage = "ru" }, null, false);
        vm.PageIndex = 5; await Settle(); settings = Descendants(main).OfType<SettingsView>().Single(); Click(settings, "settings-discard"); Click(settings, "settings-tab-app");
        Find<ComboBox>(settings, "interfaceLanguage").SelectedValue = "en"; Click(settings, "settings-save");
        for (var retry = 0; retry < 100 && settings.HasUnsavedChanges; retry++) await Task.Delay(20); await Settle();
        Check(UiText.Language == "en" && (await store.LoadAsync()).InterfaceLanguage == "en", "Saving the interface choice changes language and persists it for the next launch");
        var reloaded = await new SettingsStore(data).LoadAsync(); UiText.SetLanguage(reloaded.InterfaceLanguage);
        Check(reloaded.SourceLanguage == "auto" && reloaded.TargetLanguage == "ru", "Interface language does not change the selected translation languages");
        main.Width = 960; main.Height = 650;
        foreach (var language in new[] { "ru", "en" }) { UiText.SetLanguage(language); await Settle(); Save(main, Path.Combine(output, language + "-settings-narrow.png")); Check(Find<Button>(settings, "settings-save").IsVisible, "Settings actions remain visible in the small " + language + " layout"); }
        await File.WriteAllTextAsync(Path.Combine(output, "missing-resources.json"), JsonSerializer.Serialize(missingResources));
        Check(missingResources.Count == 0, "All visited interface bindings have bilingual resources: " + string.Join("; ", missingResources));
        main.Close(); await vm.StopAsync(); UiText.SetLanguage("ru"); return passed;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var item in Descendants(child)) yield return item; }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class Keys : ICredentialStore { public string? Read(string provider) => null; public void Write(string provider, string value) { } public void Delete(string provider) { } }
    private sealed class Api : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Requests++; return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"translatedText\":[\"[API TEST DOUBLE] cached word\"]}") }); }
    }
}
