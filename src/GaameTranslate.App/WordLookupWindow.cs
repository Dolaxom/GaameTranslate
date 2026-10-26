using System.Windows.Automation;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GaameTranslate.Core;
using GaameTranslate.Windows;

namespace GaameTranslate.App;

public sealed class WordLookupWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    public WordLookupWindow(MainViewModel vm, string word, TranslationSession session, TranslatedBlock block, DictionaryArticle? knownArticle = null)
    {
        var preferences = vm.SettingsForSession(session);
        var model = new WordLookupViewModel(vm, word, session, block, knownArticle); DataContext = model;
        SetResourceReference(StyleProperty, typeof(Window)); Ui.Set(this, Window.TitleProperty, "GaameTranslate · dictionary"); Width = 590; SizeToContent = SizeToContent.Height; MaxHeight = 780; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var root = new StackPanel { Margin = new(24, 18, 24, 22) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(SettingsView.Muted("СЛОВО ПОД КУРСОРОМ"));
        var selection = new TextBox { FontSize = 28, FontWeight = FontWeights.SemiBold, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent, ToolTip = "Выберите выражение целиком или исправьте OCR" }; root.Children.Add(selection); Ui.Localize(selection);
        selection.SetBinding(TextBox.TextProperty, new Binding(nameof(model.Word)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); Identify(selection, "lookup-word");
        var grammar = SettingsView.Muted("", new(0, 6, 0, 10)); root.Children.Add(grammar);
        var meaning = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) }; root.Children.Add(meaning); Identify(meaning, "lookup-translation"); AutomationProperties.SetLiveSetting(meaning, AutomationLiveSetting.Polite);
        var origin = SettingsView.Muted("", new(0, 0, 0, 8)); System.Windows.Data.BindingOperations.SetBinding(origin, TextBlock.TextProperty, Ui.Binding(new Binding(nameof(model.TranslationSource)))); root.Children.Add(origin);
        if (model.DictionaryNotice.Length > 0)
        {
            var notice = SettingsView.Muted(model.DictionaryNotice, new(0, 8, 0, 12)); Identify(notice, "lookup-dictionary-notice"); root.Children.Add(notice);
            var setup = Ui.Localize(new Button { Content = "Set up translation", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 12) });
            setup.Click += (_, _) => { vm.OpenSettingsCommand.Execute(null); Close(); }; root.Children.Add(setup);
        }
        var basic = SettingsView.Muted("", new(0, 0, 0, 8)); root.Children.Add(basic);
        var wordState = SettingsView.Muted("", new(0, 0, 0, 8)); root.Children.Add(wordState); Identify(wordState, "lookup-word-status");
        var explanationState = SettingsView.Muted("", new(0, 0, 0, 12)); root.Children.Add(explanationState); Identify(explanationState, "lookup-explanation-status");
        root.Children.Add(SettingsView.Muted("КОНТЕКСТ ИГРЫ", new(0, 14, 0, 8)));
        var translatedContext = new TextBlock { FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; root.Children.Add(translatedContext); Identify(translatedContext, "lookup-context");
        var contextState = SettingsView.Muted("", new(0, 0, 0, 8)); root.Children.Add(contextState);
        root.Children.Add(new Expander { Header = SettingsView.Muted("Оригинальная реплика", new(0)), Content = SettingsView.Muted(L.M("{0}", block.Original.Text), new(0, 10, 0, 10)), Margin = new(0, 0, 0, 16) });
        var articlePanel = new StackPanel(); root.Children.Add(articlePanel);
        var feedback = SettingsView.Muted("", new(0, 12, 0, 10)); root.Children.Add(feedback);
        var buttons = new WrapPanel(); root.Children.Add(buttons);
        Button Action(string title) { var button = Ui.Localize(new Button { Content = title, Margin = new(0, 0, 8, 8), Padding = new(12, 7, 12, 7) }); buttons.Children.Add(button); return button; }
        var refresh = Action("Перевести"); var speak = Action("▶ Озвучить"); var explain = Action("Объяснить в контексте"); var save = Action("☆ Сохранить");
        save.Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton"); Identify(refresh, "lookup-refresh"); Identify(explain, "lookup-explain"); Identify(save, "lookup-save");
        bool saving = false; string savedSignature = "";
        string Signature() => JsonSerializer.Serialize(new { model.Word, model.Translation, model.ContextTranslation, model.Article });
        void UpdateState()
        {
            if (model.Translation.Length > 0) meaning.Text = model.Translation; else Ui.SetText(meaning, model.IsLoadingWord || model.WordStatus.Length == 0 ? "Перевод слова…" : "Перевод пока недоступен");
            Ui.SetText(basic, model.BasicTranslation.Length > 0 && model.BasicTranslation != model.Translation ? L.M("Первый перевод: {0}", model.BasicTranslation) : new UiMessage(""));
            basic.Visibility = basic.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Ui.SetText(wordState, model.WordStatus); wordState.Visibility = wordState.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Ui.SetText(explanationState, model.IsLoadingExplanation ? new UiMessage("Уточняем значение в реплике…") : model.ExplanationStatus);
            explanationState.Visibility = explanationState.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (model.ContextTranslation.Length > 0) translatedContext.Text = model.ContextTranslation; else Ui.SetText(translatedContext, model.IsLoadingContext ? "Переводим реплику…" : "Перевод реплики появится после перевода слова.");
            Ui.SetText(contextState, model.ContextStatus); contextState.Visibility = contextState.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            refresh.IsEnabled = !model.IsLoadingWord; explain.IsEnabled = !model.IsLoadingWord && !model.IsLoadingExplanation && preferences.AssistantProvider != "None";
            save.IsEnabled = model.CanSave && !saving && Signature() != savedSignature;
            Ui.SetContent(save, model.CanSave && Signature() == savedSignature ? "★ Сохранено" : "☆ Сохранить");
        }
        void RenderArticles()
        {
            articlePanel.Children.Clear(); Ui.SetText(grammar, "");
            foreach (var item in model.Articles)
            {
                if (grammar.Text.Length == 0 && !string.IsNullOrWhiteSpace(item.Phonetic + item.PartOfSpeech + item.Level)) grammar.Text = string.Join("  ·  ", new[] { item.Phonetic, item.PartOfSpeech, item.Level }.Where(t => t.Length > 0));
                articlePanel.Children.Add(new TextBlock { Text = item.Definition, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
                void Detail(string title, string text) { if (!string.IsNullOrWhiteSpace(text)) articlePanel.Children.Add(SettingsView.Muted(L.M("{0}{1}", new UiMessage(title), text), new(0, 2, 0, 8))); }
                Detail("Синонимы: ", item.Synonyms); Detail("Пример: ", item.Examples); Detail("", item.Explanation);
                articlePanel.Children.Add(SettingsView.Muted(item.Generated ? L.M("{0} · сгенерированное объяснение", item.Source) : L.M("{0}", item.Source), new(0, 4, 0, 10)));
            }
        }
        model.PropertyChanged += (_, _) => UpdateState(); model.ArticlesChanged += () => { RenderArticles(); UpdateState(); };
        refresh.Click += async (_, _) => { Ui.SetText(feedback, ""); await model.LoadAsync(); };
        explain.Click += async (_, _) => { Ui.SetText(feedback, ""); await model.LoadAsync(true); };
        speak.Click += async (_, _) => { speak.IsEnabled = false; try { await vm.Speech.SpeakAsync(model.Word, session.SourceLanguage, preferences.SpeechVoice, lifetime.Token); } catch (OperationCanceledException) { } catch (Exception error) { Ui.Error(feedback, error, "Озвучка недоступна."); } finally { speak.IsEnabled = true; } };
        save.Click += async (_, _) =>
        {
            if (!model.CanSave || saving) return;
            var signature = Signature(); var article = model.Article;
            // Snapshot the visible result before optional media or background refinement completes.
            var draft = new VocabularyEntry { Word = model.Word.Trim(), Translation = model.Translation, SourceLanguage = model.ResultLanguage, TargetLanguage = session.TargetLanguage, Context = block.Original.Text, TranslatedContext = model.ContextTranslation, Lemma = article?.Headword ?? "", Phonetic = article?.Phonetic ?? "", PartOfSpeech = article?.PartOfSpeech ?? "", Level = article?.Level ?? "", Definition = article?.Definition ?? "", Synonyms = article?.Synonyms ?? "", Examples = article?.Examples ?? "", Explanation = article?.Explanation ?? "", DictionarySource = article?.Source ?? "" };
            saving = true; UpdateState();
            try
            {
                var screenshot = ""; var audio = ""; UiMessage mediaFailure = "";
                if (vm.Settings.SaveWordScreenshots && vm.LastSession?.Id == session.Id && vm.LastFrame is { } frame) screenshot = await vm.Media.SaveAsync(Crop(frame, block.Original.Bounds), ".png", lifetime.Token);
                if (vm.Settings.SaveWordAudio) try { audio = await vm.Media.SaveAsync(await vm.Speech.SynthesizeAsync(draft.Word, session.SourceLanguage, preferences.SpeechVoice, lifetime.Token), ".wav", lifetime.Token); } catch (UserFacingException error) { mediaFailure = L.M(" · {0}", error.LocalizedMessage); }
                var entry = await vm.SaveWordAsync(draft with { ScreenshotFile = screenshot, AudioFile = audio });
                savedSignature = signature; Ui.SetText(feedback, L.M("{0}{1}", new UiMessage(entry.Encounters > 1 ? "Слово уже в словаре · счётчик встреч обновлён" : "Слово добавлено в словарь"), mediaFailure));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Ui.Error(feedback, error, "Не удалось сохранить карточку."); }
            finally { saving = false; UpdateState(); }
        };
        RenderArticles(); UpdateState();
        Loaded += async (_, _) => { PositionNearWord(block); if (!model.HasInitialArticle) await model.LoadAsync(); };
        Closed += (_, _) => { model.Dispose(); lifetime.Cancel(); vm.Speech.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; NativeMethods.StyleWindow(hwnd, !preferences.LightTheme); NativeMethods.SetWindowDisplayAffinity(hwnd, 0x11); };
    }
    private static void Identify(DependencyObject control, string id) => AutomationProperties.SetAutomationId(control, id);
    private static byte[] Crop(CapturedFrame frame, PixelRect bounds)
    {
        var rect = new PixelRect(bounds.X - 12, bounds.Y - 10, bounds.Width + 24, bounds.Height + 20).Intersect(frame.Bounds);
        var x = Math.Clamp((int)(rect.X - frame.Bounds.X), 0, frame.Width - 1); var y = Math.Clamp((int)(rect.Y - frame.Bounds.Y), 0, frame.Height - 1);
        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
        var crop = new CroppedBitmap(source, new Int32Rect(x, y, Math.Min((int)rect.Width, frame.Width - x), Math.Min((int)rect.Height, frame.Height - y)));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private void PositionNearWord(TranslatedBlock block)
    {
        var display = DesktopCapture.Displays.OrderByDescending(d => { var r = d.Bounds.Intersect(block.Original.Bounds); return r.Width * r.Height; }).First(); var hwnd = new WindowInteropHelper(this).Handle; var dpi = NativeMethods.GetDpiForWindow(hwnd) / 96d;
        var width = Math.Min(display.WorkArea.Width - 24, ActualWidth * dpi); var height = Math.Min(display.WorkArea.Height - 24, ActualHeight * dpi); var x = Math.Clamp(block.Original.Bounds.X, display.WorkArea.X + 12, Math.Max(display.WorkArea.X + 12, display.WorkArea.Right - width - 12)); var y = block.Original.Bounds.Y - height - 12;
        if (y < display.WorkArea.Y + 12) y = Math.Min(block.Original.Bounds.Bottom + 12, display.WorkArea.Bottom - height - 12);
        NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), (int)x, (int)Math.Max(display.WorkArea.Y + 12, y), (int)width, (int)height, 0x10);
    }
}
