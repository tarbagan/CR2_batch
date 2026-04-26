using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoBatchStudio.App.ViewModels;

public sealed class BooleanToVisibilityExConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is bool flag && flag;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
