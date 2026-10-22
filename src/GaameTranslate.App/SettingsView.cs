using System.Globalization;
using GaameTranslate.Core;
using GaameTranslate.Windows;

namespace GaameTranslate.App;

public sealed class SettingsView : UserControl
{
    private readonly MainViewModel vm;
    private readonly ComboBox provider, source, target, ocr, overlay, monitor;
    private readonly PasswordBox apiKey = new();
    private readonly TextBlock keyState = new();
    private readonly TextBox endpoint, azureRegion, context, fontSize, opacity, scale, historyLimit;
    private readonly CheckBox removeKey, clickThrough, grayscale, contrast, threshold, denoise, history, tray, minimized, light;
    private readonly Dictionary<string, TextBox> shortcuts = new();
    public SettingsView(MainViewModel model)
    {
        vm = model; var s = vm.Settings;
        var root = new StackPanel(); Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "Make it yours.", FontSize = 30, FontWeight = FontWeights.SemiBold }); root.Children.Add(Muted("Good defaults. Just the controls you need.", new Thickness(0, 8, 0, 24)));
        var translation = Section(root, "Translation", "Only recognized text is sent to your selected service. Images stay on your PC.");
        provider = Choice(translation, "Provider", new[] { "DeepL Free", "DeepL Pro", "Azure", "LibreTranslate" }, s.Provider);
        source = LanguageCombo(translation, "Source language", vm.SourceLanguages, s.SourceLanguage); target = LanguageCombo(translation, "Target language", vm.TargetLanguages, s.TargetLanguage);
        var swap = new Button { Content = "Swap languages", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 14) };
        swap.Click += (_, _) => { if ((string?)source.SelectedValue == "auto") { vm.Status = "Choose an explicit source language before swapping."; return; } var from = source.SelectedValue; source.SelectedValue = target.SelectedValue; target.SelectedValue = from; }; translation.Children.Add(swap);
        Label(translation, "API key · leave empty to keep the saved key"); translation.Children.Add(apiKey); keyState.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); keyState.Margin = new(0, 6, 0, 8); translation.Children.Add(keyState);
        removeKey = Check(translation, "Delete the saved key for this provider", false);
        endpoint = Text(translation, "LibreTranslate base address", s.LibreEndpoint); azureRegion = Text(translation, "Azure resource region (required for regional resources)", s.AzureRegion);
        context = Text(translation, "Context hint · DeepL only (for example: fantasy RPG dialogue)", s.ContextHint);
        provider.SelectionChanged += (_, _) => UpdateProvider(); UpdateProvider();
        var ocrPanel = Section(root, "Capture & OCR", vm.OcrSummary);
        ocr = LanguageCombo(ocrPanel, "Installed recognition language · choose Japanese explicitly for Japanese games", vm.OcrLanguages, s.OcrLanguage);
        var displays = new List<LanguageChoice> { new("-1", "Monitor under the cursor") }; displays.AddRange(DesktopCapture.Displays.Select(d => new LanguageChoice(d.Index.ToString(CultureInfo.InvariantCulture), d.ToString())));
        monitor = LanguageCombo(ocrPanel, "Fullscreen capture monitor", displays, s.MonitorIndex.ToString(CultureInfo.InvariantCulture));
        scale = Text(ocrPanel, "Image scale · 0.5–3 (1.5 helps small text)", s.Preprocessing.Scale.ToString(CultureInfo.InvariantCulture));
        grayscale = Check(ocrPanel, "Grayscale", s.Preprocessing.Grayscale); contrast = Check(ocrPanel, "Enhance contrast", s.Preprocessing.EnhanceContrast); threshold = Check(ocrPanel, "Threshold · best for plain high-contrast text", s.Preprocessing.Threshold); denoise = Check(ocrPanel, "Smooth image noise · may soften fine strokes", s.Preprocessing.Denoise);
        ocrPanel.Children.Add(Muted("Windows profile language selects an installed recognizer; it does not detect image language. Install OCR packs through Windows language options and restart this app."));
        var overlayPanel = Section(root, "Overlay", "Use the interaction shortcut to switch between word selection and clicking through to your game.");
        overlay = Choice(overlayPanel, "Display mode", Enum.GetValues<OverlayMode>(), s.OverlayMode); fontSize = Text(overlayPanel, "Text size · 12–40", s.OverlayFontSize.ToString(CultureInfo.InvariantCulture)); opacity = Text(overlayPanel, "Background opacity · 0.3–1", s.OverlayOpacity.ToString(CultureInfo.InvariantCulture)); clickThrough = Check(overlayPanel, "Start overlays in click-through mode", s.ClickThrough);
        var keys = Section(root, "Keyboard shortcuts", "Click a field and press the desired combination. Include Ctrl, Alt, Shift or Win. Conflicts are reported after saving.");
        foreach (var binding in GlobalHotkeys.Bindings(s.Hotkeys))
        {
            var box = Text(keys, binding.Action switch { "ToggleOverlay" => "Show / hide overlay", "Interaction" => "Toggle interaction", _ => binding.Action }, binding.Shortcut); shortcuts[binding.Action] = box;
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                if (key == System.Windows.Input.Key.Tab) return;
                e.Handled = true; var modifiers = System.Windows.Input.Keyboard.Modifiers;
                if (modifiers == System.Windows.Input.ModifierKeys.None) return;
                if (!((key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z) || (key >= System.Windows.Input.Key.F1 && key <= System.Windows.Input.Key.F24) || (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9))) return;
                var parts = new List<string>(); if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl"); if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt"); if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift"); if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9 ? ((int)key - (int)System.Windows.Input.Key.D0).ToString(CultureInfo.InvariantCulture) : key.ToString()); box.Text = string.Join('+', parts);
            };
        }
        var general = Section(root, "General & privacy", "No analytics, no screenshot history, no background monitoring. Vocabulary and history are local, unencrypted SQLite data.");
        history = Check(general, "Save translation history on this PC", s.SaveHistory); historyLimit = Text(general, "Maximum history entries · 0–10,000", s.HistoryLimit.ToString(CultureInfo.InvariantCulture));
        tray = Check(general, "Keep running in the tray when the window closes", s.MinimizeToTray); minimized = Check(general, "Start in the system tray", s.StartMinimized); light = Check(general, "Use light appearance", s.LightTheme);
        var clear = new Button { Content = "Delete all vocabulary…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 4) }; clear.Click += async (_, _) => { if (Dialogs.Confirm(Window.GetWindow(this), "Delete your vocabulary?", "This permanently removes every saved word and its review schedule.")) await vm.GuardAsync(vm.ClearWordsAsync); }; general.Children.Add(clear);
        var save = new Button { Content = "Save settings", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 20) }; save.Click += async (_, _) => await SaveAsync(); root.Children.Add(save);
    }
    private void UpdateProvider()
    {
        var name = (string?)provider.SelectedItem ?? "DeepL Free";
        keyState.Text = vm.HasKey(name) ? "A key is stored securely for this provider." : name == "LibreTranslate" ? "A local service may not require a key." : "No key saved yet. Add an API key to enable translation.";
        apiKey.Clear();
    }
    private async Task SaveAsync() => await vm.GuardAsync(async () =>
    {
        static double Number(TextBox box, string label) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new UserFacingException($"Enter a valid number for {label}, using a dot as the decimal separator.");
        if (!int.TryParse(historyLimit.Text, out var limit)) throw new UserFacingException("History limit must be a whole number.");
        var next = vm.Settings with
        {
            Provider = (string)provider.SelectedItem,
            SourceLanguage = (string)source.SelectedValue,
            TargetLanguage = (string)target.SelectedValue,
            OcrLanguage = (string)ocr.SelectedValue,
            LibreEndpoint = endpoint.Text.Trim(),
            AzureRegion = azureRegion.Text.Trim(),
            ContextHint = context.Text.Trim(),
            OverlayMode = (OverlayMode)overlay.SelectedItem,
            MonitorIndex = int.Parse((string)monitor.SelectedValue, CultureInfo.InvariantCulture),
            OverlayFontSize = Number(fontSize, "text size"),
            OverlayOpacity = Number(opacity, "opacity"),
            ClickThrough = clickThrough.IsChecked == true,
            SaveHistory = history.IsChecked == true,
            HistoryLimit = limit,
            MinimizeToTray = tray.IsChecked == true,
            StartMinimized = minimized.IsChecked == true,
            LightTheme = light.IsChecked == true,
            Preprocessing = new() { Scale = Number(scale, "image scale"), Grayscale = grayscale.IsChecked == true, EnhanceContrast = contrast.IsChecked == true, Threshold = threshold.IsChecked == true, Denoise = denoise.IsChecked == true },
            Hotkeys = new() { Region = shortcuts["Region"].Text, Fullscreen = shortcuts["Fullscreen"].Text, Repeat = shortcuts["Repeat"].Text, ToggleOverlay = shortcuts["ToggleOverlay"].Text, Vocabulary = shortcuts["Vocabulary"].Text, Interaction = shortcuts["Interaction"].Text }
        };
        await vm.SaveSettingsAsync(next, apiKey.Password, removeKey.IsChecked == true); removeKey.IsChecked = false; UpdateProvider();
    });
    internal static TextBlock Muted(string text, Thickness? margin = null) { var block = new TextBlock { Text = text, Margin = margin ?? new Thickness(0, 0, 0, 12) }; block.SetResourceReference(FrameworkElement.StyleProperty, "Muted"); return block; }
    internal static StackPanel Section(Panel root, string title, string description)
    {
        var panel = new StackPanel(); var border = new Border { Child = panel, Margin = new(0, 0, 0, 20) }; border.SetResourceReference(FrameworkElement.StyleProperty, "Card"); root.Children.Add(border);
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 10) }); panel.Children.Add(Muted(description)); return panel;
    }
    internal static void Label(Panel parent, string text) => parent.Children.Add(new TextBlock { Text = text, Margin = new(0, 6, 0, 8), FontSize = 12 });
    internal static TextBox Text(Panel parent, string label, string value) { Label(parent, label); var box = new TextBox { Text = value, Margin = new(0, 0, 0, 12) }; System.Windows.Automation.AutomationProperties.SetName(box, label); parent.Children.Add(box); return box; }
    internal static CheckBox Check(Panel parent, string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value }; parent.Children.Add(box); return box; }
    private static ComboBox Choice<T>(Panel parent, string label, IEnumerable<T> choices, T selected)
    { Label(parent, label); var box = new ComboBox { ItemsSource = choices, SelectedItem = selected, Margin = new(0, 0, 0, 14) }; System.Windows.Automation.AutomationProperties.SetName(box, label); parent.Children.Add(box); return box; }
    private static ComboBox LanguageCombo(Panel parent, string label, IEnumerable<LanguageChoice> choices, string selected)
    { var box = Choice(parent, label, choices, choices.FirstOrDefault(c => c.Code == selected) ?? choices.First()); box.SelectedValuePath = "Code"; box.SelectedValue = selected; return box; }
}
