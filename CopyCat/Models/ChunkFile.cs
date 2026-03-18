using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// Represents a single source file that was packed into a <see cref="CodeChunk"/>.
/// Drives the per-file preview rows inside the chunk card:
///   - Tap the file name  → toggle the inline code viewer (IsCodeExpanded)
///   - Tap the icon       → exclude / re-include the file from the copied output (IsExcluded)
/// </summary>
public partial class ChunkFile : ObservableObject
{
    // ── Data ──────────────────────────────────────────────────────────────

    /// <summary>Relative path as it appears in the repository (e.g. "src/App.cs").</summary>
    public string Path    { get; set; } = string.Empty;

    /// <summary>Full text content of the file.</summary>
    public string Content { get; set; } = string.Empty;

    // ── UI state ──────────────────────────────────────────────────────────

    /// <summary>When true the inline code viewer is shown below this file row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    private bool _isCodeExpanded;

    /// <summary>
    /// When true this file is skipped when the chunk is copied to clipboard.
    /// The row background turns red to signal exclusion.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    private bool _isExcluded;

    // ── Derived ───────────────────────────────────────────────────────────

    /// <summary>Just the file name without directory (e.g. "App.cs").</summary>
    public string FileName =>
        string.IsNullOrEmpty(Path)
            ? string.Empty
            : System.IO.Path.GetFileName(Path.Replace('\\', '/'));

    /// <summary>Row background tint — red when excluded, transparent otherwise.</summary>
    public Color RowBackground =>
        IsExcluded ? Color.FromArgb("#2A0D0D") : Colors.Transparent;

    /// <summary>○ when included, ✕ when excluded.</summary>
    public string ExcludeIcon => IsExcluded ? "✕" : "○";

    /// <summary>Red when excluded, dim teal when included.</summary>
    public Color ExcludeIconColor =>
        IsExcluded ? Color.FromArgb("#EF4444") : Color.FromArgb("#1A3D4A");

    /// <summary>▲/▼ for the code-expand chevron.</summary>
    public string ExpandIcon => IsCodeExpanded ? "▲" : "▼";
}
