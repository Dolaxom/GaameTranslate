using GaameTranslate.Core;

namespace GaameTranslate.App;

internal static class Dialogs
{
    public static Window Shell(Window? owner, string title, UIElement content, double width = 480)
    {
        var window = new Window { Title = title, Content = content, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 760, MinHeight = 160, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner };
        if (owner is { IsVisible: true }) window.Owner = owner;
        window.SourceInitialized += (_, _) => GaameTranslate.Windows.NativeMethods.StyleWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) window.Close(); };
        return window;
    }
    public static bool Confirm(Window? owner, string title, string text)
    {
        var root = new StackPanel { Margin = new(24) }; root.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 14) }); root.Children.Add(SettingsView.Muted(text));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) }; root.Children.Add(buttons);
        var cancel = new Button { Content = "Cancel", Margin = new(0, 0, 8, 0) }; var confirm = new Button { Content = "Delete", Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton") }; buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        var window = Shell(owner, title, root); cancel.Click += (_, _) => window.DialogResult = false; confirm.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }
    public static void EditWord(Window owner, MainViewModel vm, VocabularyEntry? entry = null)
    {
        var root = new StackPanel { Margin = new(24) };
        root.Children.Add(new TextBlock { Text = entry is null ? "Keep a new word" : "Word details", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
        var word = SettingsView.Text(root, "Word", entry?.Word ?? ""); var meaning = SettingsView.Text(root, "Translation", entry?.Translation ?? "");
        var context = SettingsView.Text(root, "Original sentence", entry?.Context ?? ""); var translated = SettingsView.Text(root, "Translated sentence", entry?.TranslatedContext ?? "");
        var tags = SettingsView.Text(root, "Tags", entry?.Tags ?? ""); var notes = SettingsView.Text(root, "Notes", entry?.Notes ?? "");
        var phonetic = SettingsView.Text(root, "Произношение", entry?.Phonetic ?? ""); var grammar = SettingsView.Text(root, "Часть речи", entry?.PartOfSpeech ?? ""); var definition = SettingsView.Text(root, "Определение", entry?.Definition ?? ""); var examples = SettingsView.Text(root, "Примеры", entry?.Examples ?? ""); var explanation = SettingsView.Text(root, "Объяснение", entry?.Explanation ?? "");
        root.Children.Add(SettingsView.Muted(entry is null ? $"{vm.Settings.SourceLanguage} → {vm.Settings.TargetLanguage}" : $"{entry.SourceLanguage} → {entry.TargetLanguage} · {entry.Encounters} encounters · added {entry.AddedAt.LocalDateTime:d}"));
        var feedback = SettingsView.Muted(""); root.Children.Add(feedback); var save = new Button { Content = "Save word", Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton") }; root.Children.Add(save);
        var window = Shell(owner, "Vocabulary", new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var next = (entry ?? new VocabularyEntry { SourceLanguage = vm.Settings.SourceLanguage, TargetLanguage = vm.Settings.TargetLanguage }) with { Word = word.Text.Trim(), Translation = meaning.Text.Trim(), Context = context.Text, TranslatedContext = translated.Text, Tags = tags.Text, Notes = notes.Text, Phonetic = phonetic.Text, PartOfSpeech = grammar.Text, Definition = definition.Text, Examples = examples.Text, Explanation = explanation.Text };
                if (entry is null) await vm.SaveWordAsync(next); else await vm.UpdateWordAsync(next); window.Close();
            }
            catch (Exception e) { feedback.Text = e is UserFacingException ? e.Message : "Could not save the word. Check available disk space and try again."; }
            finally { save.IsEnabled = true; }
        };
        window.ShowDialog();
    }
}
