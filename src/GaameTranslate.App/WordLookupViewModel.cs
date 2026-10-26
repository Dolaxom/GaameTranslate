using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using GaameTranslate.Core;

namespace GaameTranslate.App;

/// <summary>Word first; context and dictionary enrichment can never delay or erase that first result.</summary>
public sealed partial class WordLookupViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel vm;
    private readonly TranslationSession session;
    private readonly TranslatedBlock block;
    private readonly AppSettings preferences;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? active;
    private long version;
    private bool disposed;
    private IReadOnlyList<DictionaryArticle> local = [], online = [];
    private DictionaryArticle? generated;
    [ObservableProperty] private string word = "";
    [ObservableProperty] private string translation = "";
    [ObservableProperty] private string basicTranslation = "";
    [ObservableProperty] private UiMessage translationSource = "";
    [ObservableProperty] private string contextTranslation = "";
    [ObservableProperty] private UiMessage wordStatus = "";
    [ObservableProperty] private UiMessage contextStatus = "";
    [ObservableProperty] private UiMessage explanationStatus = "";
    [ObservableProperty] private bool isLoadingWord;
    [ObservableProperty] private bool isLoadingContext;
    [ObservableProperty] private bool isLoadingExplanation;
    public string ResultLanguage { get; private set; }
    public DictionaryArticle? Article => generated ?? local.FirstOrDefault() ?? online.FirstOrDefault();
    public IReadOnlyList<DictionaryArticle> Articles => (generated is null ? Enumerable.Empty<DictionaryArticle>() : [generated]).Concat(local).Concat(online).ToArray();
    public bool CanSave => !string.IsNullOrWhiteSpace(Translation);
    public bool HasInitialArticle { get; }
    public event Action? ArticlesChanged;
    public WordLookupViewModel(MainViewModel model, string selected, TranslationSession origin, TranslatedBlock original, DictionaryArticle? knownArticle = null)
    {
        vm = model; session = origin; block = original; preferences = vm.SettingsForSession(origin); ResultLanguage = origin.SourceLanguage;
        Word = selected; ContextTranslation = original.Translation;
        if (knownArticle is not null)
        {
            HasInitialArticle = true; generated = knownArticle; Translation = BasicTranslation = knownArticle.Translation; TranslationSource = L.M("{0}", knownArticle.Source);
            if (!string.IsNullOrWhiteSpace(knownArticle.ContextTranslation)) ContextTranslation = knownArticle.ContextTranslation;
        }
    }
    partial void OnTranslationChanged(string value) => OnPropertyChanged(nameof(CanSave));
    partial void OnWordChanged(string value)
    {
        version++; active?.Cancel(); Translation = BasicTranslation = ""; TranslationSource = WordStatus = ContextStatus = ExplanationStatus = "";
        IsLoadingWord = IsLoadingContext = IsLoadingExplanation = false; generated = null; local = online = []; ArticlesChanged?.Invoke();
    }
    public async Task LoadAsync(bool forceExplanation = false)
    {
        if (disposed || string.IsNullOrWhiteSpace(Word)) return;
        active?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); active = request; var current = ++version; var token = request.Token; var selected = Word.Trim();
        bool Current() => current == version && !token.IsCancellationRequested;
        WordStatus = ""; ContextStatus = ""; ExplanationStatus = ""; IsLoadingWord = true;
        var started = Stopwatch.StartNew();
        // Local dictionary I/O is independent from the single-word request, including failures.
        var localTask = LoadLocalAsync();
        try
        {
            try
            {
                var quick = await vm.LookupWordAsync(selected, session.SourceLanguage, session.TargetLanguage, preferences, token);
                if (!Current()) return;
                BasicTranslation = quick.Item.Text; Translation = quick.Item.Text; ResultLanguage = quick.Item.SourceLanguage;
                TranslationSource = quick.Saved ? "Сохранённый перевод" : L.M("Быстрый перевод · {0}", preferences.Provider);
                vm.RecordWordLookupReady(session, started.Elapsed.TotalMilliseconds);
                WordStatus = "";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { if (Current()) WordStatus = Message(error, "Быстрый перевод недоступен. Пробуем получить значение из контекста."); }
            finally { if (Current()) IsLoadingWord = false; }
            if (!Current()) return;
            // Begin expensive work only after the word request has completed, so it has priority on local servers.
            var contextTask = LoadContextAsync(); var explanationTask = LoadExplanationAsync(); var onlineTask = LoadOnlineAsync();
            await Task.WhenAll(contextTask, explanationTask, onlineTask, localTask);
            if (Current() && ContextTranslation.Length > 0) await vm.CompleteWordLookupContextAsync(session, ContextTranslation, started.Elapsed.TotalMilliseconds, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (Current()) ContextStatus = Message(error, "Перевод показан, но не удалось обновить историю."); }
        finally
        {
            // Always observe the independently running dictionary task, also when a request is superseded.
            await localTask;
            if (Current()) IsLoadingWord = IsLoadingContext = IsLoadingExplanation = false;
            if (ReferenceEquals(active, request)) active = null;
        }

        async Task LoadLocalAsync()
        {
            try { var articles = await Task.Run(() => vm.Dictionary.LookupLocalAsync(selected, token), token); if (Current()) { local = articles; ArticlesChanged?.Invoke(); } }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { /* A missing or locked optional dictionary must not hide the translation. */ }
        }
        async Task LoadContextAsync()
        {
            if (!string.IsNullOrWhiteSpace(ContextTranslation)) return;
            IsLoadingContext = true;
            try
            {
                var translated = await vm.LookupAsync(block.Original.Text, session.SourceLanguage, session.TargetLanguage, preferences.ContextHint, token, preferences);
                if (Current() && string.IsNullOrWhiteSpace(generated?.ContextTranslation)) ContextTranslation = translated.Text;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { if (Current() && ContextTranslation.Length == 0) ContextStatus = Message(error, "Не удалось перевести реплику. Перевод слова остаётся доступен."); }
            finally { if (Current()) IsLoadingContext = false; }
        }
        async Task LoadExplanationAsync()
        {
            if ((!preferences.AutoExplainWords && !forceExplanation) || preferences.AssistantProvider == "None") return;
            IsLoadingExplanation = true;
            try
            {
                var article = await vm.Dictionary.ExplainAsync(preferences, selected, session.SourceLanguage, session.TargetLanguage, block.Original.Text, token);
                if (!Current()) return;
                generated = article; Translation = article.Translation; TranslationSource = L.M("Значение в этой реплике · {0}", preferences.AssistantProvider);
                if (!string.IsNullOrWhiteSpace(article.ContextTranslation)) { ContextTranslation = article.ContextTranslation; ContextStatus = ""; }
                WordStatus = ""; ArticlesChanged?.Invoke();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { if (Current()) ExplanationStatus = Message(error, "Уточнение недоступно. Быстрый перевод остаётся доступен."); }
            finally { if (Current()) IsLoadingExplanation = false; }
        }
        async Task LoadOnlineAsync()
        {
            if (!preferences.EnableOnlineDictionary) return;
            try { var articles = await vm.Dictionary.LookupOnlineAsync(selected, session.SourceLanguage, token); if (Current()) { online = articles; ArticlesChanged?.Invoke(); } }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { /* Online definitions supplement the first result; they are not required for saving. */ }
        }
    }
    private static UiMessage Message(Exception error, string fallback) => error is UserFacingException known ? known.LocalizedMessage : new(fallback);
    public void Dispose() { if (disposed) return; disposed = true; version++; lifetime.Cancel(); active?.Cancel(); lifetime.Dispose(); }
}
