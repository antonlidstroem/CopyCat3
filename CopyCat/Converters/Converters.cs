using System.Globalization;

namespace CopyCat.Converters;

/// <summary>Converts a bool to one of two Color values.</summary>
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

/// <summary>Returns true when the integer value is greater than zero.</summary>
public class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>Converts a bool to one of two string values (e.g. toggle arrow glyphs).</summary>
public class BoolToStringConverter : IValueConverter
{
    public string TrueValue  { get; set; } = string.Empty;
    public string FalseValue { get; set; } = string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Returns true when the string value is non-null and non-empty.
/// Used for the chunk search clear button visibility.
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// FIX B-1: Border.Stroke is typed as Brush, not Color.
/// Runtime bindings bypass TypeConverters, so a Color value silently fails.
/// This converter wraps a Color into a SolidColorBrush so Stroke bindings work correctly.
/// </summary>
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c) return new SolidColorBrush(c);
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Returns true when the string value IS null or empty.
/// Complement of StringNotEmptyConverter. Used for paste-button visibility
/// (show the paste button only when the field is blank).
/// </summary>
public class StringIsEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

