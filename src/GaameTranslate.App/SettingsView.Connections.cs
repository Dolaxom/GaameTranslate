using System.Windows.Automation;
using GaameTranslate.Core;

namespace GaameTranslate.App;

public sealed partial class SettingsView
{
    private sealed class ConnectionEditor
    {
        private readonly SettingsView owner;
        private readonly bool helper;
        private readonly ComboBox selector;
        private readonly StackPanel body = new(), keySlot = new(), keyArea = new();
        private readonly Dictionary<string, StackPanel> services = new();
        private readonly PasswordBox key;
        private readonly TextBlock description, keyState, result, fallbackHint;
        private readonly Button test, remove;
        private readonly Expander optionalKey;
        private CancellationTokenSource? checking;
        private bool updatingKey;
        public string Name => (string?)selector.SelectedValue ?? "None";
        public TextBox Input(string id) => owner.owners.Keys.OfType<TextBox>().First(c => AutomationProperties.GetAutomationId(c) == (helper ? "assistant-" : "translation-") + id);

        public ConnectionEditor(SettingsView view, Panel parent, bool assistant)
        {
            owner = view; helper = assistant; var s = owner.vm.Settings; var prefix = helper ? "assistant-" : "translation-";
            var available = ProviderCatalog.All.Where(p => !helper || p.LanguageModel).Select(p => new LanguageChoice(p.Name, ProviderLabel(p.Name)));
            if (helper) available = new[] { new LanguageChoice("None", "Без AI-помощника") }.Concat(available);
            selector = owner.Choice(parent, helper ? "assistant" : "provider", helper ? "Помощник" : "Сервис", available, helper ? s.AssistantProvider : s.Provider);
            description = Muted(""); description.FontSize = 12; parent.Children.Add(description); parent.Children.Add(body);
            fallbackHint = Muted("If translation fails or takes over 5 seconds, English → Russian uses the built-in dictionary. The service is retried after 15 seconds. Connection tests use the configured timeout.", new(0, 10, 0, 0)); fallbackHint.FontSize = 12; parent.Children.Add(fallbackHint);
            body.Children.Add(keySlot);
            Label(keyArea, "API-ключ"); key = new PasswordBox { Style = (Style)owner.FindResource("SettingsPassword"), Margin = new(0, 0, 0, 6) }; owner.Track(key, prefix + "key", "API-ключ"); keyArea.Children.Add(key);
            keyState = Muted("", new(0, 0, 0, 6)); keyState.FontSize = 12; keyArea.Children.Add(keyState);
            remove = Ui.Localize(new Button { Content = "Удалить сохранённый ключ", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 11, Padding = new(8, 4, 8, 4), Margin = new(0, 0, 0, 10) }); Identify(remove, prefix + "remove-key", "Удалить сохранённый ключ"); keyArea.Children.Add(remove);
            optionalKey = Ui.Localize(new Expander { Header = "API-ключ для защищённого сервера", Style = (Style)owner.FindResource("SettingsExpander"), Margin = new(0, 4, 0, 8) });
            StackPanel Service(string name) { var panel = new StackPanel { Visibility = Visibility.Collapsed }; services[name] = panel; body.Children.Add(panel); return panel; }
            var libre = Service("LibreTranslate");
            var libreOptions = owner.Advanced(libre, "Адрес сервера");
            owner.Field(libreOptions, "libre", "Адрес LibreTranslate", s.LibreEndpoint, "Для сервера на этом компьютере: http://127.0.0.1:5000", prefix);
            var ollama = Service("Ollama");
            owner.Field(ollama, helper ? "assistantModel" : "ollamaModel", "Модель", helper ? s.AssistantModel : s.OllamaModel, helper ? "Для объяснений нужна универсальная модель, например Qwen." : "Имя установленной модели, например translategemma:4b.", prefix);
            owner.Field(owner.Advanced(ollama, "Адрес локального сервера"), "ollama", "Адрес Ollama", s.OllamaEndpoint, "Обычно http://127.0.0.1:11434", prefix);
            var yandex = Service("Yandex Translate"); var auth = owner.Advanced(yandex, "Подключение через IAM-токен");
            var iam = owner.Toggle(auth, "iam", "Использовать IAM-токен", "Вставьте токен в поле API-ключа выше.", s.YandexIamToken, prefix);
            var folderPanel = new StackPanel(); auth.Children.Add(folderPanel); owner.Field(folderPanel, "folder", "ID каталога Yandex Cloud", s.YandexFolderId, "Обязателен для пользовательского IAM-токена.", prefix);
            void FolderVisibility() => folderPanel.Visibility = iam.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            iam.Checked += (_, _) => FolderVisibility(); iam.Unchecked += (_, _) => FolderVisibility(); FolderVisibility();
            var yandexGpt = Service("YandexGPT"); owner.Field(yandexGpt, "folder", "ID каталога Yandex Cloud", s.YandexFolderId, null, prefix + "gpt-");
            var gptOptions = owner.Advanced(yandexGpt, "Модель и тип доступа"); owner.Field(gptOptions, "yandexModel", "Модель · необязательно", s.YandexModel, "Если оставить пустым, используется модель по умолчанию в вашем каталоге.", prefix);
            owner.Toggle(gptOptions, "iam", "Использовать IAM-токен", "Вставьте токен вместо API-ключа.", s.YandexIamToken, prefix + "gpt-");
            var giga = Service("GigaChat"); owner.Field(giga, "gigaModel", "Модель", s.GigaChatModel, null, prefix);
            owner.Field(owner.Advanced(giga, "Область доступа"), "gigaScope", "Scope", s.GigaChatScope, "Для личного аккаунта обычно GIGACHAT_API_PERS.", prefix);
            var compatible = Service("OpenAI compatible"); owner.Field(compatible, "compatible", "Адрес API", s.CompatibleEndpoint, "Для LM Studio обычно http://127.0.0.1:1234/v1", prefix);
            owner.Field(compatible, "compatibleModel", "ID модели", s.CompatibleModel, null, prefix);
            owner.Field(Service("Azure"), "azureRegion", "Регион ресурса Azure", s.AzureRegion, "Укажите регион, если он требуется вашему ресурсу.", prefix);
            Service("DeepL Free"); Service("DeepL Pro");
            var testRow = new WrapPanel { Margin = new(0, 12, 0, 0) };
            test = Ui.Localize(new Button { Content = "Проверить подключение", Padding = new(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Left }); Identify(test, prefix + "test", "Проверить подключение"); testRow.Children.Add(test); body.Children.Add(testRow);
            result = Muted("Тестовая фраза · без сохранения настроек", new(0, 8, 0, 0)); result.FontSize = 12; AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite); Identify(result, prefix + "result", "Результат проверки"); body.Children.Add(result);
            key.PasswordChanged += (_, _) =>
            {
                if (updatingKey) return;
                owner.keyDrafts[Name] = key.Password; if (key.Password.Length > 0) owner.removedKeys.Remove(Name);
                foreach (var connection in owner.connections.Where(c => c.Name == Name)) connection.RefreshKey(); owner.Changed();
            };
            remove.Click += (_, _) => { if (!owner.removedKeys.Add(Name)) owner.removedKeys.Remove(Name); owner.keyDrafts.Remove(Name); foreach (var connection in owner.connections.Where(c => c.Name == Name)) connection.RefreshKey(); owner.Changed(); };
            selector.SelectionChanged += (_, _) => { Cancel(); Update(); };
            owner.fields["ollama"].TextChanged += (_, _) => { if (Name == "Ollama") PlaceKey(); };
            owner.fields["compatible"].TextChanged += (_, _) => { if (Name == "OpenAI compatible") PlaceKey(); };
            test.Click += async (_, _) => await TestAsync();
            Update();
        }

