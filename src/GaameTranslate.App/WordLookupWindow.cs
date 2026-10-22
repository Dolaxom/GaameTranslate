using GaameTranslate.Core;

namespace GaameTranslate.App;

public sealed class WordLookupWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    public WordLookupWindow(MainViewModel vm, string word, TranslationSession session, TranslatedBlock block)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "GaameTranslate · dictionary"; Width = 440; SizeToContent = SizeToContent.Height; MaxHeight = 640; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new(24) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "A word to keep.", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        var selection = SettingsView.Text(root, "Word or phrase · edit to select a Japanese / Chinese substring", word);
        var originalContext = SettingsView.Muted(block.Original.Text); originalContext.MaxHeight = 100; root.Children.Add(originalContext);
        var translated = new TextBlock { FontSize = 21, Margin = new(0, 12, 0, 18), Text = "Looking up…" }; root.Children.Add(translated);
        var note = SettingsView.Muted(""); root.Children.Add(note);
        var buttons = new WrapPanel(); root.Children.Add(buttons);
        var lookup = new Button { Content = "Translate selection", Margin = new(0, 0, 8, 8) }; var save = new Button { Content = "Add to vocabulary", IsEnabled = false, Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton"), Margin = new(0, 0, 0, 8) }; buttons.Children.Add(lookup); buttons.Children.Add(save);
        TranslationItem? result = null; var recognizedWord = "";
        async Task TranslateAsync()
        {
            if (string.IsNullOrWhiteSpace(selection.Text)) { note.Text = "Select a word or phrase first."; return; }
            lookup.IsEnabled = false; save.IsEnabled = false; note.Text = ""; translated.Text = "Looking up…";
            try { recognizedWord = selection.Text.Trim(); result = await vm.LookupAsync(recognizedWord, session.SourceLanguage, session.TargetLanguage, block.Original.Text, lifetime.Token); translated.Text = result.Text; save.IsEnabled = true; }
            catch (OperationCanceledException) { }
            catch (Exception e) { translated.Text = "Could not look up this word."; note.Text = e is UserFacingException ? e.Message : "Check your provider and connection, then try again."; }
            finally { lookup.IsEnabled = true; }
        }
        lookup.Click += async (_, _) => await TranslateAsync(); selection.TextChanged += (_, _) => save.IsEnabled = false;
        save.Click += async (_, _) =>
        {
            if (result is null) return; save.IsEnabled = false;
            try { var entry = await vm.SaveWordAsync(new() { Word = recognizedWord, Translation = result.Text, SourceLanguage = result.SourceLanguage, TargetLanguage = session.TargetLanguage, Context = block.Original.Text, TranslatedContext = block.Translation }); note.Text = entry.Encounters > 1 ? "Already saved · encounter count updated." : "Saved to your vocabulary."; }
            catch (Exception e) { note.Text = e is UserFacingException ? e.Message : "Could not save the word. Check available disk space."; save.IsEnabled = true; }
        };
        Loaded += async (_, _) => await TranslateAsync(); Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        SourceInitialized += (_, _) => GaameTranslate.Windows.NativeMethods.StyleWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle, !vm.Settings.LightTheme);
    }
}
