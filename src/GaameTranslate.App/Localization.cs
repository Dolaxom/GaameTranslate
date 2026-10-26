using System.Globalization;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Markup;
using GaameTranslate.Core;

namespace GaameTranslate.App;

[MarkupExtensionReturnType(typeof(object))]
[XamlSetMarkupExtension(nameof(ReceiveMarkupExtension))]
public sealed class LocExtension : MarkupExtension
{
    public object Key { get; set; } = "";
    public BindingBase? Value { get; set; }
    public string? Fallback { get; set; }
    public LocExtension() { }
    public LocExtension(object key) => Key = key;
    public static void ReceiveMarkupExtension(object target, XamlSetMarkupExtensionEventArgs args)
    {
        if (target is not LocExtension extension || args.MarkupExtension is not BindingBase binding) return;
        if (args.Member.Name == nameof(Key)) extension.Key = binding;
        else if (args.Member.Name == nameof(Value)) extension.Value = binding;
        else return;
        args.Handled = true;
    }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target && target.TargetObject is not DependencyObject) return this;
        var key = Key is string text && text.StartsWith("{}", StringComparison.Ordinal) ? text[2..] : Key;
        return Ui.Binding(key, Value, Fallback).ProvideValue(serviceProvider);
    }
}

public static class Ui
{
    public static MultiBinding Binding(object key, BindingBase? value = null, string? fallback = null)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = new TextConverter(value is not null, fallback) };
        binding.Bindings.Add(key as BindingBase ?? new Binding { Source = key });
        binding.Bindings.Add(new Binding(nameof(UiText.LanguageState.Language)) { Source = UiText.State });
        if (value is not null) binding.Bindings.Add(value);
        return binding;
    }
    public static void Set(DependencyObject target, DependencyProperty property, UiMessage message) => BindingOperations.SetBinding(target, property, Binding(message));
    public static void SetText(TextBlock target, UiMessage message) => Set(target, TextBlock.TextProperty, message);
    public static void SetContent(ContentControl target, UiMessage message) => Set(target, ContentControl.ContentProperty, message);
    public static void Error(TextBlock target, Exception error, string fallback) => SetText(target, error is UserFacingException known ? known.LocalizedMessage : new(fallback));
    public static T Localize<T>(T target, UiMessage? message = null) where T : DependencyObject
    {
        if (target is TextBlock text) SetText(text, message ?? new(text.Text));
        else if (target is Window window) Set(window, Window.TitleProperty, message ?? new(window.Title));
        else if (target is HeaderedContentControl header) Set(header, HeaderedContentControl.HeaderProperty, message ?? new(header.Header?.ToString() ?? ""));
        else if (target is HeaderedItemsControl menu) Set(menu, HeaderedItemsControl.HeaderProperty, message ?? new(menu.Header?.ToString() ?? ""));
        else if (target is ContentControl content) SetContent(content, message ?? new(content.Content?.ToString() ?? ""));
        if (target is FrameworkElement { ToolTip: string tip } element) Set(element, FrameworkElement.ToolTipProperty, new(tip));
        var name = AutomationProperties.GetName(target); if (name.Length > 0) Set(target, AutomationProperties.NameProperty, new(name));
        return target;
    }
    public static TextBlock Text(UiMessage message) { var text = new TextBlock(); SetText(text, message); return text; }
    private sealed class TextConverter(bool hasValue, string? fallback) : IMultiValueConverter
    {
        public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
        {
            if (values.Length == 0 || values[0] == DependencyProperty.UnsetValue) return "";
            var key = values[0] as UiMessage ?? new UiMessage(values[0]?.ToString() ?? "");
            if (!hasValue) return key.Text;
            var value = values.Length > 2 ? values[2] : null;
            if (fallback is not null) return value is null || value == DependencyProperty.UnsetValue ? UiText.Get(fallback) : value;
            return UiText.Format(key.Key, value == DependencyProperty.UnsetValue ? null : value);
        }
        public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
