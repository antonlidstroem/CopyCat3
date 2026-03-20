using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// A toggleable file-extension filter chip shown in the FILE TYPES card.
///
/// Each instance represents a language group (e.g. ".cs", ".ts / .tsx").
/// <see cref="IsEnabled"/> drives both the chip visual state and whether
/// files with a matching extension are included in the fetch.
///
/// Inherits <see cref="ObservableObject"/> so that toggling
/// <see cref="IsEnabled"/> propagates to the XAML chip immediately
/// without any additional ViewModel wiring.
/// </summary>
public partial class FileTypeFilter : ObservableObject
{
    /// <summary>Display label shown on the chip (e.g. ".cs", ".sh/.ps1").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// All file extensions that belong to this group.
    /// A single chip can cover multiple extensions (e.g. ".ts" and ".tsx").
    /// </summary>
    public string[] Extensions { get; init; } = [];

    /// <summary>Whether files matching this group are included in the chunk.</summary>
    [ObservableProperty]
    private bool _isEnabled;
}
