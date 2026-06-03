using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Replicator.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is bool boolValue && boolValue;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility visibility && visibility == Visibility.Visible;
        if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible;
    }
}
