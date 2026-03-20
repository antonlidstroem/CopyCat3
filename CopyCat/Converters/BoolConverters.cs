using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Inverts a boolean value.
/// Used wherever an element should be visible when a binding is <c>false</c>
/// (e.g. show a placeholder when a list is empty).
///
/// XAML usage:
///   IsVisible="{Binding IsBusy, Converter={StaticResource InverseBool}}"
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
