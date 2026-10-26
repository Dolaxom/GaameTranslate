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
        new WordEditorWindow(vm, entry) { Owner = owner }.ShowDialog();
    }
}
