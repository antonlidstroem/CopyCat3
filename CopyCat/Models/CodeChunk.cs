using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    public int    Index        { get; set; }
    public string ProjectName  { get; set; } = string.Empty;
    public string Content      { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CopyButtonTextColor))]
    private bool _isCopied;

    /// <summary>Markerad för multi-select share.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    private bool _isSelected;

    /// <summary>Styr om förhandsvisningen är expanderad.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    // ── Card-färg baserat på tillstånd (kopierad > markerad > standard) ────────

    /// <summary>Kortets bakgrundsfärg: grön om kopierad, blå om markerad, annars standard.</summary>
    public Color CardBackgroundColor =>
        IsCopied   ? Color.FromArgb("#1A2E20") :   // BgChunkCopied  – grön
        IsSelected ? Color.FromArgb("#17213A") :   // BgChunkSelected – blå
                     Color.FromArgb("#1A1A2E");    // BgChunkDefault

    /// <summary>Kortets kantfärg: grön om kopierad, blå om markerad, annars standard.</summary>
    public Color CardBorderColor =>
        IsCopied   ? Color.FromArgb("#22C55E") :   // BorderChunkCopied   – grön
        IsSelected ? Color.FromArgb("#5B8EFF") :   // BorderChunkSelected – blå
                     Color.FromArgb("#2D2D4A");    // BorderChunkDefault

    /// <summary>Textfärg på kopiera-knappen (ljusgrön när kopierad).</summary>
    public Color CopyButtonTextColor =>
        IsCopied ? Color.FromArgb("#22C55E") : Color.FromArgb("#666690");

    // ── Etiketter ──────────────────────────────────────────────────────────────

    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";

    public string SubLabel => $"~{EstimatedTokens:N0} tokens";

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    /// <summary>
    /// Listar filnamnen i chunken genom att plocka ut alla rubriker på formen
    /// "==== sökväg/till/Fil.cs ====" och visa bara filnamnet (sista segmentet).
    /// </summary>
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

            return names.Count == 0
                ? string.Empty
                : string.Join("\n", names);
        }
    }

    [GeneratedRegex(@"^====\s+(?<path>.+?)\s+====$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();
}
