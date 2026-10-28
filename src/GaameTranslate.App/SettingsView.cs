using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using GaameTranslate.Core;
using GaameTranslate.Windows;

namespace GaameTranslate.App;

public sealed partial class SettingsView : UserControl
{
    private readonly MainViewModel vm;
    private readonly Dictionary<string, TextBox> fields = new();
    private readonly Dictionary<string, ComboBox> choices = new();
    private readonly Dictionary<string, CheckBox> flags = new();
    private readonly Dictionary<string, Slider> sliders = new();
    private readonly Dictionary<string, StackPanel> pages = new();
    private readonly Dictionary<string, Button> tabs = new();
    private readonly Dictionary<FrameworkElement, string> owners = new();
    private readonly Dictionary<string, string> keyDrafts = new();
    private readonly HashSet<string> removedKeys = new();
    private readonly List<ConnectionEditor> connections = new();
    private Button save = null!, discard = null!;
    private TextBlock feedback = null!;
    private ScrollViewer scroll = null!;
    private string currentPage = "translation", buildingPage = "translation";
    private bool ready, saving;
    private int runningChecks;
    public bool HasUnsavedChanges { get; private set; }

    public SettingsView(MainViewModel model)
    {
        vm = model;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/GaameTranslate;component/SettingsTheme.xaml", UriKind.Relative) });
        Build();
        Unloaded += (_, _) => { vm.SetShortcutRecording(false); foreach (var connection in connections) connection.Cancel(); };
    }

    public void RefreshIfClean() { if (!HasUnsavedChanges && runningChecks == 0 && !saving) Build(); }
    public void OpenTab(string id) { if (pages.ContainsKey(id)) SelectPage(id); }

