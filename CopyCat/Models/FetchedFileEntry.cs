using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// Represents a single file returned by the last fetch operation.
/// Drives the "browse fetched files" panel inside FILE PATTERNS so the
/// developer can exclude specific files without writing glob patterns.
///
/// Populated by MainViewModel.FetchAsync after the file list is obtained.
/// Cleared on Reset. Not persisted — session-only.
/// </summary>
public partial class FetchedFileEntry : ObservableObject
{
    /// <summary>Relative path as it appears in the repository (e.g. "src/App/MainPage.cs").</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Just the file name portion, used as the display label in the browser row
    /// and as the exact-match pattern added to FilePatternFilters on exclusion.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Parent folder path used for group headers in the CollectionView
    /// (e.g. "src/App" or "Root" for files at the repository root).
    /// </summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>
    /// When true this file has been explicitly excluded by the user in the
    /// file browser. The ViewModel adds an exact-filename FilePatternFilter
    /// when this becomes true and removes it when it becomes false.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private bool _isExcluded;

    // ── Derived display helpers ────────────────────────────────────────────

    /// <summary>○ when included, ✕ when excluded.</summary>
    public string ExcludeIcon => IsExcluded ? "✕" : "○";

    /// <summary>Red when excluded, dim border-color when included.</summary>
    public Color ExcludeIconColor =>
        IsExcluded ? Color.FromArgb("#EF4444") : Color.FromArgb("#1A3D4A");

    /// <summary>Red tint row background when excluded, transparent otherwise.</summary>
    public Color RowBackground =>
        IsExcluded ? Color.FromArgb("#2A0D0D") : Colors.Transparent;
}
