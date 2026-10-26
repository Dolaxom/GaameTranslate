using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GaameTranslate.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly SettingsView settingsView;
    private readonly VocabularyView vocabularyView;
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public bool AllowClose { get; set; }
    public MainWindow(MainViewModel model)
    {
        vm = model; InitializeComponent(); DataContext = vm;
        settingsView = new SettingsView(vm); SettingsHost.Children.Add(settingsView);
        vocabularyView = new VocabularyView(vm); VocabularyHost.Children.Add(vocabularyView);
        SourceInitialized += (_, _) => GaameTranslate.Windows.NativeMethods.StyleWindow(new WindowInteropHelper(this).Handle, !vm.Settings.LightTheme);
        Closing += OnClosing;
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await vm.GuardAsync(vm.RefreshAsync); };
        vm.PropertyChanged += OnModelChanged;
        Closed += (_, _) => { searchTimer.Stop(); vm.PropertyChanged -= OnModelChanged; vocabularyView.Dispose(); };
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
        if (e.PropertyName == nameof(MainViewModel.PageIndex) && vm.PageIndex == 5) settingsView.RefreshIfClean();
    }
    private void HistorySearch_Changed(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; searchTimer.Stop(); searchTimer.Start(); }
    private void Reopen_Click(object sender, RoutedEventArgs e) => vm.ReopenHistory();
    private void CopyHistory_Click(object sender, RoutedEventArgs e) { if (vm.SelectedHistory is { } history) vm.Copy(history.TranslatedText); }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e) { if (Dialogs.Confirm(this, "Clear translation history?", "This permanently removes all locally saved translations. Vocabulary is kept.")) await vm.GuardAsync(vm.ClearHistoryAsync); }
}
