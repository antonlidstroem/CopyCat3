using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CopyCat.Models;

/// <summary>
/// Represents one source file within a <see cref="CodeChunk"/>.
///
/// Shown in the expanded file-list section of a chunk card (Zone C).
/// The user can:
///   • Exclude/include individual files from the copy payload.
///   • Expand inline to preview the raw file content.
/// </summary>
public partial class ChunkFile : ObservableObject
{
    // ── Data ─────────────────────────────────────────────────────────────────

    /// <summary>Full path of the file (repo-root-relative, forward slashes).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>File name without directory (display label in the list row).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Raw file content (used for the inline code preview).</summary>
    public string Content { get; set; } = string.Empty;

    // ── UI state ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether this file is excluded from the chunk copy payload.
    /// Excluded files are skipped by <c>BuildChunkContent()</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private bool _isExcluded;

    /// <summary>Whether the inline code preview is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    private bool _isCodeExpanded;

    // ── Computed display helpers ──────────────────────────────────────────────

    /// <summary>Toggle icon for the exclude/include control.</summary>
    public string ExcludeIcon => IsExcluded ? "✕" : "○";

    /// <summary>Colour of the exclude icon.</summary>
    public Color ExcludeIconColor =>
        IsExcluded
            ? Color.FromArgb("#EF4444")   // TextError
            : Color.FromArgb("#6B7280");  // TextMuted

    /// <summary>Row background tint when the file is excluded.</summary>
    public Color RowBackground =>
        IsExcluded
            ? Color.FromArgb("#2A1515")
            : Colors.Transparent;

    /// <summary>Chevron icon for the code-expand toggle.</summary>
    public string ExpandIcon => IsCodeExpanded ? "▲" : "▼";
}
