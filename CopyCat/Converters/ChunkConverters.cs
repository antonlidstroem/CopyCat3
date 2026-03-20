using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Converts a boolean expand/collapse state to an arrow character.
/// true  → "▲" (expanded / collapse prompt)
/// false → "▼" (collapsed / expand prompt)
///
/// Used on every collapsible card header (file types, folders, patterns,
/// fetched files, chunk Zone C strip).
///
/// XAML usage:
///   Text="{Binding IsFileTypesExpanded, Converter={StaticResource ExpandArrowConv}}"
/// </summary>
public sealed class ExpandArrowConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "▲" : "▼";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shows a short tap-hint label when at least one chunk is selected.
/// true  → "tap to deselect"
/// false → "" (empty — hint only relevant when selection is active)
///
/// XAML usage:
///   Text="{Binding HasSelection, Converter={StaticResource SelectionHintConv}}"
/// </summary>
public sealed class SelectionHintConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "tap card to deselect" : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
