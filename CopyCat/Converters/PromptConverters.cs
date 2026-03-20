using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Border colour for a prompt card.
/// true  (IsModifiedOrCustom) → amber border (user-edited or custom prompt)
/// false (pristine built-in)  → teal border
///
/// XAML usage (PromptsPage):
///   Stroke="{Binding IsModifiedOrCustom, Converter={StaticResource PromptBorderConv}}"
///
/// Also used on the compact AI PROMPT card in the results panel:
///   Stroke="{Binding HasSelectedPrompt, Converter={StaticResource PromptBorderConv}}"
/// </summary>
public sealed class PromptBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#F59E0B")   // Amber  — modified/custom or active
            : Color.FromArgb("#00B4BC");  // Teal   — pristine built-in or inactive

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Background colour for the circular selection dot on prompt and chunk cards.
/// true  (selected) → filled teal
/// false            → transparent
/// </summary>
public sealed class SelectionDotBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#00B4BC")  // AccentPrimary — selected
            : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Border colour for the circular selection dot.
/// true  (selected) → teal
/// false            → subtle grey ring
/// </summary>
public sealed class SelectionDotBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb("#00B4BC")   // AccentPrimary — selected
            : Color.FromArgb("#3D3D50");  // dim ring — unselected

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
