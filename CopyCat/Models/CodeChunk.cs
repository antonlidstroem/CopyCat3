using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    public int    Index           { get; set; }
    public string ProjectName     { get; set; } = string.Empty;
    public string Content         { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    /// <summary>
    /// Individual source files packed into this chunk.
    /// Populated by ChunkingService — drives the per-file preview rows
    /// and selective exclusion when copying.
    /// </summary>
    public List<ChunkFile> FileEntries { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CopyButtonTextColor))]
    private bool _isCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    // ── Card colours (6-color palette) ────────────────────────────────────

    public Color CardBackgroundColor =>
        IsCopied   ? Color.FromArgb("#061A1B") :
        IsSelected ? Color.FromArgb("#1A1406") :
                     Color.FromArgb("#0D2128");

    public Color CardBorderColor =>
        IsCopied   ? Color.FromArgb("#00B4BC") :
        IsSelected ? Color.FromArgb("#F59E0B") :
                     Color.FromArgb("#1A3D4A");

    public Color CopyButtonTextColor =>
        IsCopied ? Color.FromArgb("#00B4BC") : Color.FromArgb("#1A3D4A");

    // ── Labels ────────────────────────────────────────────────────────────

    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";
    public string SubLabel     => $"~{EstimatedTokens:N0} tokens";

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    /// <summary>
    /// Kept for backwards compatibility. Returns newline-joined file names.
    /// The new UI uses FileEntries directly; this is still used when
    /// FileEntries is empty (chunks from an older session).
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
