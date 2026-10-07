using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using WitchDrawer.Core.Localization;

namespace WitchDrawer.App.Localization;

/// <summary>A live translation binding, optionally formatting a bound value or supplying null text.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = key;
    public BindingBase? Value { get; set; }
    public bool Fallback { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var translation = new Binding($"[{Key}]")
        {
            Source = LocalizationProvider.Instance,
            Mode = BindingMode.OneWay
        };
        if (Value is null) return translation.ProvideValue(serviceProvider);
        var binding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = new LocalizedValueConverter(Fallback)
        };
        binding.Bindings.Add(translation);
        binding.Bindings.Add(Value);
        return binding.ProvideValue(serviceProvider);
    }
}

internal sealed class LocalizedValueConverter(bool fallback) : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 2 || values[0] is not string text) return DependencyProperty.UnsetValue;
        var value = values[1];
        if (value is null || value == DependencyProperty.UnsetValue) return fallback ? text : string.Empty;
        return fallback ? value : string.Format(Strings.Culture, text, value);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
