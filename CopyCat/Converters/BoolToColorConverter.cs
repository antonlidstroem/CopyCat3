using System.Globalization;

namespace CopyCat.Converters;

/// <summary>
/// Converts a boolean to one of two <see cref="Color"/> values.
///
/// This converter is the engine behind every chip, selection-dot and
/// card-border colour in CopyCat.  All per-state colours are declared
/// once in Styles.xaml as <see cref="TrueColor"/> / <see cref="FalseColor"/>
/// properties so no converter subclass proliferation is needed.
///
/// XAML usage (Styles.xaml):
///   &lt;conv:BoolToColorConverter x:Key="ChipIncludedBgConv"
///       TrueColor="{StaticResource BgChipIncluded}"
///       FalseColor="{StaticResource BgChipOff}" /&gt;
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    /// <summary>Colour returned when the bound value is <c>true</c>.</summary>
    public Color TrueColor  { get; set; } = Colors.Transparent;

    /// <summary>Colour returned when the bound value is <c>false</c> (or not a bool).</summary>
    public Color FalseColor { get; set; } = Colors.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueColor : FalseColor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