    private void Build()
    {
        ready = false;
        foreach (var connection in connections) connection.Cancel();
        fields.Clear(); choices.Clear(); flags.Clear(); sliders.Clear(); pages.Clear(); tabs.Clear(); owners.Clear(); connections.Clear(); keyDrafts.Clear(); removedKeys.Clear();
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 14) };
        heading.Children.Add(Ui.Localize(new TextBlock { Text = "Настройки", FontSize = 30, FontWeight = FontWeights.SemiBold }));
        heading.Children.Add(Muted("Всё для удобного перевода — от подключения до любимых сочетаний клавиш.", new(0, 6, 0, 0)));
        root.Children.Add(heading);
        var navigation = new WrapPanel { Margin = new(0, 0, 0, 10) }; Grid.SetRow(navigation, 1); root.Children.Add(navigation);
        var content = new Grid();
        scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 12, 0) };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        foreach (var (id, title) in new[] { ("translation", "Перевод"), ("screen", "Экран"), ("assistant", "AI assistant"), ("dictionary", "Словарь"), ("shortcuts", "Клавиши"), ("games", "Игры"), ("app", "Приложение") })
        {
            var button = Ui.Localize(new Button { Content = title, Style = (Style)FindResource("SettingsTab"), Tag = false }); Identify(button, "settings-tab-" + id, title);
            button.Click += (_, _) => SelectPage(id); navigation.Children.Add(button); tabs[id] = button;
            var page = new StackPanel { Visibility = Visibility.Collapsed }; pages[id] = page; content.Children.Add(page);
        }
        BuildTranslation(); BuildScreen(); BuildAssistant(); BuildDictionary(); BuildShortcuts(); BuildGames(); BuildApplication();
        var footer = new Grid { Margin = new(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        feedback = Muted("Все изменения сохранены", new(0, 0, 18, 0)); feedback.FontSize = 12; feedback.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite); Identify(feedback, "settings-feedback", "Состояние настроек"); footer.Children.Add(feedback);
        discard = Ui.Localize(new Button { Content = "Отменить", Margin = new(0, 0, 10, 0), ToolTip = "Вернуть сохранённые настройки" }); Identify(discard, "settings-discard", "Отменить изменения");
        discard.Click += (_, _) => Build(); Grid.SetColumn(discard, 1); footer.Children.Add(discard);
        save = Ui.Localize(new Button { Content = "Сохранить", Style = (Style)FindResource("PrimaryButton"), MinWidth = 128 }); Identify(save, "settings-save", "Сохранить настройки");
        save.Click += async (_, _) => await SaveAsync(); Grid.SetColumn(save, 2); footer.Children.Add(save);
        var footerBorder = new Border { Child = footer, BorderThickness = new(0, 1, 0, 0), Padding = new(0, 0, 0, 2) };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); Grid.SetRow(footerBorder, 3); root.Children.Add(footerBorder);
        ready = true; HasUnsavedChanges = false; SelectPage(currentPage); UpdateActions();
    }

    private void SelectPage(string id)
    {
        currentPage = id;
        foreach (var item in pages) item.Value.Visibility = item.Key == id ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in tabs) item.Value.Tag = item.Key == id ? "selected" : "";
        scroll.ScrollToTop();
    }

    private void Changed()
    {
        if (!ready) return;
        try { HasUnsavedChanges = ReadSettings() != vm.Settings || keyDrafts.Values.Any(k => k.Length > 0) || removedKeys.Count > 0; }
        catch (UserFacingException) { HasUnsavedChanges = true; }
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Ui.SetText(feedback, HasUnsavedChanges ? "Есть несохранённые изменения" : "Все изменения сохранены");
        UpdateActions();
        foreach (var connection in connections) connection.InvalidateResult();
    }

    private void UpdateActions()
    {
        if (save is null) return;
        save.IsEnabled = HasUnsavedChanges && !saving && runningChecks == 0;
        discard.IsEnabled = HasUnsavedChanges && !saving && runningChecks == 0;
        foreach (var page in pages.Values) page.IsEnabled = !saving;
        foreach (var tab in tabs.Values) tab.IsEnabled = !saving;
    }

    private async Task<bool> SaveAsync()
    {
        if (saving || runningChecks > 0) return false;
        try
        {
            var next = ReadSettings(true); ValidateDraft(next);
            saving = true; UpdateActions(); Ui.SetContent(save, "Сохранение…");
            await vm.SaveSettingsAsync(next, null, false);
            foreach (var name in keyDrafts.Keys.Concat(removedKeys).Distinct().ToArray())
                vm.UpdateProviderCredential(name, keyDrafts.GetValueOrDefault(name, ""), removedKeys.Contains(name));
            keyDrafts.Clear(); removedKeys.Clear();
            foreach (var connection in connections) { connection.RefreshKey(); connection.MarkSaved(); }
            HasUnsavedChanges = false; feedback.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            Ui.SetText(feedback, vm.Status.Key == "Настройки сохранены." ? new UiMessage("Сохранено · настройки уже применены") : vm.Status);
            if (vm.Status.Key.StartsWith("Сочетания заняты", StringComparison.Ordinal)) feedback.SetResourceReference(TextBlock.ForegroundProperty, "SettingsErrorBrush");
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
        finally { saving = false; Ui.SetContent(save, "Сохранить"); UpdateActions(); }
    }

    private void ShowError(Exception error)
    {
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "SettingsErrorBrush");
        Ui.Error(feedback, error, "Не удалось выполнить действие. Проверьте подключение и доступ к папке данных.");
    }

    private void Invalid(FrameworkElement control, string message)
    {
        if (ready)
        {
            if (owners.TryGetValue(control, out var page)) SelectPage(page);
            for (DependencyObject? parent = control; parent is not null; parent = LogicalTreeHelper.GetParent(parent))
                if (parent is Expander expander) expander.IsExpanded = true;
            Dispatcher.BeginInvoke(() => { control.BringIntoView(); control.Focus(); });
        }
        throw new UserFacingException(message);
    }

    private void ValidateDraft(AppSettings next)
    {
        var used = new HashSet<(uint, uint)>();
        foreach (var binding in GlobalHotkeys.Bindings(next.Hotkeys))
        {
            try { if (!used.Add(GlobalHotkeys.Parse(binding.Shortcut))) Invalid(fields["key-" + binding.Action], "Это сочетание уже используется. Выберите другое."); }
            catch (UserFacingException) { Invalid(fields["key-" + binding.Action], "Каждому действию нужно своё сочетание: Ctrl, Alt, Shift или Win + буква, цифра или F1–F24."); }
        }
        if (next.ContextHint.Length > 1000) Invalid(fields["context"], "Описание игры должно быть не длиннее 1000 символов.");
        if (next.HistoryLimit is < 0 or > 10000) Invalid(fields["historyLimit"], "В истории можно хранить от 0 до 10 000 переводов.");
        foreach (var (name, id) in new[] { ("LibreTranslate", "libre"), ("Ollama", "ollama"), ("OpenAI compatible", "compatible") })
            if (next.Provider == name || next.AssistantProvider == name)
                try { AppSettings.ValidateEndpoint(fields[id].Text.Trim()); } catch (UserFacingException) { Invalid(connections.First(c => c.Name == name).Input(id), "Укажите полный адрес сервера: HTTPS или HTTP для localhost."); }
        try { if (!AppSettings.ValidateEndpoint(next.AnkiEndpoint).IsLoopback) Invalid(fields["ankiEndpoint"], "AnkiConnect должен работать на этом компьютере."); }
        catch (UserFacingException) { Invalid(fields["ankiEndpoint"], "Адрес AnkiConnect должен быть локальным, например http://127.0.0.1:8765."); }
        if (string.IsNullOrWhiteSpace(next.AnkiDeck) || next.AnkiDeck.Length > 120) Invalid(fields["ankiDeck"], "Введите название колоды длиной до 120 символов.");
        next.Validate();
    }

    private AppSettings ReadConnectionSettings()
    {
        string T(string id) => fields[id].Text.Trim();
        string C(string id) => (string)choices[id].SelectedValue;
        return vm.Settings with
        {
            Provider = C("provider"), SourceLanguage = C("source"), TargetLanguage = C("target"), LibreEndpoint = T("libre"), AzureRegion = T("azureRegion"), ContextHint = T("context"),
            YandexFolderId = T("folder"), YandexIamToken = flags["iam"].IsChecked == true, YandexModel = T("yandexModel"), GigaChatModel = T("gigaModel"), GigaChatScope = T("gigaScope"),
            OllamaEndpoint = T("ollama"), OllamaModel = T("ollamaModel"), CompatibleEndpoint = T("compatible"), CompatibleModel = T("compatibleModel"),
            AssistantProvider = C("assistant"), AssistantModel = T("assistantModel"), TranslationTimeoutSeconds = (int)sliders["timeout"].Value
        };
    }

    private AppSettings ReadSettings(bool focusErrors = false)
    {
        string T(string id) => fields[id].Text.Trim();
        string C(string id) => (string)choices[id].SelectedValue;
        bool F(string id) => flags[id].IsChecked == true;
        double V(string id) => sliders[id].Value;
        if (!int.TryParse(T("historyLimit"), out var limit))
        {
            if (focusErrors) Invalid(fields["historyLimit"], "Лимит истории должен быть целым числом.");
            throw new UserFacingException("Лимит истории должен быть целым числом.");
        }
        return ReadConnectionSettings() with
        {
            InterfaceLanguage = C("interfaceLanguage"), ShowStartupTips = F("startupTips"),
            OcrEngine = C("engine"), OcrLanguage = C("ocr"), OcrUseGpu = F("gpu"), GroupParagraphs = F("paragraphs"), MonitorIndex = int.Parse(C("monitor"), CultureInfo.InvariantCulture), MonitoringIntervalMs = (int)(V("interval") * 1000),
            OverlayMode = Enum.Parse<OverlayMode>(C("overlay")), OverlayFontSize = V("font"), OverlayOpacity = V("opacity") / 100, ClickThrough = F("clickThrough"), MatchSourceTypography = F("typography"), SampleOverlayBackground = F("background"), FreezeImage = F("freeze"),
            EnableOnlineDictionary = F("online"), SaveWordScreenshots = F("screenshots"), SaveWordAudio = F("audio"), AutoExplainWords = F("autoExplain"), SpeechVoice = C("voice"), AnkiEndpoint = T("ankiEndpoint"), AnkiDeck = T("ankiDeck"),
            AutoApplyGameProfiles = F("profiles"), ControllerEnabled = F("controller"), SaveHistory = F("history"), HistoryLimit = limit, MinimizeToTray = F("tray"), StartMinimized = F("minimized"), LightTheme = F("light"),
            Preprocessing = new() { Scale = V("scale"), Grayscale = F("grayscale"), EnhanceContrast = F("contrast"), Threshold = F("threshold"), Denoise = F("denoise") },
            Hotkeys = new() { Region = T("key-Region"), Fullscreen = T("key-Fullscreen"), Repeat = T("key-Repeat"), ToggleOverlay = T("key-ToggleOverlay"), Vocabulary = T("key-Vocabulary"), Interaction = T("key-Interaction"), ScreenTranslation = T("key-ScreenTranslation"), WordLookup = T("key-WordLookup") }
        };
    }

    private StackPanel Page(string id) { buildingPage = id; return pages[id]; }
    private TControl Track<TControl>(TControl control, string id, string name) where TControl : FrameworkElement { Identify(control, id, name); owners[control] = buildingPage; return control; }
    private static void Identify(DependencyObject control, string id, string name) { AutomationProperties.SetAutomationId(control, id); Ui.Set(control, AutomationProperties.NameProperty, new(name)); }
    private TextBox Field(Panel parent, string id, string label, string value, string? hint = null, string prefix = "")
    {
        var box = Text(parent, label, value); Track(box, prefix + id, label);
        if (fields.TryGetValue(id, out var original)) box.SetBinding(TextBox.TextProperty, new Binding(nameof(TextBox.Text)) { Source = original, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        else fields[id] = box;
        box.TextChanged += (_, _) => Changed(); if (hint is not null) parent.Children.Add(Muted(hint, new(0, -4, 0, 12))); return box;
    }
    private ComboBox Choice(Panel parent, string id, string label, IEnumerable<LanguageChoice> items, string selected)
    {
        Label(parent, label); var values = items.ToList(); if (!values.Any(i => i.Code == selected)) values.Add(new(selected, selected));
        var box = new ComboBox { ItemsSource = values, SelectedValuePath = "Code", SelectedValue = selected, Margin = new(0, 0, 0, 12) };
        choices[id] = Track(box, id, label); box.SelectionChanged += (_, _) => Changed(); parent.Children.Add(box); return box;
    }
    private CheckBox Toggle(Panel parent, string id, string title, string hint, bool value, string prefix = "")
    {
        var row = new Grid { Margin = new(0, 8, 0, 8) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { Margin = new(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center }; copy.Children.Add(Ui.Localize(new TextBlock { Text = title, FontWeight = FontWeights.Medium }));
        if (hint.Length > 0) { var help = Muted(hint, new(0, 3, 0, 0)); help.FontSize = 12; copy.Children.Add(help); } row.Children.Add(copy);
        var box = new CheckBox { IsChecked = value, Style = (Style)FindResource("SettingsSwitch"), VerticalAlignment = VerticalAlignment.Center };
        Track(box, prefix + id, title); if (flags.TryGetValue(id, out var original)) box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = original, Mode = BindingMode.TwoWay }); else flags[id] = box;
        box.Checked += (_, _) => Changed(); box.Unchecked += (_, _) => Changed(); Grid.SetColumn(box, 1); row.Children.Add(box); parent.Children.Add(row); return box;
    }
    private Slider Range(Panel parent, string id, string title, double value, double minimum, double maximum, double step, string suffix)
    {
        var header = new DockPanel { Margin = new(0, 12, 0, 6) }; var number = new TextBlock { FontWeight = FontWeights.SemiBold }; number.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        DockPanel.SetDock(number, Dock.Right); header.Children.Add(number); header.Children.Add(Ui.Text(title)); parent.Children.Add(header);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5, Style = (Style)FindResource("SettingsSlider"), Margin = new(0, 0, 0, 10) };
        void Update() => Ui.SetText(number, L.M("{0:0.##}{1}", slider.Value, new UiMessage(suffix)));
        sliders[id] = Track(slider, id, title); slider.ValueChanged += (_, _) => { Update(); Changed(); }; Update(); parent.Children.Add(slider); return slider;
    }
    private StackPanel Advanced(Panel parent, string title)
    {
        var body = new StackPanel { Margin = new(0, 10, 0, 2) };
        parent.Children.Add(Ui.Localize(new Expander { Header = title, Content = body, Style = (Style)FindResource("SettingsExpander") })); return body;
    }
    internal static TextBlock Muted(UiMessage text, Thickness? margin = null) { var block = Ui.Text(text); block.Margin = margin ?? new Thickness(0, 0, 0, 12); block.SetResourceReference(StyleProperty, "Muted"); return block; }
    internal static StackPanel Section(Panel root, string title, string description)
    {
        var panel = new StackPanel(); var border = new Border { Child = panel, Padding = new(18), Margin = new(0, 0, 0, 16) }; border.SetResourceReference(StyleProperty, "Card"); root.Children.Add(border);
        panel.Children.Add(Ui.Localize(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) })); if (description.Length > 0) panel.Children.Add(Muted(description)); return panel;
    }
    internal static void Label(Panel parent, string text) { var label = Muted(text, new(0, 6, 0, 7)); label.FontSize = 12; parent.Children.Add(label); }
    internal static TextBox Text(Panel parent, string label, string value) { Label(parent, label); var box = new TextBox { Text = value, Margin = new(0, 0, 0, 12) }; Ui.Set(box, AutomationProperties.NameProperty, new(label)); parent.Children.Add(box); return box; }
    internal static CheckBox Check(Panel parent, string label, bool value) { var box = new CheckBox { Content = Ui.Localize(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }), IsChecked = value }; parent.Children.Add(box); return box; }
}
