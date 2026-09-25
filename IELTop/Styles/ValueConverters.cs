using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace IELTop.Styles;

/// <summary>Shows the element when the bound value is true.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>
/// Matches the current page against ConverterParameter to switch panels.
/// Invert=true shows the panel on every page except the named one.
/// </summary>
public sealed class StringMatchToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool match = string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);
        if (Invert) match = !match;
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Hides the element when the bound string is empty.</summary>
public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shows the panel only when the current page is not in a pipe separated list.
/// Used so shared placeholder content hides on every dedicated page.
/// </summary>
public sealed class PageNotInListToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var page = value as string ?? string.Empty;
        var pages = (parameter as string ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool listed = pages.Any(p => string.Equals(p, page, StringComparison.OrdinalIgnoreCase));
        return listed ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
