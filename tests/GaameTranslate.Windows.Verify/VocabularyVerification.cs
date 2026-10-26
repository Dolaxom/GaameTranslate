using System.IO;
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

internal static class VocabularyVerification
{
    public static async Task<IReadOnlyList<string>> RunAsync(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
        var directory = Path.Combine(output, "vocabulary-" + Guid.NewGuid().ToString("N")); var db = new LocalDatabase(directory); var settings = new SettingsStore(directory);
        await settings.SaveAsync(new AppSettings { SourceLanguage = "auto", TargetLanguage = "ru", AssistantProvider = "None" });
        var keys = new NoKeys(); using var http = new HttpClient(); using var speech = new WindowsSpeechService();
        var vm = new MainViewModel(new DesktopCapture(), new WindowsOcrProvider(), db, settings, keys, http, new TranslationCache(), NullLogger<MainViewModel>.Instance, new DictionaryService(http, keys, new OfflineDictionaryStore(Path.Combine(directory, "dictionaries"))), speech, new LocalMediaStore(Path.Combine(directory, "media")));
        await vm.InitializeAsync(); var main = new MainWindow(vm) { AllowClose = true }; main.Show(); vm.PageIndex = 2;
        async Task Settle() { await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); main.UpdateLayout(); }
        async Task WaitFor(Func<bool> done) { for (var n = 0; n < 160 && !done(); n++) await Task.Delay(25); if (!done()) throw new TimeoutException(); await Settle(); }
        T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
        void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool InWindow(FrameworkElement control, Window window) { var p = control.TranslatePoint(new Point(), window); return control.IsVisible && p.X >= 0 && p.Y >= 0 && p.X + control.ActualWidth < window.ActualWidth && p.Y + control.ActualHeight < window.ActualHeight; }
        await Settle(); Save(main, Path.Combine(output, "vocabulary-empty.png"));
        Check(Find<Button>(main, "vocabulary-empty-add").IsVisible && !Find<Button>(main, "vocabulary-edit").IsVisible, "Empty collection offers a clear first-word action");
        var editor = new WordEditorWindow(vm) { Owner = main }; editor.Show(); await Settle(); Save(editor, Path.Combine(output, "word-add.png"));
        Check(InWindow(Find<Button>(editor, "word-editor-save"), editor), "New word form keeps save visible without scrolling");
        Check(!Find<Expander>(editor, "word-editor-context-section").IsExpanded && !Find<TextBox>(editor, "word-editor-definition").IsVisible, "New card starts with only the essential fields");
        Click(editor, "word-editor-save"); await Settle();
        Check(Find<TextBlock>(editor, "word-editor-feedback").Text.Contains("Введите слово") && (await db.GetWordsAsync()).Count == 0, "Blank word shows local validation without persistence");
        Find<TextBox>(editor, "word-editor-word").Text = "traveler"; Click(editor, "word-editor-save"); await Settle();
        Check(Find<TextBlock>(editor, "word-editor-feedback").Text.Contains("перевод"), "Missing translation points to the required field");
        Find<TextBox>(editor, "word-editor-translation").Text = "путешественник"; Click(editor, "word-editor-save"); await Settle();
        Check(Find<TextBlock>(editor, "word-editor-feedback").Text.Contains("язык слова"), "Auto-detected app language requires an explicit language for a manual card");
        Find<ComboBox>(editor, "word-editor-source").SelectedValue = "en";
        Find<TextBox>(editor, "word-editor-context").Text = "Hello traveler. Your journey begins here."; Find<TextBox>(editor, "word-editor-tags").Text = "Disco Elysium, диалоги";
        Click(editor, "word-editor-save"); await WaitFor(() => !editor.IsVisible && vm.Words.Count == 1);
        Check(vm.Words.Single().SourceLanguage == "en" && vm.Words.Single().Context.Contains("journey") && vm.Words.Single().Tags.Contains("диалоги"), "Manual card persists chosen language, context and tags");
        Check(vm.SelectedWord?.Word == "traveler", "Saved card is selected in the collection");
        var rich = await vm.SaveWordAsync(new VocabularyEntry { Word = "better", Translation = "лучше", SourceLanguage = "en", TargetLanguage = "ru", Context = "Better not to get into the matter.", TranslatedContext = "Лучше не вмешиваться в это дело.", Notes = "Совет, а не сравнение", Tags = "Disco Elysium, выражения", Definition = "Used to give advice", Phonetic = "ˈbetər", PartOfSpeech = "наречие", Level = "A2", Synonyms = "it would be advisable to", Examples = "You had better wait.", Explanation = "В этой реплике герой советует воздержаться от действия.", DictionarySource = "Fixture dictionary", ScreenshotFile = "preserve.png", AudioFile = "preserve.wav", Reviews = 4, DueAt = DateTimeOffset.UtcNow.AddDays(2) });
        await vm.SaveWordAsync(new VocabularyEntry { Word = "旅人", Translation = "путешественник", SourceLanguage = "ja", TargetLanguage = "ru", Context = "旅人は道を歩く。", AddedAt = DateTimeOffset.UtcNow.AddDays(-10) });
        await Settle(); vm.SelectedWord = vm.Words.Single(w => w.Id == rich.Id); await Settle(); Save(main, Path.Combine(output, "vocabulary.png"));
        var list = Find<ListBox>(main, "vocabulary-list"); var search = Find<TextBox>(main, "vocabulary-search");
        Check(Descendants(main).OfType<TextBlock>().Any(t => t.Text == rich.Explanation && t.IsVisible), "Selected card previews saved explanation without opening the editor");
        search.Text = "Совет"; await Settle(); Check(list.Items.Count == 1 && ((VocabularyEntry)list.Items[0]).Id == rich.Id, "Search matches notes immediately");
        search.Text = "никаких совпадений"; await Settle(); Save(main, Path.Combine(output, "vocabulary-no-results.png")); Check(Find<Button>(main, "vocabulary-reset").IsVisible && vm.SelectedWord is null, "No matches shows reset filters and clears stale card actions");
        Click(main, "vocabulary-reset"); await Settle(); Find<ComboBox>(main, "vocabulary-language").SelectedItem = ((IEnumerable<LanguageChoice>)Find<ComboBox>(main, "vocabulary-language").ItemsSource).Single(l => l.Code == "ja"); await Settle();
        Check(list.Items.Count == 1 && ((VocabularyEntry)list.Items[0]).Word == "旅人", "Language filter selects the corresponding saved cards");
        Click(main, "vocabulary-reset"); Find<ComboBox>(main, "vocabulary-sort").SelectedIndex = 3; await Settle(); Check(list.Items.Count == 2, "This-week filter excludes older cards");
        Find<ComboBox>(main, "vocabulary-sort").SelectedIndex = 1; await Settle(); Check(((VocabularyEntry)list.Items[0]).Word == "better", "Alphabetical sorting orders the visible collection");
        vm.SelectedWord = vm.Words.Single(w => w.Id == rich.Id); editor = new WordEditorWindow(vm, vm.SelectedWord) { Owner = main }; editor.Show(); await Settle();
        Find<Expander>(editor, "word-editor-details-section").IsExpanded = true; await Settle(); Save(editor, Path.Combine(output, "word-edit-expanded.png"));
        editor.Width = 560; editor.Height = 480; await Settle(); Save(editor, Path.Combine(output, "word-edit-narrow.png"));
        Check(InWindow(Find<Button>(editor, "word-editor-save"), editor) && InWindow(Find<Button>(editor, "word-editor-cancel"), editor), "Expanded long editor retains save and cancel at its smallest size");
        Find<TextBox>(editor, "word-editor-translation").Text = "лучше бы"; Click(editor, "word-editor-save"); await WaitFor(() => !editor.IsVisible);
        var updated = (await db.GetWordsAsync()).Single(w => w.Id == rich.Id);
        Check(updated.Translation == "лучше бы" && updated.Synonyms == rich.Synonyms && updated.AudioFile == rich.AudioFile && updated.ScreenshotFile == rich.ScreenshotFile && updated.Reviews == rich.Reviews && updated.DueAt == rich.DueAt, "Editing preserves unedited dictionary, media and review data");
        editor = new WordEditorWindow(vm, updated) { Owner = main }; editor.Show(); await Settle(); Find<TextBox>(editor, "word-editor-word").Text = "traveler"; Click(editor, "word-editor-save");
        await WaitFor(() => Find<Button>(editor, "word-editor-save").IsEnabled);
        Check(editor.IsVisible && Find<TextBlock>(editor, "word-editor-feedback").Visibility == Visibility.Visible && (await db.GetWordsAsync()).Single(w => w.Id == rich.Id).Word == "better", "Duplicate rename stays open with feedback and preserves the saved card");
        Click(editor, "word-editor-cancel"); await Settle();
        editor = new WordEditorWindow(vm) { Owner = main }; editor.Show(); await Settle(); Find<TextBox>(editor, "word-editor-word").Text = "not saved"; Click(editor, "word-editor-cancel"); await Settle();
        Check((await db.GetWordsAsync()).Count == 3, "Cancelling a draft does not add a card");
        Click(main, "vocabulary-reset"); vm.SelectedWord = vm.Words.Single(w => w.Id == rich.Id); await Settle(); await vm.RefreshAsync(); await Settle(); Check(vm.SelectedWord?.Id == rich.Id, "Refreshing the collection retains the selected card");
        main.Width = 960; main.Height = 650; await Settle(); Save(main, Path.Combine(output, "vocabulary-narrow.png"));
        Check(InWindow(Find<Button>(main, "vocabulary-add"), main) && InWindow(Find<Button>(main, "vocabulary-edit"), main) && InWindow(Find<Button>(main, "vocabulary-tools"), main), "Small window keeps add, edit and import/export actions visible");
        GaameTranslate.App.App.ApplyTheme(true); await Settle(); Save(main, Path.Combine(output, "vocabulary-light.png"));
        editor = new WordEditorWindow(vm) { Owner = main }; editor.Show(); await Settle(); Save(editor, Path.Combine(output, "word-add-light.png"));
        Check(((SolidColorBrush)editor.Background).Color == Color.FromRgb(244, 246, 248), "Word editor inherits the real light theme"); editor.Close(); GaameTranslate.App.App.ApplyTheme(false);
        main.Close(); await vm.StopAsync(); return passed;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var item in Descendants(child)) yield return item; }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class NoKeys : ICredentialStore { public string? Read(string provider) => null; public void Write(string provider, string value) { } public void Delete(string provider) { } }
}
