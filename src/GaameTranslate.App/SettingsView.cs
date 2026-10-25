using System.Globalization;
using GaameTranslate.Core;
using GaameTranslate.Windows;
using Microsoft.Win32;

namespace GaameTranslate.App;

public sealed class SettingsView : UserControl
{
    private readonly MainViewModel vm;
    private readonly ComboBox provider, source, target, ocr, overlay, monitor;
    private readonly PasswordBox apiKey = new();
    private readonly TextBlock keyState = new();
    private readonly StackPanel librePanel = new(), azurePanel = new();
    private readonly TextBox endpoint, azureRegion, context, fontSize, opacity, scale, historyLimit;
    private readonly CheckBox removeKey, clickThrough, grayscale, contrast, threshold, denoise, history, tray, minimized, light;
    private readonly Dictionary<string, TextBox> shortcuts = new();
    private readonly Dictionary<string, TextBox> extra = new();
    private readonly Dictionary<string, CheckBox> flags = new();
    private readonly ComboBox engine, assistant, voice;
    private readonly PasswordBox assistantKey = new();
    public SettingsView(MainViewModel model)
    {
        vm = model; var s = vm.Settings;
        var root = new StackPanel(); Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "Make it yours.", FontSize = 30, FontWeight = FontWeights.SemiBold }); root.Children.Add(Muted("Good defaults. Just the controls you need.", new Thickness(0, 8, 0, 24)));
        var translation = Section(root, "Translation", "Only recognized text is sent to your selected service. Images stay on your PC.");
        provider = Choice(translation, "Сервис перевода", ProviderCatalog.All.Select(p => p.Name).ToArray(), s.Provider);
        source = LanguageCombo(translation, "Source language", vm.SourceLanguages, s.SourceLanguage); target = LanguageCombo(translation, "Target language", vm.TargetLanguages, s.TargetLanguage);
        var swap = new Button { Content = "Swap languages", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 14) };
        swap.Click += (_, _) => { if ((string?)source.SelectedValue == "auto") { vm.Status = "Choose an explicit source language before swapping."; return; } var from = source.SelectedValue; source.SelectedValue = target.SelectedValue; target.SelectedValue = from; }; translation.Children.Add(swap);
        Label(translation, "API key · leave empty to keep the saved key"); translation.Children.Add(apiKey); keyState.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); keyState.Margin = new(0, 6, 0, 8); translation.Children.Add(keyState);
        removeKey = Check(translation, "Delete the saved key for this provider", false);
        translation.Children.Add(librePanel); translation.Children.Add(azurePanel);
        endpoint = Text(librePanel, "LibreTranslate base address", s.LibreEndpoint); azureRegion = Text(azurePanel, "Azure resource region (required for regional resources)", s.AzureRegion);
        context = Text(translation, "Контекст игры · жанр, имена и стиль речи", s.ContextHint);
        void Field(Panel p, string id, string label, string value) => extra[id] = Text(p, label, value);
        void Flag(Panel p, string id, string label, bool value) => flags[id] = Check(p, label, value);
        var connections = Section(root, "Дополнительные подключения", "Выберите сервис выше и настройте его поля. API-ключи сохраняются отдельно для каждого сервиса в Windows DPAPI.");
        var servicePanels = new Dictionary<string, StackPanel>();
        StackPanel Service(string name) { var panel = new StackPanel(); servicePanels[name] = panel; connections.Children.Add(panel); return panel; }
        var yandex = Service("Yandex"); Field(yandex, "folder", "ID каталога Yandex Cloud", s.YandexFolderId); Flag(yandex, "iam", "IAM-токен (Bearer) вместо API-ключа", s.YandexIamToken); Field(yandex, "yandexModel", "Модель YandexGPT · пусто = gpt://<каталог>/aliceai-llm", s.YandexModel);
        yandex.Children.Add(Muted("Translate: ключ сервисного аккаунта не требует каталога в запросе; для пользовательского IAM-токена каталог обязателен. YandexGPT требует каталог и разрешения AI Studio."));
        var giga = Service("GigaChat"); Field(giga, "gigaModel", "Модель GigaChat", s.GigaChatModel); Field(giga, "gigaScope", "OAuth scope", s.GigaChatScope); giga.Children.Add(Muted("Ключ: Authorization key (Base64 client_id:client_secret). Проверка сертификатов TLS остаётся включённой."));
        var local = Service("Ollama"); Field(local, "ollama", "Адрес Ollama", s.OllamaEndpoint); Field(local, "ollamaModel", "Модель перевода Ollama", s.OllamaModel); local.Children.Add(Muted("TranslateGemma переводит реплики. Qwen подходит для контекстного перевода и объяснений. scripts/Start-LocalModels.ps1 -Assistant устанавливает обе модели."));
        var compatible = Service("OpenAI compatible"); Field(compatible, "compatible", "Базовый адрес совместимого API", s.CompatibleEndpoint); Field(compatible, "compatibleModel", "ID модели", s.CompatibleModel);
        assistant = Choice(connections, "Помощник словаря и объяснений", new[] { "None" }.Concat(ProviderCatalog.All.Where(p => ProviderCatalog.IsLanguageModel(p.Name)).Select(p => p.Name)).ToArray(), s.AssistantProvider);
        Field(connections, "assistantModel", "Модель помощника Ollama", s.AssistantModel); Label(connections, "Ключ помощника · пустое поле сохраняет существующий"); connections.Children.Add(assistantKey);
        Field(connections, "timeout", "Таймаут запроса, секунд · 10–600", s.TranslationTimeoutSeconds.ToString());
        void UpdateConnections() { var name = (string?)provider.SelectedItem ?? ""; var helper = (string?)assistant.SelectedItem ?? ""; foreach (var p in servicePanels) p.Value.Visibility = (p.Key == name || p.Key == helper || (p.Key == "Yandex" && (name.StartsWith("Yandex") || helper.StartsWith("Yandex")))) ? Visibility.Visible : Visibility.Collapsed; }
        provider.SelectionChanged += (_, _) => { UpdateProvider(); UpdateConnections(); }; assistant.SelectionChanged += (_, _) => { assistantKey.Clear(); UpdateConnections(); }; UpdateProvider(); UpdateConnections();
        var ocrPanel = Section(root, "Capture & OCR", vm.OcrSummary);
        engine = Choice(ocrPanel, "Движок OCR", new[] { "PP-OCRv6 Small", "PP-OCRv6 Medium", "Windows" }, s.OcrEngine); Flag(ocrPanel, "gpu", "Ускорять OCR на GPU через Vulkan", s.OcrUseGpu); Flag(ocrPanel, "paragraphs", "Объединять строки в реплики перед переводом", s.GroupParagraphs);
        ocrPanel.Children.Add(Muted("PP-OCRv6 Small: быстрее; Medium: точнее. Для кириллицы и европейских языков используется установленный Windows OCR. Всё распознавание происходит на компьютере."));
        ocr = LanguageCombo(ocrPanel, "Installed recognition language · choose Japanese explicitly for Japanese games", vm.OcrLanguages, s.OcrLanguage);
        var displays = new List<LanguageChoice> { new("-1", "Monitor under the cursor") }; displays.AddRange(DesktopCapture.Displays.Select(d => new LanguageChoice(d.Index.ToString(CultureInfo.InvariantCulture), d.ToString())));
        monitor = LanguageCombo(ocrPanel, "Fullscreen capture monitor", displays, s.MonitorIndex.ToString(CultureInfo.InvariantCulture));
        scale = Text(ocrPanel, "Image scale · 0.5–3 (1.5 helps small text)", s.Preprocessing.Scale.ToString(CultureInfo.InvariantCulture));
        grayscale = Check(ocrPanel, "Grayscale", s.Preprocessing.Grayscale); contrast = Check(ocrPanel, "Enhance contrast", s.Preprocessing.EnhanceContrast); threshold = Check(ocrPanel, "Threshold · best for plain high-contrast text", s.Preprocessing.Threshold); denoise = Check(ocrPanel, "Smooth image noise · may soften fine strokes", s.Preprocessing.Denoise);
        ocrPanel.Children.Add(Muted("Windows profile language selects an installed recognizer; it does not detect image language. Install OCR packs through Windows language options and restart this app."));
        Field(ocrPanel, "interval", "Интервал автоперевода области, мс · 500–10000", s.MonitoringIntervalMs.ToString());
        var overlayPanel = Section(root, "Overlay", "Use the interaction shortcut to switch between word selection and clicking through to your game.");
        overlay = Choice(overlayPanel, "Display mode", Enum.GetValues<OverlayMode>(), s.OverlayMode); fontSize = Text(overlayPanel, "Text size · 12–40", s.OverlayFontSize.ToString(CultureInfo.InvariantCulture)); opacity = Text(overlayPanel, "Background opacity · 0.3–1", s.OverlayOpacity.ToString(CultureInfo.InvariantCulture)); clickThrough = Check(overlayPanel, "Start overlays in click-through mode", s.ClickThrough);
        Flag(overlayPanel, "typography", "Подбирать размер текста по исходной реплике", s.MatchSourceTypography); Flag(overlayPanel, "background", "Подбирать цвет фона по изображению", s.SampleOverlayBackground);
        Flag(overlayPanel, "freeze", "Зафиксировать изображение под переводом (игра продолжает работать)", s.FreezeImage);
        var profiles = Section(root, "Игровые профили и геймпад", "Профиль выбирается по имени активного .exe перед захватом. Сначала сохраните настройки, затем запишите профиль.");
        Flag(profiles, "profiles", "Автоматически применять игровой профиль", s.AutoApplyGameProfiles); Flag(profiles, "controller", "Включить сочетания XInput-геймпада", s.ControllerEnabled);
        profiles.Children.Add(Muted("Геймпад: удерживайте LB+RB, затем Y — экранный перевод; X — повтор области; B — показать/скрыть слой. Для других действий используются мышь и клавиатура."));
        var profileName = Text(profiles, "Название профиля", ""); var processName = Text(profiles, "Имя игрового процесса, например disco.exe", ""); var profileSave = new Button { Content = "Сохранить текущие настройки в профиль", HorizontalAlignment = HorizontalAlignment.Left }; profiles.Children.Add(profileSave);
        profileSave.Click += async (_, _) => await vm.GuardAsync(async () => { await vm.Profiles.SaveAsync(new(profileName.Text.Trim(), processName.Text.Trim(), vm.Settings)); vm.Status = "Игровой профиль сохранён."; });
        var dictionaries = Section(root, "Словарь и карточки", "Словари работают локально. Снимки и озвучка сохраняются в карточки только при включении соответствующих опций.");
        Flag(dictionaries, "online", "Английские статьи через Free Dictionary API", s.EnableOnlineDictionary); Flag(dictionaries, "screenshots", "Сохранять фрагмент экрана в карточку", s.SaveWordScreenshots); Flag(dictionaries, "audio", "Сохранять озвучку слова в карточку", s.SaveWordAudio);
        Flag(dictionaries, "autoExplain", "Автоматически объяснять слово выбранным помощником (передаёт слово и контекст помощнику)", s.AutoExplainWords);
        voice = LanguageCombo(dictionaries, "Голос Windows", new[] { new LanguageChoice("", "Автоматически по языку") }.Concat(vm.Speech.Voices), s.SpeechVoice);
        var dictionaryState = Muted("Поддерживаются Yomitan .zip, DSL, StarDict .ifo, Kaikki .jsonl и CC-CEDICT .txt."); dictionaries.Children.Add(dictionaryState);
        var import = new Button { Content = "Импорт словаря…", HorizontalAlignment = HorizontalAlignment.Left }; dictionaries.Children.Add(import);
        Field(dictionaries, "ankiEndpoint", "Адрес AnkiConnect на этом компьютере", s.AnkiEndpoint); Field(dictionaries, "ankiDeck", "Колода для синхронизации", s.AnkiDeck);
        async Task RefreshDictionaries() { var sources = await Task.Run(() => vm.Dictionary.Local.SourcesAsync()); dictionaryState.Text = sources.Count == 0 ? "Словари пока не импортированы. Yomitan, DSL, StarDict, Kaikki JSONL, CC-CEDICT." : string.Join("\n", sources.Select(s => $"{s.Name} · {s.Count:N0} статей")); }
        import.Click += async (_, _) => await vm.GuardAsync(async () => { var dialog = new OpenFileDialog { Filter = "Словари|*.zip;*.dsl;*.ifo;*.jsonl;*.txt|Все файлы|*.*" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return; import.IsEnabled = false; try { dictionaryState.Text = "Импорт…"; var count = await Task.Run(() => vm.Dictionary.Local.ImportAsync(dialog.FileName)); vm.Status = $"Импортировано статей: {count}"; await RefreshDictionaries(); } finally { import.IsEnabled = true; } });
        Loaded += async (_, _) => await vm.GuardAsync(RefreshDictionaries);
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
        var general = Section(root, "General & privacy", "История и словарь хранятся локально. Аналитики нет. Автозахват включается отдельной кнопкой и останавливается кнопкой Стоп. Снимки карточек по умолчанию выключены.");
        history = Check(general, "Save translation history on this PC", s.SaveHistory); historyLimit = Text(general, "Maximum history entries · 0–10,000", s.HistoryLimit.ToString(CultureInfo.InvariantCulture));
        tray = Check(general, "Keep running in the tray when the window closes", s.MinimizeToTray); minimized = Check(general, "Start in the system tray", s.StartMinimized); light = Check(general, "Use light appearance", s.LightTheme);
        var clear = new Button { Content = "Delete all vocabulary…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 4) }; clear.Click += async (_, _) => { if (Dialogs.Confirm(Window.GetWindow(this), "Delete your vocabulary?", "This permanently removes every saved word and its review schedule.")) await vm.GuardAsync(vm.ClearWordsAsync); }; general.Children.Add(clear);
        var save = new Button { Content = "Save settings", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 20) }; save.Click += async (_, _) => await SaveAsync(); root.Children.Add(save);
        var test = new Button { Content = "Проверить сохранённое подключение", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 24) }; test.Click += async (_, _) => await vm.GuardAsync(vm.TestProviderAsync); root.Children.Add(test);
    }
    private void UpdateProvider()
    {
        var name = (string?)provider.SelectedItem ?? "DeepL Free";
        librePanel.Visibility = name == "LibreTranslate" ? Visibility.Visible : Visibility.Collapsed; azurePanel.Visibility = name == "Azure" ? Visibility.Visible : Visibility.Collapsed;
        keyState.Text = ProviderCatalog.All.First(p => p.Name == name).Description + "\n" + (vm.HasKey(name) ? "Ключ сохранён." : ProviderCatalog.RequiresKey(name) ? "Добавьте API-ключ для подключения." : "Локальный сервер может работать без ключа."); keyState.TextWrapping = TextWrapping.Wrap;
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
            AutoExplainWords = flags["autoExplain"].IsChecked == true,
            FreezeImage = flags["freeze"].IsChecked == true,
            AutoApplyGameProfiles = flags["profiles"].IsChecked == true,
            ControllerEnabled = flags["controller"].IsChecked == true,
            AnkiEndpoint = extra["ankiEndpoint"].Text.Trim(),
            AnkiDeck = extra["ankiDeck"].Text.Trim(),
            YandexFolderId = extra["folder"].Text.Trim(),
            YandexIamToken = flags["iam"].IsChecked == true,
            YandexModel = extra["yandexModel"].Text.Trim(),
            GigaChatModel = extra["gigaModel"].Text.Trim(),
            GigaChatScope = extra["gigaScope"].Text.Trim(),
            OllamaEndpoint = extra["ollama"].Text.Trim(),
            OllamaModel = extra["ollamaModel"].Text.Trim(),
            CompatibleEndpoint = extra["compatible"].Text.Trim(),
            CompatibleModel = extra["compatibleModel"].Text.Trim(),
            AssistantProvider = (string)assistant.SelectedItem,
            AssistantModel = extra["assistantModel"].Text.Trim(),
            TranslationTimeoutSeconds = (int)Number(extra["timeout"], "timeout"),
            OcrEngine = (string)engine.SelectedItem,
            OcrUseGpu = flags["gpu"].IsChecked == true,
            GroupParagraphs = flags["paragraphs"].IsChecked == true,
            MonitoringIntervalMs = (int)Number(extra["interval"], "interval"),
            MatchSourceTypography = flags["typography"].IsChecked == true,
            SampleOverlayBackground = flags["background"].IsChecked == true,
            SaveWordScreenshots = flags["screenshots"].IsChecked == true,
            SaveWordAudio = flags["audio"].IsChecked == true,
            EnableOnlineDictionary = flags["online"].IsChecked == true,
            SpeechVoice = (string)voice.SelectedValue,
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
            Hotkeys = new() { Region = shortcuts["Region"].Text, Fullscreen = shortcuts["Fullscreen"].Text, Repeat = shortcuts["Repeat"].Text, ToggleOverlay = shortcuts["ToggleOverlay"].Text, Vocabulary = shortcuts["Vocabulary"].Text, Interaction = shortcuts["Interaction"].Text, ScreenTranslation = shortcuts["ScreenTranslation"].Text, WordLookup = shortcuts["WordLookup"].Text }
        };
        await vm.SaveSettingsAsync(next, apiKey.Password, removeKey.IsChecked == true); vm.SaveAssistantKey(next.AssistantProvider, assistantKey.Password); assistantKey.Clear(); removeKey.IsChecked = false; UpdateProvider();
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