        private void Update()
        {
            foreach (var service in services) service.Value.Visibility = service.Key == Name ? Visibility.Visible : Visibility.Collapsed;
            body.Visibility = Name is "None" or ProviderCatalog.BuiltInDictionary ? Visibility.Collapsed : Visibility.Visible;
            fallbackHint.Visibility = !helper && Name != ProviderCatalog.BuiltInDictionary ? Visibility.Visible : Visibility.Collapsed;
            Ui.SetText(description, Name switch
            {
                "None" => "Доступны обычный перевод слова и импортированные словари.",
                ProviderCatalog.BuiltInDictionary => "Offline English–Russian word dictionary · no setup needed",
                "Ollama" => "Локальные модели. Нужен запущенный Ollama; для сервера на вашем компьютере ключ не требуется.",
                "LibreTranslate" => "Перевод через ваш сервер. Для локального LibreTranslate API-ключ обычно не нужен.",
                "Yandex Translate" => "Официальный API Яндекса. Нужен API-ключ сервисного аккаунта Yandex Cloud.",
                "YandexGPT" => "Контекстный перевод через Yandex AI Studio. Нужны API-ключ и каталог с доступом к модели.",
                "GigaChat" => "Вставьте ключ авторизации из кабинета GigaChat API. Обычная подписка на чат его не заменяет.",
                "OpenAI compatible" => "Подключите LM Studio или другой совместимый сервер. Для облачного API нужен ключ.",
                "Azure" => "Ключ и регион из ресурса Microsoft Azure Translator.",
                _ => "Используйте API-ключ выбранного тарифа DeepL. Доступность сервиса зависит от региона."
            });
            PlaceKey(); RefreshKey(); InvalidateResult();
        }

