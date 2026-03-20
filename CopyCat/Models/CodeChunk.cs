using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CopyCat.Models;

/// <summary>
/// A token-bounded slice of source files produced by the chunking service.
///
/// Shown as a three-zone card in the Results panel.
///   Zone A — select (tap to toggle <see cref="IsSelected"/>)
///   Zone B — copy button
///   Zone C — expand strip (tap to show <see cref="FileEntries"/>)
///
/// Design notes
/// ────────────
/// All display-helper properties are computed on the model itself rather
/// than in the ViewModel.  This keeps the ViewModel thin and makes the
/// helpers unit-testable without a MAUI host.
/// </summary>
public partial class CodeChunk : ObservableObject
{
    // ── Chunk identity ────────────────────────────────────────────────────────

    /// <summary>Zero-based position in the ordered chunk list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    private int _index;

    /// <summary>Source project name (repository or folder name).</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Full concatenated content of all included files.
    /// Used as the copy/share payload.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Approximate token count (≈ chars / 4).</summary>
    public int EstimatedTokens { get; set; }

    /// <summary>Individual files that make up this chunk.</summary>
    public List<ChunkFile> FileEntries { get; set; } = [];

    // ── UI state ─────────────────────────────────────────────────────────────

    /// <summary>Whether this chunk is selected for multi-select operations.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoneABackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderThickness))]
    private bool _isSelected;

    /// <summary>Whether this chunk has been copied at least once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderThickness))]
    private bool _isCopied;

    /// <summary>Whether the file-list expansion panel is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    // ── Computed display helpers ──────────────────────────────────────────────

    /// <summary>Primary label: "Chunk N · ProjectName".</summary>
    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";

    /// <summary>Secondary label: approximate token count.</summary>
    public string SubLabel => $"~{EstimatedTokens:N0} tokens";

    /// <summary>Warning icon shown when the chunk is very large.</summary>
    public string ChunkWarningIcon => EstimatedTokens switch
    {
        > 128_000 => "⛔",
        >  32_000 => "⚠️",
        >  16_000 => "ℹ️",
        _         => string.Empty,
    };

    /// <summary>Icon on the Zone C expand strip.</summary>
    public string PreviewToggleIcon => IsPreviewExpanded ? "▲  files" : "▼  files";

    /// <summary>Background of Zone A (selection area).</summary>
    public Color ZoneABackground =>
        IsSelected
            ? Color.FromArgb("#0D2A2B")   // teal tint when selected
            : Colors.Transparent;

    /// <summary>Card border colour driven by copy and selection state.</summary>
    public Color CardBorderColor =>
        IsSelected ? Color.FromArgb("#00B4BC") :
        IsCopied   ? Color.FromArgb("#1F4D1F") :
                     Color.FromArgb("#2D2D3A");

    /// <summary>Border thickness — thicker when selected.</summary>
    public double CardBorderThickness => IsSelected ? 1.5 : 0.8;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Raises change notifications for all index-derived display labels
    /// after a merge/reorder operation updates <see cref="Index"/>.
    /// </summary>
    public void NotifyIndexChanged()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(SubLabel));
    }
}
