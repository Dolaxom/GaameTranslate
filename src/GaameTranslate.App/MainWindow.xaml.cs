using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using Microsoft.Win32;

namespace GaameTranslate.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public bool AllowClose { get; set; }
    public MainWindow(MainViewModel model)
    {
        vm = model; InitializeComponent(); DataContext = vm;
        SettingsHost.Children.Add(new SettingsView(vm));
        VocabularyLanguage.ItemsSource = new List<LanguageChoice> { new("all", "All languages") }.Concat(Languages.Translation).ToArray(); VocabularyLanguage.SelectedIndex = 0;
        SourceInitialized += (_, _) => GaameTranslate.Windows.NativeMethods.StyleWindow(new WindowInteropHelper(this).Handle, !vm.Settings.LightTheme);
        Closing += OnClosing;
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await vm.GuardAsync(vm.RefreshAsync); ApplyVocabularyView(); };
        vm.PropertyChanged += OnModelChanged;
        Closed += (_, _) => { searchTimer.Stop(); vm.PropertyChanged -= OnModelChanged; };
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        if (vm.Settings.MinimizeToTray) { Hide(); vm.Status = "Running in the system tray. Your global shortcuts are active."; }
        else Dispatcher.BeginInvoke(() => ((App)System.Windows.Application.Current).ExitApplication());
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PageIndex) && vm.PageIndex == 5) { SettingsHost.Children.Clear(); SettingsHost.Children.Add(new SettingsView(vm)); }
    }
    private void WordSearch_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; searchTimer.Stop(); searchTimer.Start(); }
    private void HistorySearch_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; searchTimer.Stop(); searchTimer.Start(); }
    private void VocabularyFilter_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyVocabularyView(); }
    private void ApplyVocabularyView()
    {
        var view = CollectionViewSource.GetDefaultView(WordsList.ItemsSource); if (view is null) return;
        using (view.DeferRefresh())
        {
            var language = (VocabularyLanguage.SelectedItem as LanguageChoice)?.Code ?? "all"; var sort = VocabularySort.SelectedIndex;
            view.Filter = o => o is VocabularyEntry w && (language == "all" || Languages.Base(w.SourceLanguage) == language) && (sort != 3 || w.AddedAt >= DateTimeOffset.UtcNow.AddDays(-7));
            view.SortDescriptions.Clear(); view.SortDescriptions.Add(sort switch { 1 => new(nameof(VocabularyEntry.Word), ListSortDirection.Ascending), 2 => new(nameof(VocabularyEntry.DueAt), ListSortDirection.Ascending), _ => new(nameof(VocabularyEntry.AddedAt), ListSortDirection.Descending) });
        }
    }
    private void AddWord_Click(object sender, RoutedEventArgs e) => Dialogs.EditWord(this, vm);
    private void EditWord_Click(object sender, RoutedEventArgs e) { if (vm.SelectedWord is { } word) Dialogs.EditWord(this, vm, word); else vm.Status = "Select a word to edit."; }
    private async void Import_Click(object sender, RoutedEventArgs e) => await vm.GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv", Title = "Import vocabulary" }; if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024) throw new UserFacingException("Choose a CSV smaller than 20 MB.");
        var entries = VocabularyCsv.Import(await File.ReadAllTextAsync(dialog.FileName));
        await vm.ImportWordsAsync(entries);
    });
    private async void Export_Click(object sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void Anki_Click(object sender, RoutedEventArgs e) => await ExportAsync(true);
    private async Task ExportAsync(bool anki) => await vm.GuardAsync(async () =>
    {
        var dialog = new SaveFileDialog { Filter = anki ? "Anki text (*.txt)|*.txt" : "CSV files (*.csv)|*.csv", FileName = anki ? "GaameTranslate-Anki.txt" : "GaameTranslate-vocabulary.csv" }; if (dialog.ShowDialog(this) != true) return;
        var entries = WordsList.Items.Cast<VocabularyEntry>().ToArray();
        await File.WriteAllTextAsync(dialog.FileName, anki ? VocabularyCsv.ExportAnki(entries) : VocabularyCsv.Export(entries), new System.Text.UTF8Encoding(true)); vm.Status = "Vocabulary exported.";
    });
    private void Reopen_Click(object sender, RoutedEventArgs e) => vm.ReopenHistory();
    private void CopyHistory_Click(object sender, RoutedEventArgs e) { if (vm.SelectedHistory is { } history) vm.Copy(history.TranslatedText); }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e) { if (Dialogs.Confirm(this, "Clear translation history?", "This permanently removes all locally saved translations. Vocabulary is kept.")) await vm.GuardAsync(vm.ClearHistoryAsync); }
}
