using System.Globalization;
using System.Windows.Data;
using WitchDrawer.App.ViewModels;

namespace WitchDrawer.App.Infrastructure;

public sealed class BoxVisualStyleEqualityConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return values.Length >= 2
            && values[0] is BoxVisualStyle optionStyle
            && values[1] is BoxVisualStyle selectedStyle
            && optionStyle == selectedStyle;
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
