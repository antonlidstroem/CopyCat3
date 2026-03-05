using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Converts a bool to one of two <see cref="Color"/> values.
/// Set TrueColor / FalseColor from StaticResource in XAML.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public Color TrueColor  { get; set; } = Colors.Transparent;
    public Color FalseColor { get; set; } = Colors.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueColor : FalseColor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>Inverts a bool value.</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>
/// Returns true when the integer value is greater than zero.
/// Use for IsVisible bindings on collections.
/// </summary>
public class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts a bool to one of two string values (e.g. toggle arrow glyphs).
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public string TrueValue  { get; set; } = string.Empty;
    public string FalseValue { get; set; } = string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
