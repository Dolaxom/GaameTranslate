using System.Globalization;
using System.Windows.Input;
using GaameTranslate.Core;
using GaameTranslate.Windows;
using Microsoft.Win32;

namespace GaameTranslate.App;

public sealed partial class SettingsView
{
    private void BuildTranslation()
    {
        var page = Page("translation"); var s = vm.Settings;
        var pair = new Grid { Margin = new(0, 0, 0, 8) }; pair.ColumnDefinitions.Add(new()); pair.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); pair.ColumnDefinitions.Add(new());
        var left = new StackPanel(); var right = new StackPanel(); Grid.SetColumn(right, 2); pair.Children.Add(left); pair.Children.Add(right);
        var from = Choice(left, "source", "Язык игры", vm.SourceLanguages.Select(Localize), s.SourceLanguage);
        var to = Choice(right, "target", "Переводить на", vm.TargetLanguages.Select(Localize), s.TargetLanguage);
        var swap = Ui.Localize(new Button { Content = "⇄", FontSize = 22, Padding = new(11, 3, 11, 3), Margin = new(10, 22, 10, 12), ToolTip = "Поменять языки местами" }); Identify(swap, "swap-languages", "Поменять языки местами"); Grid.SetColumn(swap, 1); pair.Children.Add(swap);
        swap.IsEnabled = s.SourceLanguage != "auto"; from.SelectionChanged += (_, _) => swap.IsEnabled = (string?)from.SelectedValue != "auto";
        swap.Click += (_, _) => { var previous = from.SelectedValue; from.SelectedValue = to.SelectedValue; to.SelectedValue = previous; }; page.Children.Add(pair);
        var provider = Section(page, "Переводчик", "");
        connections.Add(new ConnectionEditor(this, provider, false));
        var advanced = Advanced(page, "Контекст игры и время ожидания");
        Field(advanced, "context", "О чём игра? Необязательно", s.ContextHint, "Например: детектив, морские термины, вежливая речь персонажей. До 1000 символов.");
        fields["context"].AcceptsReturn = true; fields["context"].TextWrapping = TextWrapping.Wrap; fields["context"].MinHeight = 72;
        Range(advanced, "timeout", "Ждать ответа переводчика", s.TranslationTimeoutSeconds, 10, 600, 10, " с");
        advanced.Children.Add(Muted("Локальной модели может понадобиться больше времени при первом запуске."));
    }

    private void BuildScreen()
    {
        var page = Page("screen"); var s = vm.Settings;
        var appearance = Section(page, "Как показывать перевод", "Экранный перевод по Alt+Q всегда размещает текст поверх реплик игры.");
        Choice(appearance, "overlay", "Перевод выбранной области", [new("Floating", "Отдельное окно"), new("Replacement", "Поверх исходного текста"), new("Spatial", "Рядом с репликами"), new("SidePanel", "Панель сбоку")], s.OverlayMode.ToString());
        var preview = new Border { CornerRadius = new(10), Padding = new(20), Margin = new(0, 4, 0, 10), Background = new LinearGradientBrush(Color.FromRgb(30, 57, 62), Color.FromRgb(34, 38, 55), 20) };
        var previewText = Ui.Localize(new TextBlock { Text = "Путешествие начинается здесь.", Foreground = Brushes.White, FontSize = s.OverlayFontSize, TextWrapping = TextWrapping.Wrap });
        var previewCard = new Border { CornerRadius = new(7), Padding = new(16, 12, 16, 12), Child = previewText };
        var previewContents = new StackPanel(); previewContents.Children.Add(Ui.Localize(new TextBlock { Text = "ПРИМЕР ТЕКСТА", Foreground = new SolidColorBrush(Color.FromRgb(168, 191, 190)), FontSize = 10, Margin = new(0, 0, 0, 12) })); previewContents.Children.Add(previewCard); preview.Child = previewContents; appearance.Children.Add(preview);
        var font = Range(appearance, "font", "Размер текста в отдельном окне", s.OverlayFontSize, 12, 40, 1, " пт");
        var opacity = Range(appearance, "opacity", "Плотность подложки", s.OverlayOpacity * 100, 30, 100, 1, "%");
        void Preview() { previewText.FontSize = font.Value; previewCard.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity.Value / 100 * 255), 16, 19, 24)); }
        font.ValueChanged += (_, _) => Preview(); opacity.ValueChanged += (_, _) => Preview(); Preview();
        Toggle(appearance, "typography", "Подбирать размер по игре", "В экранном переводе размер зависит от исходной реплики.", s.MatchSourceTypography);
        Toggle(appearance, "background", "Подбирать цвет подложки", "Использовать фон игрового изображения.", s.SampleOverlayBackground);
        var behavior = Advanced(appearance, "Поведение слоя");
        Toggle(behavior, "clickThrough", "Пропускать нажатия в игру", "Для выбора слов включите взаимодействие горячей клавишей.", s.ClickThrough);
        Toggle(behavior, "freeze", "Показывать стоп-кадр", "Изображение фиксируется, но сама игра продолжает работать.", s.FreezeImage);
        var recognition = Section(page, "Распознавание текста", "Текст распознаётся на вашем компьютере.");
        Choice(recognition, "engine", "Качество распознавания", [new("PP-OCRv6 Small", "Быстро · PP-OCRv6 Small"), new("PP-OCRv6 Medium", "Точнее · PP-OCRv6 Medium"), new("Windows", "Системное · Windows OCR")], s.OcrEngine);
        Choice(recognition, "ocr", "Язык распознавания", vm.OcrLanguages.Select(l => l.Code == "auto" ? new LanguageChoice("auto", "Автоматически по выбранному движку") : Localize(l)), s.OcrLanguage);
        var advanced = Advanced(recognition, "Монитор и тонкая настройка OCR");
        advanced.Children.Add(Muted("PP-OCRv6 поддерживает английский, японский и китайский. Для кириллицы выберите русский явно: будет использован Windows OCR."));
        Choice(advanced, "monitor", "Экран для захвата", new[] { new LanguageChoice("-1", "Монитор под курсором") }.Concat(DesktopCapture.Displays.Select(d => new LanguageChoice(d.Index.ToString(CultureInfo.InvariantCulture), L.M("Монитор {0} · {1:0} × {2:0}", d.Index + 1, d.Bounds.Width, d.Bounds.Height)))), s.MonitorIndex.ToString(CultureInfo.InvariantCulture));
        Toggle(advanced, "gpu", "Использовать видеокарту", "Ускорение распознавания, если оно доступно.", s.OcrUseGpu);
        Toggle(advanced, "paragraphs", "Объединять строки в реплики", "Помогает сохранить смысл многострочных диалогов.", s.GroupParagraphs);
        Range(advanced, "scale", "Увеличение изображения", s.Preprocessing.Scale, .5, 3, .1, "×");
        Toggle(advanced, "grayscale", "Чёрно-белое изображение", "Убирает цвет перед распознаванием.", s.Preprocessing.Grayscale);
        Toggle(advanced, "contrast", "Усилить контраст", "Для бледных надписей на однородном фоне.", s.Preprocessing.EnhanceContrast);
        Toggle(advanced, "threshold", "Оставить только светлое и тёмное", "Подходит для простого контрастного текста.", s.Preprocessing.Threshold);
        Toggle(advanced, "denoise", "Сглаживать шум", "Может смягчить тонкие штрихи букв.", s.Preprocessing.Denoise);
        Range(advanced, "interval", "Пауза между автопереводами", s.MonitoringIntervalMs / 1000d, .5, 10, .5, " с");
        advanced.Children.Add(Muted("Автоперевод области запускается отдельно на странице перевода."));
    }

    private void BuildDictionary()
    {
        var page = Page("dictionary"); var s = vm.Settings;
        var helper = Section(page, "Значение слова в контексте", "Помощник объясняет выражения и оттенки смысла в игровой реплике.");
        connections.Add(new ConnectionEditor(this, helper, true));
        var automatic = Toggle(helper, "autoExplain", "Объяснять при открытии слова", "Слово и реплика отправляются выбранному помощнику.", s.AutoExplainWords);
        void UpdateAssistant() { automatic.IsEnabled = (string?)choices["assistant"].SelectedValue != "None"; ((FrameworkElement)automatic.Parent).Visibility = automatic.IsEnabled ? Visibility.Visible : Visibility.Collapsed; }
        choices["assistant"].SelectionChanged += (_, _) => UpdateAssistant(); UpdateAssistant();
        var dictionaries = Section(page, "Локальные словари", "Импортируйте словарь, чтобы получать статьи без интернета.");
        var state = Muted("Yomitan ZIP, DSL, StarDict, Kaikki JSONL, CC-CEDICT."); dictionaries.Children.Add(state);
        var import = Ui.Localize(new Button { Content = "＋ Импортировать словарь", HorizontalAlignment = HorizontalAlignment.Left }); dictionaries.Children.Add(import);
        async Task Refresh()
        {
            var sources = await Task.Run(() => vm.Dictionary.Local.SourcesAsync());
            Ui.SetText(state, sources.Count == 0 ? new UiMessage("Пока нет словарей. Поддерживаются Yomitan, DSL, StarDict, Kaikki и CC-CEDICT.") : L.M("{0}", L.Join("\n", sources.Select(item => L.M("{0} · {1:N0} статей", item.Name, item.Count)))));
        }
        import.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = L.Get("Словари|*.zip;*.dsl;*.ifo;*.jsonl;*.txt|Все файлы|*.*"), Title = L.Get("Импорт словаря") };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            import.IsEnabled = false;
            try { Ui.SetText(state, "Импортируем словарь…"); await Task.Run(() => vm.Dictionary.Local.ImportAsync(dialog.FileName)); await Refresh(); }
            catch (Exception error) { Ui.Error(state, error, "Не удалось импортировать словарь."); }
            finally { import.IsEnabled = true; }
        };
        dictionaries.Loaded += async (_, _) => { try { await Refresh(); } catch { Ui.SetText(state, "Не удалось прочитать список словарей."); } };
        Toggle(dictionaries, "online", "Дополнять английские статьи из интернета", "Free Dictionary API получает выбранное слово.", s.EnableOnlineDictionary);
        var cards = Section(page, "Озвучка и карточки", "Сохранённые слова остаются на этом компьютере.");
        Choice(cards, "voice", "Голос для озвучки", new[] { new LanguageChoice("", "Автоматически по языку") }.Concat(vm.Speech.Voices), s.SpeechVoice);
        Toggle(cards, "screenshots", "Добавлять снимок к карточке", "Сохраняется только фрагмент с выбранной репликой.", s.SaveWordScreenshots);
        Toggle(cards, "audio", "Добавлять озвучку к карточке", "Звуковой файл сохраняется вместе со словом.", s.SaveWordAudio);
        var anki = Advanced(cards, "Подключение Anki");
        Field(anki, "ankiDeck", "Название колоды", s.AnkiDeck);
        Field(anki, "ankiEndpoint", "Адрес AnkiConnect", s.AnkiEndpoint, "Anki и дополнение AnkiConnect должны быть запущены. Синхронизация доступна на странице словаря.");
    }

    private void BuildShortcuts()
    {
        var page = Page("shortcuts"); var panel = Section(page, "Управление без переключения из игры", "Нажмите на сочетание и введите новое. Tab переводит фокус к следующему полю.");
        var labels = new Dictionary<string, (string Title, string Hint)>
        {
            ["ScreenTranslation"] = ("Перевести экран", "Повторное нажатие закрывает перевод"), ["WordLookup"] = ("Слово под курсором", "Перевод, объяснение и сохранение слова"),
            ["Region"] = ("Выбрать область", "Выделить фрагмент экрана мышью"), ["Repeat"] = ("Повторить область", "Перевести тот же фрагмент ещё раз"),
            ["Fullscreen"] = ("Захватить весь экран", "Показать перевод в выбранном режиме"), ["ToggleOverlay"] = ("Показать или скрыть перевод", "Переключить видимость готового результата"),
            ["Interaction"] = ("Взаимодействовать со слоем", "Переключиться между выбором слов и игрой"), ["Vocabulary"] = ("Открыть словарь", "Перейти к сохранённым словам")
        };
        var bindings = GlobalHotkeys.Bindings(vm.Settings.Hotkeys).ToDictionary(b => b.Action, b => b.Shortcut);
        foreach (var item in labels)
        {
            var row = new Grid { Margin = new(0, 8, 0, 8) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(180) });
            var copy = new StackPanel { Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center }; copy.Children.Add(Ui.Localize(new TextBlock { Text = item.Value.Title, FontWeight = FontWeights.Medium })); var hint = Muted(item.Value.Hint, new(0, 3, 0, 0)); hint.FontSize = 12; copy.Children.Add(hint); row.Children.Add(copy);
            var box = new TextBox { Text = bindings[item.Key], IsReadOnly = true, HorizontalContentAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Нажмите Ctrl, Alt, Shift или Win вместе с буквой, цифрой или F1–F24" };
            Ui.Localize(box); fields["key-" + item.Key] = Track(box, "key-" + item.Key, item.Value.Title); box.TextChanged += (_, _) => Changed();
            box.GotKeyboardFocus += (_, _) => vm.SetShortcutRecording(true);
            box.LostKeyboardFocus += (_, _) => vm.SetShortcutRecording(false);
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key; if (key == Key.Tab) return; e.Handled = true;
                if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
                var modifiers = Keyboard.Modifiers; if (modifiers == ModifierKeys.None || !((key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9) || (key >= Key.F1 && key <= Key.F24))) return;
                var parts = new List<string>(); if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl"); if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt"); if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift"); if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture) : key.ToString()); box.Text = string.Join('+', parts);
            };
            Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row);
        }
        var reset = Ui.Localize(new Button { Content = "Вернуть стандартные сочетания", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 16, 0, 0) });
        reset.Click += (_, _) => { foreach (var binding in GlobalHotkeys.Bindings(new())) fields["key-" + binding.Action].Text = binding.Shortcut; }; panel.Children.Add(reset);
    }

    private void BuildGames()
    {
        var page = Page("games"); var s = vm.Settings;
        var profiles = Section(page, "Настройки для отдельных игр", "Профиль автоматически выбирается по запущенной игре перед захватом экрана.");
        Toggle(profiles, "profiles", "Применять игровые профили", "Настройки других игр и приложения сохраняются отдельно.", s.AutoApplyGameProfiles);
        var editor = Advanced(profiles, "Сохранить профиль игры");
        var name = Text(editor, "Название игры", ""); Track(name, "profile-name", "Название игры"); var process = Text(editor, "Имя процесса игры", ""); Track(process, "profile-process", "Имя процесса игры"); editor.Children.Add(Muted("Например disco.exe. Имя можно посмотреть в диспетчере задач Windows."));
        var state = Muted(""); profiles.Children.Add(state);
        var saveProfile = Ui.Localize(new Button { Content = "Сохранить настройки и профиль", HorizontalAlignment = HorizontalAlignment.Left }); editor.Children.Add(saveProfile);
        async Task Refresh() { var list = await vm.Profiles.LoadAsync(); Ui.SetText(state, list.Count == 0 ? new UiMessage("Сохранённых профилей пока нет.") : L.M("{0}", string.Join("\n", list.Select(p => $"{p.Name} · {p.Executable}")))); }
        saveProfile.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) Invalid(name, "Введите название игры.");
                if (string.IsNullOrWhiteSpace(process.Text) || process.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Invalid(process, "Введите имя процесса без пути, например disco.exe.");
                if (!await SaveAsync()) return;
                await vm.Profiles.SaveAsync(new(name.Text.Trim(), process.Text.Trim(), vm.Settings)); await Refresh(); Ui.SetText(feedback, "Настройки и профиль сохранены");
            }
            catch (Exception error) { ShowError(error); }
        };
        profiles.Loaded += async (_, _) => { try { await Refresh(); } catch { Ui.SetText(state, "Не удалось прочитать игровые профили."); } };
        var controller = Section(page, "Геймпад", "Быстрые действия для контроллеров с поддержкой XInput.");
        Toggle(controller, "controller", "Использовать сочетания геймпада", "Удерживайте LB + RB и нажмите одну из кнопок ниже.", s.ControllerEnabled);
        controller.Children.Add(Muted("Y — перевести экран\nX — повторить перевод области\nB — показать или скрыть перевод"));
    }

    private void BuildApplication()
    {
        var page = Page("app"); var s = vm.Settings;
        var help = Section(page, "Getting started", "Help is always available from the sidebar and tray menu.");
        Toggle(help, "startupTips", "Show dictionary-mode tips at startup", "A non-blocking reminder appears when no full translation provider is configured.", s.ShowStartupTips);
        var guide = Ui.Localize(new Button { Content = "Quick start guide", HorizontalAlignment = HorizontalAlignment.Left }); guide.Click += (_, _) => vm.OpenQuickStartCommand.Execute(null); help.Children.Add(guide);
        var appearance = Section(page, "Привычный вид и поведение", "Изменения применяются после сохранения.");
        Choice(appearance, "interfaceLanguage", "Язык интерфейса / Interface language", [new("ru", "Русский"), new("en", "English")], s.InterfaceLanguage);
        Toggle(appearance, "light", "Светлая тема", "Выключите, чтобы использовать тёмную тему.", s.LightTheme);
        Toggle(appearance, "tray", "Сворачивать в трей при закрытии", "Горячие клавиши продолжат работать в фоне.", s.MinimizeToTray);
        Toggle(appearance, "minimized", "Запускать свёрнутым", "При запуске будет виден только значок в трее.", s.StartMinimized);
        var privacy = Section(page, "История и данные", "История, словари и карточки хранятся на этом компьютере. Аналитики нет.");
        Toggle(privacy, "history", "Сохранять историю переводов", "Отключение останавливает запись новых переводов.", s.SaveHistory);
        var limits = Advanced(privacy, "Сколько хранить и что удалить");
        Field(limits, "historyLimit", "Максимум переводов в истории", s.HistoryLimit.ToString(CultureInfo.InvariantCulture), "От 0 до 10 000. Сохранённые слова не входят в этот лимит.");
        var delete = Ui.Localize(new Button { Content = "Удалить все сохранённые слова…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) }); delete.SetResourceReference(Control.ForegroundProperty, "SettingsErrorBrush");
        delete.Click += async (_, _) => { if (Dialogs.Confirm(Window.GetWindow(this), "Удалить все слова?", "Сохранённые слова и расписание повторений будут удалены. Это действие нельзя отменить.")) { try { await vm.ClearWordsAsync(); Ui.SetText(feedback, "Словарь очищен"); } catch (Exception error) { ShowError(error); } } }; limits.Children.Add(delete);
    }

    private static LanguageChoice Localize(LanguageChoice language) => new(language.Code, language.Code switch
    {
        "auto" => "Определять автоматически", "en" or "en-US" => "Английский" + (language.Code == "en-US" ? " · Windows" : ""), "ru" => "Русский", "ja" => "Японский", "zh" => "Китайский", "ko" => "Корейский", "de" => "Немецкий", "fr" => "Французский", "es" => "Испанский", "it" => "Итальянский", "pt" => "Португальский", "pl" => "Польский", "uk" => "Украинский", "tr" => "Турецкий", _ => language.Name
    });
}
