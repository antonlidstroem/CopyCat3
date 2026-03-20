using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CopyCat.Models;

/// <summary>
/// Represents a single file shown in the Fetched Files browser
/// (the collapsible list inside the file-patterns card).
///
/// Each entry lets the user include or exclude a specific file
/// individually, independent of the pattern filters.  Tapping
/// the exclude icon creates a matching <see cref="FilePatternFilter"/>
/// with <c>IsAutoAdded = true</c>.
/// </summary>
public partial class FetchedFileEntry : ObservableObject
{
    // ── Data ─────────────────────────────────────────────────────────────────

    /// <summary>Normalised forward-slash path relative to the repo root.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>File name portion of <see cref="Path"/> (no directory).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Directory portion of <see cref="Path"/>.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Whether this file is excluded from chunking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private bool _isExcluded;

    // ── Computed display helpers ──────────────────────────────────────────────

    /// <summary>Icon character shown in the toggle column.</summary>
    public string ExcludeIcon => IsExcluded ? "✕" : "○";

    /// <summary>Colour of the toggle icon.</summary>
    public Color ExcludeIconColor =>
        IsExcluded
            ? Color.FromArgb("#EF4444")   // TextError
            : Color.FromArgb("#6B7280");  // TextMuted

    /// <summary>Row background tint when the file is excluded.</summary>
    public Color RowBackground =>
        IsExcluded
            ? Color.FromArgb("#2A1515")   // subtle red tint
            : Colors.Transparent;
}
