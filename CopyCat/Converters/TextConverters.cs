using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Returns <c>true</c> when a string is non-null and non-empty.
/// Used to show/hide clear buttons and search result labels.
///
/// XAML usage:
///   IsVisible="{Binding ChunkSearchText, Converter={StaticResource StringNotEmptyConv}}"
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
