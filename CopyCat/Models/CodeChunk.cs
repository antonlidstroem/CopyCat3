using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    public int    Index           { get; set; }
    public string ProjectName     { get; set; } = string.Empty;
    public string Content         { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

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

    // ── Card colours: copied (green) > selected (blue) > default ──────────

    public Color CardBackgroundColor =>
        IsCopied   ? Color.FromArgb("#00A3A9") :
        IsSelected ? Color.FromArgb("#F59E0B") :
                     Color.FromArgb("#008C8B");

    public Color CardBorderColor =>
        IsCopied   ? Color.FromArgb("#F59E0B") :
        IsSelected ? Color.FromArgb("#006770") :
                     Color.FromArgb("#003B46");


    /// <summary>Share button tint — green when this chunk is the active copied one.</summary>
    public Color CopyButtonTextColor =>
        IsCopied ? Color.FromArgb("#22C55E") : Color.FromArgb("#666690");

    // ── Labels ────────────────────────────────────────────────────────────

    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";
    public string SubLabel     => $"~{EstimatedTokens:N0} tokens";

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    public string PreviewSnippet
    {
        get
        {
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
