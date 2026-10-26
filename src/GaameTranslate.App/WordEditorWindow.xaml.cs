using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using GaameTranslate.Core;
using GaameTranslate.Windows;

namespace GaameTranslate.App;

public partial class WordEditorWindow : Window
{
    private readonly MainViewModel vm;
    private readonly VocabularyEntry? original;
    private readonly Dictionary<string, TextBox> extras = [];
    private bool saving, hasSaved;
    public VocabularyEntry? SavedEntry { get; private set; }
    public WordEditorWindow(MainViewModel model, VocabularyEntry? entry = null)
    {
        vm = model; original = entry; InitializeComponent();
        var source = entry?.SourceLanguage ?? vm.Settings.SourceLanguage;
        var target = entry?.TargetLanguage ?? vm.Settings.TargetLanguage;
        // Existing imported language tags are retained even when they are not in the translation catalogue.
        Source.ItemsSource = Choices(source); Target.ItemsSource = Choices(target);
        Source.SelectedValue = source is "auto" or "" ? null : source; Target.SelectedValue = target;
        Word.Text = entry?.Word ?? ""; Meaning.Text = entry?.Translation ?? "";
        OriginalContext.Text = entry?.Context ?? ""; TranslatedContext.Text = entry?.TranslatedContext ?? ""; Notes.Text = entry?.Notes ?? ""; Tags.Text = entry?.Tags ?? "";
        Field("phonetic", "Произношение", entry?.Phonetic); Field("grammar", "Часть речи", entry?.PartOfSpeech); Field("definition", "Определение", entry?.Definition, true); Field("examples", "Примеры", entry?.Examples, true); Field("explanation", "Объяснение", entry?.Explanation, true);
        ContextSection.IsExpanded = entry is not null && (!string.IsNullOrWhiteSpace(entry.Context) || !string.IsNullOrWhiteSpace(entry.TranslatedContext));
        if (entry is not null) { Ui.Set(this, Window.TitleProperty, "Редактировать карточку"); Ui.SetText(Heading, "Редактировать карточку"); Ui.SetContent(Save, "Сохранить изменения"); }
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 30); MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 30);
        Loaded += (_, _) => { Word.Focus(); Word.CaretIndex = Word.Text.Length; };
        SourceInitialized += (_, _) => NativeMethods.StyleWindow(new WindowInteropHelper(this).Handle, !vm.Settings.LightTheme);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !saving) { e.Handled = true; Close(); } else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; if (!saving) Save_Click(this, new()); } };
        Closing += (_, e) => { if (saving) e.Cancel = true; };
        Source.SelectionChanged += (_, _) => ClearFeedback(); Target.SelectionChanged += (_, _) => ClearFeedback(); Meaning.TextChanged += (_, _) => ClearFeedback();
    }
    private static LanguageChoice[] Choices(string code) => Languages.Translation.Concat(code is not ("" or "auto") && Languages.Translation.All(l => l.Code != code) ? [new LanguageChoice(code, code)] : Array.Empty<LanguageChoice>()).ToArray();
    private void Field(string id, string label, string? text, bool multiline = false)
    {
        ExtraFields.Children.Add(Ui.Localize(new TextBlock { Text = label, Style = (Style)FindResource("FieldLabel"), Margin = new(0, 12, 0, 7) }));
        var input = new TextBox { Text = text ?? "", AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 40, MaxHeight = multiline ? 200 : double.PositiveInfinity, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Ui.Set(input, AutomationProperties.NameProperty, new(label)); AutomationProperties.SetAutomationId(input, "word-editor-" + id); extras[id] = input; ExtraFields.Children.Add(input);
    }
    private void Word_Changed(object sender, TextChangedEventArgs e) { if (Feedback is not null) ClearFeedback(); }
    private void ClearFeedback() { Feedback.Visibility = Visibility.Collapsed; Word.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); Meaning.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); Source.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); Target.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); }
    private bool Invalid(Control field, string message)
    {
        Ui.SetText(Feedback, message); Feedback.Visibility = Visibility.Visible; field.SetResourceReference(Control.BorderBrushProperty, "SettingsErrorBrush"); field.BringIntoView(); field.Focus(); return false;
    }
    private bool Validate()
    {
        ClearFeedback();
        if (string.IsNullOrWhiteSpace(Word.Text)) return Invalid(Word, "Введите слово или выражение.");
        if (string.IsNullOrWhiteSpace(Meaning.Text)) return Invalid(Meaning, "Добавьте перевод слова.");
        if (Source.SelectedItem is not LanguageChoice) return Invalid(Source, "Выберите язык слова — он нужен для озвучки и повторения.");
        if (Target.SelectedItem is not LanguageChoice) return Invalid(Target, "Выберите язык перевода.");
        return true;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (saving || hasSaved || !Validate()) return;
        var next = (original ?? new VocabularyEntry()) with
        {
            Word = Word.Text.Trim(), Translation = Meaning.Text.Trim(), SourceLanguage = ((LanguageChoice)Source.SelectedItem).Code, TargetLanguage = ((LanguageChoice)Target.SelectedItem).Code,
            Context = OriginalContext.Text, TranslatedContext = TranslatedContext.Text, Notes = Notes.Text, Tags = Tags.Text,
            Phonetic = extras["phonetic"].Text, PartOfSpeech = extras["grammar"].Text, Definition = extras["definition"].Text, Examples = extras["examples"].Text, Explanation = extras["explanation"].Text
        };
        saving = true; Form.IsEnabled = false; Save.IsEnabled = Cancel.IsEnabled = false; Ui.SetContent(Save, "Сохранение…");
        try
        {
            if (original is null) SavedEntry = await vm.SaveWordAsync(next); else { await vm.UpdateWordAsync(next); SavedEntry = next; }
            vm.SelectedWord = vm.Words.FirstOrDefault(w => w.Id == SavedEntry.Id); hasSaved = true; saving = false; Close();
        }
        catch (Exception error)
        {
            Ui.SetText(Feedback, error is UserFacingException known ? known.LocalizedMessage.Key == "That word is already saved for this language pair."
                ? new UiMessage("Такая карточка уже есть для этих языков. Измените слово или отредактируйте существующую карточку.") : known.LocalizedMessage
                : new UiMessage("Не удалось сохранить карточку. Проверьте свободное место и попробуйте ещё раз."));
            Feedback.Visibility = Visibility.Visible;
        }
        finally { saving = false; Form.IsEnabled = true; Save.IsEnabled = Cancel.IsEnabled = true; Ui.SetContent(Save, original is null ? "Добавить слово" : "Сохранить изменения"); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