        private void PlaceKey()
        {
            bool Local(string id) => Uri.TryCreate(owner.fields[id].Text.Trim(), UriKind.Absolute, out var uri) && uri.IsLoopback;
            optionalKey.Content = null; keySlot.Children.Clear();
            body.Children.Remove(keySlot);
            if (Name == "LibreTranslate" || Name == "Ollama" && Local("ollama") || Name == "OpenAI compatible" && Local("compatible")) { optionalKey.Content = keyArea; keySlot.Children.Add(optionalKey); body.Children.Add(keySlot); }
            else { keySlot.Children.Add(keyArea); body.Children.Insert(0, keySlot); }
        }

        public void RefreshKey()
        {
            updatingKey = true;
            try
            {
                key.Password = owner.keyDrafts.GetValueOrDefault(Name, ""); var exists = owner.vm.HasKey(Name);
                Ui.SetText(keyState, owner.removedKeys.Contains(Name) ? "Ключ будет удалён после сохранения." : key.Password.Length > 0 ? "Новый ключ будет сохранён вместе с настройками." : exists ? "Ключ сохранён. Оставьте поле пустым, чтобы использовать его." : "Ключ будет защищён средствами Windows.");
                remove.Visibility = exists || owner.removedKeys.Contains(Name) ? Visibility.Visible : Visibility.Collapsed;
                Ui.SetContent(remove, owner.removedKeys.Contains(Name) ? "Отменить удаление ключа" : "Удалить сохранённый ключ");
            }
            finally { updatingKey = false; }
        }

        public void Cancel() => checking?.Cancel();
        public void InvalidateResult()
        {
            if (result is null) return;
            if (checking is not null) { checking.Cancel(); return; }
            result.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Ui.SetText(result, "Тестовая фраза · без сохранения настроек");
        }

        private async Task TestAsync()
        {
            if (checking is not null) { checking.Cancel(); return; }
            using var cancellation = new CancellationTokenSource(); checking = cancellation; owner.runningChecks++; owner.UpdateActions();
            Ui.SetContent(test, "Отменить проверку"); result.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Ui.SetText(result, "Подключаемся… Первый запуск локальной модели может занять больше времени.");
            try
            {
                var settings = owner.ReadConnectionSettings();
                if (helper) settings = settings with { Provider = settings.AssistantProvider, OllamaModel = settings.AssistantModel };
                settings = settings with { AssistantProvider = "None" };
                var translated = await owner.vm.TestConnectionAsync(settings, owner.keyDrafts.GetValueOrDefault(Name), owner.removedKeys.Contains(Name), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested(); result.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); Ui.SetText(result, L.M("✓ Подключение работает\n{0}", translated));
            }
            catch (OperationCanceledException) { Ui.SetText(result, "Проверка отменена."); }
            catch (Exception error) { result.SetResourceReference(TextBlock.ForegroundProperty, "SettingsErrorBrush"); Ui.Error(result, error, "Не удалось подключиться. Проверьте адрес сервера и доступ к сети."); }
            finally { checking = null; owner.runningChecks--; owner.UpdateActions(); Ui.SetContent(test, "Проверить подключение"); }
        }

        private static string ProviderLabel(string name) => name switch
        {
            ProviderCatalog.BuiltInDictionary => "Built-in dictionary · English → Russian",
            "Yandex Translate" => "Яндекс Переводчик", "YandexGPT" => "YandexGPT · контекстный перевод", "GigaChat" => "GigaChat", "Ollama" => "Ollama · локальные модели", "LibreTranslate" => "LibreTranslate · свой сервер", "OpenAI compatible" => "Совместимый API · LM Studio, DeepSeek и другие", "Azure" => "Microsoft Azure", _ => name
        };
    }
}
