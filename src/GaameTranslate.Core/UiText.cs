using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace GaameTranslate.Core;

/// <summary>A resource key with unevaluated arguments, so an open view can change language without losing state.</summary>
public sealed record UiMessage(string Key, params object?[] Arguments)
{
    public string Text => UiText.Format(Key, Arguments);
    public int Length => Text.Length;
    public bool StartsWith(string value, StringComparison comparison = StringComparison.Ordinal) => Text.StartsWith(value, comparison);
    public override string ToString() => Text;
    public static implicit operator UiMessage(string key) => new(key);
}

public static class UiText
{
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> catalogue = Load();
    public static LanguageState State { get; } = new();
    public static string Language => State.Language;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "ru" ? "ru-RU" : "en-US");
    public static IReadOnlyDictionary<string, Dictionary<string, string>> Catalogue => catalogue;
    public static UiMessage M(string key, params object?[] arguments) => new(key, arguments);
    public static object Join(string separator, IEnumerable<UiMessage> messages) => new MessageList(separator, messages.ToArray());
    private sealed record MessageList(string Separator, UiMessage[] Messages) { public override string ToString() => string.Join(Separator, Messages.Select(m => m.Text)); }
    public static string Get(string key) => Get(key, Language);
    public static string Get(string key, string language) => catalogue.TryGetValue(key, out var entry) && entry.TryGetValue(language, out var value) ? value : key;
    public static string Format(string key, params object?[] arguments) => arguments.Length == 0 ? Get(key) : string.Format(Culture, Get(key), arguments.Select(a => a is UiMessage message ? message.Text : a).ToArray());
    public static void SetLanguage(string language)
    {
        if (language is not ("ru" or "en")) throw new ArgumentOutOfRangeException(nameof(language));
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        State.Set(language);
    }
    private static IReadOnlyDictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("GaameTranslate.Core.UiStrings.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    }
    public sealed class LanguageState : INotifyPropertyChanged
    {
        public string Language { get; private set; } = "en";
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Set(string value) { if (Language == value) return; Language = value; PropertyChanged?.Invoke(this, new(nameof(Language))); }
    }
}
