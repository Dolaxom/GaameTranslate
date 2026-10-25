using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GaameTranslate.Core;
using GaameTranslate.Windows;

namespace GaameTranslate.App;

public sealed class WordLookupWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private long version;
    public WordLookupWindow(MainViewModel vm, string word, TranslationSession session, TranslatedBlock block, DictionaryArticle? knownArticle = null)
    {
        var preferences = vm.SettingsForSession(session);
        SetResourceReference(StyleProperty, typeof(Window)); Title = "GaameTranslate · dictionary"; Width = 590; SizeToContent = SizeToContent.Height; MaxHeight = 780; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var root = new StackPanel { Margin = new(24, 18, 24, 22) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(SettingsView.Muted("КОНТЕКСТНЫЙ ПЕРЕВОД"));
        var translatedContext = new TextBlock { Text = block.Translation, FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) }; root.Children.Add(translatedContext);
        root.Children.Add(new Expander { Header = SettingsView.Muted("Оригинальная реплика",new(0)), Content = SettingsView.Muted(block.Original.Text, new(0, 10, 0, 10)), Margin = new(0, 0, 0, 16) });
        var selection = new TextBox { Text = word, FontSize = 28, FontWeight = FontWeights.SemiBold, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent, ToolTip = "Выберите выражение целиком или исправьте OCR" }; root.Children.Add(selection);
        var grammar = SettingsView.Muted("", new(0, 6, 0, 10)); root.Children.Add(grammar);
        var meaning = new TextBlock { Text = "Перевод…", FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 12) }; root.Children.Add(meaning);
        var articlePanel = new StackPanel(); root.Children.Add(articlePanel);
        var feedback = SettingsView.Muted("", new(0, 12, 0, 10)); root.Children.Add(feedback);
        var buttons = new WrapPanel(); root.Children.Add(buttons);
        Button Action(string title) { var button = new Button { Content = title, Margin = new(0, 0, 8, 8), Padding = new(12, 7, 12, 7) }; buttons.Children.Add(button); return button; }
        var refresh = Action("Перевести"); var speak = Action("▶ Озвучить"); var explain = Action("Объяснить в контексте"); var save = Action("☆ Сохранить"); save.Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButton"); save.IsEnabled = false;
        DictionaryArticle? article = null; TranslationItem? result = null; string recognizedWord = word;
        void RenderArticles(IEnumerable<DictionaryArticle> articles)
        {
            articlePanel.Children.Clear(); foreach (var item in articles)
            {
                article ??= item;
                if (!string.IsNullOrWhiteSpace(item.Phonetic + item.PartOfSpeech + item.Level)) grammar.Text = string.Join("  ·  ", new[] { item.Phonetic, item.PartOfSpeech, item.Level }.Where(t => t.Length > 0));
                articlePanel.Children.Add(new TextBlock { Text = item.Definition, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
                void Detail(string title, string text) { if (!string.IsNullOrWhiteSpace(text)) articlePanel.Children.Add(SettingsView.Muted(title + text, new(0, 2, 0, 8))); }
                Detail("Синонимы: ", item.Synonyms); Detail("Пример: ", item.Examples); Detail("", item.Explanation);
                articlePanel.Children.Add(SettingsView.Muted(item.Generated ? item.Source + " · сгенерированное объяснение" : item.Source, new(0, 4, 0, 10)));
            }
        }
        async Task TranslateAsync()
        {
            var current = ++version; if (string.IsNullOrWhiteSpace(selection.Text)) return; recognizedWord = selection.Text.Trim(); var selected = recognizedWord;
            refresh.IsEnabled = false; save.IsEnabled = false; feedback.Text = ""; meaning.Text = "Перевод…"; article = null; result = null; articlePanel.Children.Clear(); grammar.Text = "";
            try
            {
                var local = await Task.Run(() => vm.Dictionary.LookupLocalAsync(selected, lifetime.Token)); if (current != version) return; RenderArticles(local);
                if (preferences.AutoExplainWords && preferences.AssistantProvider != "None")
                {
                    meaning.Text = "Значение в контексте…";
                    var generated = await vm.Dictionary.ExplainAsync(preferences, selected, session.SourceLanguage, session.TargetLanguage, block.Original.Text, lifetime.Token); if (current != version) return;
                    article = generated; RenderArticles(new[] { generated }.Concat(local)); result = new(generated.Translation, session.SourceLanguage); meaning.Text = generated.Translation;
                    if (generated.ContextTranslation.Length > 0) translatedContext.Text = generated.ContextTranslation; save.IsEnabled = true; feedback.Text = "Контекстная статья AI · проверяйте перед сохранением."; return;
                }
                var translated = await vm.LookupAsync(selected, session.SourceLanguage, session.TargetLanguage, block.Original.Text, lifetime.Token, preferences); if (current != version) return; result = translated; meaning.Text = translated.Text; save.IsEnabled = true;
                if (preferences.EnableOnlineDictionary) { var online = await vm.Dictionary.LookupOnlineAsync(selected, session.SourceLanguage, lifetime.Token); if (current != version) return; RenderArticles(local.Concat(online)); }
                if (articlePanel.Children.Count == 0) feedback.Text = "Импортируйте локальный словарь или нажмите «Объяснить в контексте».";
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (current == version) { feedback.Text = e is UserFacingException ? e.Message : "Не удалось подключиться к словарю или переводчику."; if (result is null) meaning.Text = "Перевод недоступен"; } }
            finally { if (current == version) refresh.IsEnabled = true; }
        }
        refresh.Click += async (_, _) => await TranslateAsync(); selection.TextChanged += (_, _) => { version++; save.IsEnabled = false; refresh.IsEnabled = true; };
        explain.Click += async (_, _) =>
        {
            var current = ++version; recognizedWord = selection.Text.Trim(); var selected = recognizedWord; explain.IsEnabled = false; feedback.Text = "Анализ значения в этой реплике…";
            try { var generated = await vm.Dictionary.ExplainAsync(preferences, selected, session.SourceLanguage, session.TargetLanguage, block.Original.Text, lifetime.Token); if (current != version) return; article = generated; RenderArticles([generated]); meaning.Text = generated.Translation; if (generated.ContextTranslation.Length > 0) translatedContext.Text = generated.ContextTranslation; result = new(generated.Translation, session.SourceLanguage); save.IsEnabled = true; feedback.Text = "Объяснение языковой модели · можно проверить и изменить карточку перед экспортом."; }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (current == version) feedback.Text = e is UserFacingException ? e.Message : "Не удалось получить объяснение."; }
            finally { explain.IsEnabled = true; refresh.IsEnabled = true; }
        };
        speak.Click += async (_, _) => { speak.IsEnabled = false; try { await vm.Speech.SpeakAsync(selection.Text, session.SourceLanguage, preferences.SpeechVoice, lifetime.Token); } catch (OperationCanceledException) { } catch (Exception e) { feedback.Text = e is UserFacingException ? e.Message : "Озвучка недоступна."; } finally { speak.IsEnabled = true; } };
        save.Click += async (_, _) =>
        {
            if (result is null) return; save.IsEnabled = false;
            try
            {
                var screenshot = ""; var audio = ""; var mediaFailure = "";
                if (vm.Settings.SaveWordScreenshots && vm.LastSession?.Id == session.Id && vm.LastFrame is { } frame) screenshot = await vm.Media.SaveAsync(Crop(frame, block.Original.Bounds), ".png", lifetime.Token);
                if (vm.Settings.SaveWordAudio) try { audio = await vm.Media.SaveAsync(await vm.Speech.SynthesizeAsync(recognizedWord, session.SourceLanguage, preferences.SpeechVoice, lifetime.Token), ".wav", lifetime.Token); } catch (UserFacingException e) { mediaFailure = " · " + e.Message; }
                var entry = await vm.SaveWordAsync(new() { Word = recognizedWord, Translation = result.Text, SourceLanguage = result.SourceLanguage, TargetLanguage = session.TargetLanguage, Context = block.Original.Text, TranslatedContext = translatedContext.Text, Lemma = article?.Headword ?? "", Phonetic = article?.Phonetic ?? "", PartOfSpeech = article?.PartOfSpeech ?? "", Level = article?.Level ?? "", Definition = article?.Definition ?? "", Synonyms = article?.Synonyms ?? "", Examples = article?.Examples ?? "", Explanation = article?.Explanation ?? "", DictionarySource = article?.Source ?? "", ScreenshotFile = screenshot, AudioFile = audio });
                save.Content = "★ Сохранено"; feedback.Text = (entry.Encounters > 1 ? "Слово уже в словаре · счётчик встреч обновлён" : "Слово добавлено в словарь") + mediaFailure;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { feedback.Text = e is UserFacingException ? e.Message : "Не удалось сохранить карточку."; save.IsEnabled = true; }
        };
        Loaded += async (_, _) => { PositionNearWord(block); if (knownArticle is null) await TranslateAsync(); else { article = knownArticle; RenderArticles([knownArticle]); meaning.Text = knownArticle.Translation; result = new(knownArticle.Translation, session.SourceLanguage); save.IsEnabled = true; } }; Closed += (_, _) => { lifetime.Cancel(); vm.Speech.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; NativeMethods.StyleWindow(hwnd, !preferences.LightTheme); NativeMethods.SetWindowDisplayAffinity(hwnd, 0x11); };
    }
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
