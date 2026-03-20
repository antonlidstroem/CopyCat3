using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Background colour for an INCLUDED (green) chip.
/// true  → active/included teal background
/// false → dim grey background
///
/// Used by FileTypeFilter chips.
/// </summary>
public sealed class ChipIncludedBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#0D2A2B")   // teal tint — included
            : Color.FromArgb("#1A1A24");  // dim — excluded

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Border colour for an INCLUDED (green) chip.
/// true  → teal border
/// false → subtle grey border
/// </summary>
public sealed class ChipIncludedBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#00B4BC")   // AccentPrimary — included
            : Color.FromArgb("#2D2D3A");  // BorderDefault — excluded

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Background colour for an EXCLUDED (red) chip.
/// true  → active/excluded red tint
/// false → dim grey background
///
/// Used by FolderFilter and FilePatternFilter chips.
/// </summary>
public sealed class ChipExcludedBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#2A1515")   // red tint — excluded/active
            : Color.FromArgb("#1A1A24");  // dim — inactive

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Border colour for an EXCLUDED (red) chip.
/// true  → red border
/// false → subtle grey border
/// </summary>
public sealed class ChipExcludedBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#EF4444")   // TextError — excluded/active
            : Color.FromArgb("#2D2D3A");  // BorderDefault — inactive

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Text colour for any chip (both included and excluded families).
/// true  → bright white (active state)
/// false → muted grey (inactive state)
/// </summary>
public sealed class ChipLabelColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#F9FAFB")   // TextPrimary — active
            : Color.FromArgb("#6B7280");  // TextMuted   — inactive

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
