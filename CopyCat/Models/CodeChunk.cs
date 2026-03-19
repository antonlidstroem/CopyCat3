using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

/// <summary>
/// A token-bounded slice of source files ready to be copied to an AI chat window.
///
/// Card visual design (C2):
///   Zone A (left, select area)  — background tinted amber when IsSelected.
///   Zone B (right, copy area)   — Copy button; card border turns teal when IsCopied.
///   Zone C (bottom, expand)     — ▼/▲ strip; always visible.
///
/// Token warning (C3):
///   ChunkWarningIcon is computed from EstimatedTokens and shown inline in Zone A.
/// </summary>
public partial class CodeChunk : ObservableObject
{
    // ── Data ──────────────────────────────────────────────────────────────

    private int _index;

    /// <summary>
    /// Zero-based position in the chunk list.
    /// Changing this via <see cref="NotifyIndexChanged"/> raises PropertyChanged
    /// for DisplayLabel and SubLabel so the CollectionView updates after a merge.
    /// </summary>
    public int Index
    {
        get => _index;
        set => _index = value;
    }

    public string ProjectName     { get; set; } = string.Empty;
    public string Content         { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    /// <summary>
    /// Individual source files packed into this chunk.
    /// Populated by ChunkingService — drives the per-file preview rows
    /// and selective exclusion when copying.
    /// </summary>
    public List<ChunkFile> FileEntries { get; set; } = [];

    // ── Observable state ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoneABackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderThickness))]
    [NotifyPropertyChangedFor(nameof(CopyButtonTextColor))]
    private bool _isCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoneABackground))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    // ── C2: Three-zone card colours ────────────────────────────────────────
    //
    // Zone A background: amber tint when selected (IsSelected).
    //   Selected  → #1C1406 (AccentPrimary at ~11% on C1Deep)
    //   Default   → C2Surface (#0D2128)
    //   Copied does NOT change Zone A — only the card border changes.
    //
    // Card border: teal (C5) at 2.5px when copied, C3Border at 1.5px otherwise.
    //   Both copied+selected: teal border + amber Zone A (they coexist).

    /// <summary>Zone A (select area) background color.</summary>
    public Color ZoneABackground =>
        IsSelected ? Color.FromArgb("#1C1406") : Color.FromArgb("#0D2128");

    /// <summary>Full card border color — teal when copied, structural border otherwise.</summary>
    public Color CardBorderColor =>
        IsCopied ? Color.FromArgb("#00B4BC") : Color.FromArgb("#1A3D4A");

    /// <summary>Card border thickness — heavier when copied to signal completion.</summary>
    public double CardBorderThickness => IsCopied ? 2.5 : 1.5;

    /// <summary>Copy button text tint — teal when copied, dim when not.</summary>
    public Color CopyButtonTextColor =>
        IsCopied ? Color.FromArgb("#00B4BC") : Color.FromArgb("#1A3D4A");

    // ── C3: Token context warning ──────────────────────────────────────────

    /// <summary>
    /// Warning icon shown next to the token count in Zone A.
    /// Helps the developer identify oversized chunks before pasting.
    ///   ⛔ > 128,000 tokens — exceeds Claude.ai context window
    ///   ⚠️ >  32,000 tokens — too large for GPT-3.5
    ///   ℹ️ >  16,000 tokens — above GPT-3.5 but fine for GPT-4/Claude/Gemini
    ///   ""  ≤  16,000 tokens — fits all major AI interfaces
    /// </summary>
    public string ChunkWarningIcon => EstimatedTokens switch
    {
        > 128_000 => "⛔",
        >  32_000 => "⚠️",
        >  16_000 => "ℹ️",
        _         => string.Empty,
    };

    // ── C5: Index change notification ─────────────────────────────────────

    /// <summary>
    /// Call this after changing <see cref="Index"/> directly (e.g. after MergeSelected
    /// renumbers all chunks) so the CollectionView updates DisplayLabel and SubLabel.
    /// </summary>
    public void NotifyIndexChanged()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(SubLabel));
    }

    // ── Labels ────────────────────────────────────────────────────────────

    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";
    public string SubLabel     => $"~{EstimatedTokens:N0} tokens";

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    /// <summary>
    /// Newline-joined file names for backwards compatibility.
    /// The new UI uses FileEntries directly via a BindableLayout.
    /// Kept for sessions where FileEntries is empty (loaded from older data).
    /// </summary>
    public string PreviewSnippet
    {
        get
        {
            if (FileEntries.Count > 0)
                return string.Join("\n", FileEntries.Select(f => f.FileName));

            var names = HeaderRegex()
                .Matches(Content)
                .Select(m => m.Groups["path"].Value.Trim())
                .Select(p => p.Replace('\\', '/'))
                .Select(p => p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            return names.Count == 0 ? string.Empty : string.Join("\n", names);
        }
    }

    [GeneratedRegex(@"^====\s+(?<path>.+?)\s+====$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();
}
