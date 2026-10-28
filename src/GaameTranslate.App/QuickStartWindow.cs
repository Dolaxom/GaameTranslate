using System.Windows.Automation;
using GaameTranslate.Core;

namespace GaameTranslate.App;

public sealed class QuickStartWindow : Window
{
    public QuickStartWindow(MainViewModel vm)
    {
        Ui.Set(this, TitleProperty, "A quick start, at your pace"); Width = 720; MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 32); Height = Math.Min(680, MaxHeight); MinWidth = 520; MinHeight = Math.Min(480, MaxHeight);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new(24) };
        var actions = new WrapPanel { Margin = new(0, 18, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        Button Action(string key, string id, Action click)
        {
            var button = Ui.Localize(new Button { Content = key, Margin = new(0, 0, 8, 8), Padding = new(12, 8, 12, 8) });
            AutomationProperties.SetAutomationId(button, id); button.Click += (_, _) => { click(); Close(); }; actions.Children.Add(button); return button;
        }
        Action("Try a sample", "guide-sample", () => vm.TrySampleCommand.Execute(null));
        Action("Set up translation", "guide-setup", () => vm.OpenSettingsCommand.Execute(null));
        Action("Shortcut settings", "guide-shortcuts", () => vm.OpenSettingsTab("shortcuts"));
        Action("Got it", "guide-close", () => { });
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var title = Ui.Text("Understand the game. Keep the words."); title.FontSize = 26; title.FontWeight = FontWeights.SemiBold; title.TextWrapping = TextWrapping.Wrap; content.Children.Add(title);
        void Section(string heading, UiMessage body)
        {
            var panel = new StackPanel(); var label = Ui.Text(heading); label.FontSize = 17; label.FontWeight = FontWeights.SemiBold; panel.Children.Add(label);
            panel.Children.Add(SettingsView.Muted(body, new(0, 8, 0, 0)));
            var card = new Border { Child = panel, Padding = new(16), Margin = new(0, 16, 0, 0), CornerRadius = new(10) }; card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); content.Children.Add(card);
        }
        Section("Your first minute", L.M("1. Use {0} for the screen or {1} for a word under your cursor.\n2. Save a useful word with its sentence.\n3. Open My vocabulary or Review when you want to practise. Escape closes a translation window.", vm.Settings.Hotkeys.ScreenTranslation, vm.Settings.Hotkeys.WordLookup));
        Section("Start with what you have", "The built-in English–Russian dictionary works offline, with no account or download. It gives word-by-word hints. It cannot understand a sentence or choose the right meaning every time.");
        Section("Connect a translator for better sentences", "Open Settings → Translation, choose a service, follow its three steps, check the connection, and save. A local service runs on your PC; a cloud service receives the text you ask it to translate and may require paid API access.");
        Section("An assistant is optional", "Settings → AI assistant selects the AI assistant for contextual explanations. It is separate from the main translator. You can translate words and save cards without it.");
        Section("If a service stops working", "For English → Russian, a failed or slow service falls back to the built-in dictionary. The app labels that lower-quality result and retries the service later. Unknown words remain in the original; other language pairs need a working service.");
        Content = root;
    }
}
