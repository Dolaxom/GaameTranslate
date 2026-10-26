using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Microsoft.Win32;

namespace GaameTranslate.App;

public partial class VocabularyView : UserControl
{
    private readonly MainViewModel vm;
    private readonly ICollectionView words;
    private bool refreshQueued;
    public VocabularyView(MainViewModel model)
    {
        vm = model; InitializeComponent(); DataContext = vm;
        words = new CollectionViewSource { Source = vm.Words }.View;
        WordsList.ItemsSource = words;
        LanguageFilter.ItemsSource = new[] { new LanguageChoice("all", "Все языки") }.Concat(Languages.Translation).ToArray(); LanguageFilter.SelectedIndex = 0;
        vm.Words.CollectionChanged += Words_Changed; vm.PropertyChanged += Model_Changed; L.State.PropertyChanged += Language_Changed;
        Loaded += (_, _) => RefreshView();
        RefreshView();
    }
    public void Dispose() { vm.Words.CollectionChanged -= Words_Changed; vm.PropertyChanged -= Model_Changed; L.State.PropertyChanged -= Language_Changed; }
    private void Language_Changed(object? sender, PropertyChangedEventArgs e)
    {
        var confirming = DeleteConfirmation.Visibility == Visibility.Visible; RenderDetails();
        if (confirming) { DeleteConfirmation.Visibility = Visibility.Visible; SelectionActions.Visibility = Visibility.Collapsed; }
    }
    private Window OwnerWindow => Window.GetWindow(this);
    private void Model_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedWord)) { if (!ReferenceEquals(WordsList.SelectedItem, vm.SelectedWord)) WordsList.SelectedItem = vm.SelectedWord; RenderDetails(); }
    }
    private void Words_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (refreshQueued) return; refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () => { refreshQueued = false; RefreshView(); });
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (words is not null) RefreshView(); }
    private void RefreshView()
    {
        var previous = vm.SelectedWord?.Id; var search = Search.Text.Trim(); var language = (LanguageFilter.SelectedItem as LanguageChoice)?.Code ?? "all";
        using (words.DeferRefresh())
        {
            words.Filter = item => item is VocabularyEntry entry && (language == "all" || Languages.Base(entry.SourceLanguage) == language)
                && (Sort.SelectedIndex != 3 || entry.AddedAt >= DateTimeOffset.UtcNow.AddDays(-7))
                && (search.Length == 0 || new[] { entry.Word, entry.Translation, entry.Context, entry.TranslatedContext, entry.Notes, entry.Tags, entry.Definition }.Any(s => s.Contains(search, StringComparison.OrdinalIgnoreCase)));
            words.SortDescriptions.Clear(); words.SortDescriptions.Add(Sort.SelectedIndex switch { 1 => new(nameof(VocabularyEntry.Word), ListSortDirection.Ascending), 2 => new(nameof(VocabularyEntry.DueAt), ListSortDirection.Ascending), _ => new(nameof(VocabularyEntry.AddedAt), ListSortDirection.Descending) });
        }
        var visible = words.Cast<VocabularyEntry>().ToArray();
        WordsList.SelectedItem = visible.FirstOrDefault(w => w.Id == previous) ?? visible.FirstOrDefault();
        Ui.SetText(ResultCount, L.M("Карточек: {0}", visible.Length));
        EmptyCollection.Visibility = vm.WordCount == 0 && vm.Words.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CollectionLayout.Visibility = EmptyCollection.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        NoResults.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderDetails();
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) { vm.SelectedWord = WordsList.SelectedItem as VocabularyEntry; RenderDetails(); }
    private void RenderDetails()
    {
        if (Details is null) return; Details.Children.Clear(); DeleteConfirmation.Visibility = Visibility.Collapsed; SelectionActions.Visibility = Visibility.Visible;
        Edit.IsEnabled = Delete.IsEnabled = vm.SelectedWord is not null;
        if (vm.SelectedWord is not { } entry) { Details.Children.Add(LocalizedLabel("Выберите карточку", 22, true)); Details.Children.Add(LocalizedLabel("Её перевод, контекст и заметки появятся здесь.", 12)); return; }
        var language = LocalizedLabel(VocabularyLabelConverter.Pair(entry), 11); language.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); Details.Children.Add(language);
        Details.Children.Add(Label(entry.Word, 30, true));
        if (!string.IsNullOrWhiteSpace(entry.Phonetic + entry.PartOfSpeech + entry.Level)) Details.Children.Add(Muted(string.Join("  ·  ", new[] { entry.Phonetic, entry.PartOfSpeech, entry.Level }.Where(s => !string.IsNullOrWhiteSpace(s)))));
        var translation = Label(entry.Translation, 21); translation.Margin = new(0, 12, 0, 12); Details.Children.Add(translation);
        Section("В игре", entry.Context, entry.TranslatedContext); Section("Значение", entry.Definition); Section("Объяснение", entry.Explanation); Section("Примеры", entry.Examples); Section("Синонимы", entry.Synonyms); Section("Мои заметки", entry.Notes);
        if (!string.IsNullOrWhiteSpace(entry.Tags)) Section("Теги", entry.Tags);
        Details.Children.Add(LocalizedLabel(L.M("Добавлено {0:d} · Встреч: {1}", entry.AddedAt.LocalDateTime, entry.Encounters), 12));
        Details.Children.Add(LocalizedLabel(entry.DueAt <= DateTimeOffset.UtcNow ? new UiMessage("Готово к повторению") : L.M("Следующее повторение: {0:d}", entry.DueAt.LocalDateTime), 12));
        if (!string.IsNullOrWhiteSpace(entry.DictionarySource)) Details.Children.Add(Muted(entry.DictionarySource));
    }
    private void Section(string title, params string[] values)
    {
        if (values.All(string.IsNullOrWhiteSpace)) return;
        var heading = LocalizedLabel(title, 12, true); heading.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); heading.Margin = new(0, 18, 0, 7); Details.Children.Add(heading);
        foreach (var text in values.Where(s => !string.IsNullOrWhiteSpace(s))) { var block = Label(text); block.Margin = new(0, 0, 0, 7); Details.Children.Add(block); }
    }
    private static TextBlock Label(string text, double size = 14, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new(0, 3, 0, 4) };
    private static TextBlock Muted(string text) { var block = Label(text, 12); block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return block; }
    private static TextBlock LocalizedLabel(UiMessage message, double size = 14, bool bold = false) { var text = Label("", size, bold); Ui.SetText(text, message); return text; }
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { Search.Clear(); Search.Focus(); }
    private void Reset_Click(object sender, RoutedEventArgs e) { Search.Clear(); LanguageFilter.SelectedIndex = 0; Sort.SelectedIndex = 0; RefreshView(); }
    private void Add_Click(object sender, RoutedEventArgs e) => new WordEditorWindow(vm) { Owner = OwnerWindow }.ShowDialog();
    private void Edit_Click(object sender, RoutedEventArgs e) { if (vm.SelectedWord is { } entry) new WordEditorWindow(vm, entry) { Owner = OwnerWindow }.ShowDialog(); }
    private void List_DoubleClick(object sender, MouseButtonEventArgs e) { if (ItemsControl.ContainerFromElement(WordsList, e.OriginalSource as DependencyObject) is ListBoxItem) Edit_Click(sender, e); }
    private void Delete_Click(object sender, RoutedEventArgs e) { SelectionActions.Visibility = Visibility.Collapsed; DeleteConfirmation.Visibility = Visibility.Visible; }
    private void CancelDelete_Click(object sender, RoutedEventArgs e) { DeleteConfirmation.Visibility = Visibility.Collapsed; SelectionActions.Visibility = Visibility.Visible; }
    private async void ConfirmDelete_Click(object sender, RoutedEventArgs e) => await vm.DeleteWordCommand.ExecuteAsync(null);
    private async void Review_Click(object sender, RoutedEventArgs e) { vm.PageIndex = 3; await vm.StartReviewCommand.ExecuteAsync(null); }
    private void Tools_Click(object sender, RoutedEventArgs e) { var button = (Button)sender; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; }
    private async void Import_Click(object sender, RoutedEventArgs e) => await vm.GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv", Title = L.Get("Импорт словаря") }; if (dialog.ShowDialog(OwnerWindow) != true) return;
        if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024) throw new UserFacingException("Выберите CSV-файл меньше 20 МБ.");
        await vm.ImportWordsAsync(VocabularyCsv.Import(await File.ReadAllTextAsync(dialog.FileName)));
    });
    private async void Export_Click(object sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void Anki_Click(object sender, RoutedEventArgs e) => await ExportAsync(true);
    private async void SyncAnki_Click(object sender, RoutedEventArgs e) => await vm.GuardAsync(vm.SyncAnkiAsync);
    private async Task ExportAsync(bool anki) => await vm.GuardAsync(async () =>
    {
        var entries = words.Cast<VocabularyEntry>().ToArray(); if (entries.Length == 0) { vm.Status = "Нет карточек для экспорта. Сбросьте фильтры или добавьте слово."; return; }
        var dialog = new SaveFileDialog { Title = L.Format("Экспорт карточек: {0}", entries.Length), Filter = anki ? L.Get("Anki (*.apkg)|*.apkg|Anki text (*.txt)|*.txt") : "CSV (*.csv)|*.csv", FileName = anki ? "GaameTranslate.apkg" : "GaameTranslate-vocabulary.csv" }; if (dialog.ShowDialog(OwnerWindow) != true) return;
        if (anki && Path.GetExtension(dialog.FileName).Equals(".apkg", StringComparison.OrdinalIgnoreCase)) await Task.Run(() => AnkiPackageExporter.ExportAsync(dialog.FileName, entries, vm.Media));
        else await File.WriteAllTextAsync(dialog.FileName, anki ? VocabularyCsv.ExportAnki(entries) : VocabularyCsv.Export(entries), new System.Text.UTF8Encoding(true)); vm.Status = L.M("Экспортировано карточек: {0}.", entries.Length);
    });
}

public sealed class VocabularyLabelConverter : IValueConverter
{
    public static UiMessage Pair(VocabularyEntry entry) => L.M("{0} → {1}", Language(entry.SourceLanguage), Language(entry.TargetLanguage));
    private static UiMessage Language(string code) => new(Languages.Translation.FirstOrDefault(l => l.Code == Languages.Base(code))?.Name ?? (code == "auto" ? "Авто" : code));
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not VocabularyEntry entry ? "" : (parameter as string) switch
    {
        "language" => L.M("{0} → {1}", entry.SourceLanguage.ToUpperInvariant(), entry.TargetLanguage.ToUpperInvariant()),
        _ => !string.IsNullOrWhiteSpace(entry.Context) ? L.M("{0}", entry.Context) : !string.IsNullOrWhiteSpace(entry.Tags) ? L.M("{0}", entry.Tags) : entry.DueAt <= DateTimeOffset.UtcNow ? new UiMessage("Готово к повторению") : L.M("Повторить {0:d}", entry.DueAt.LocalDateTime)
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
