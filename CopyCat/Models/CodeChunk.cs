using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    public int Index { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int EstimatedTokens { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    private bool _isCopied;

    /// <summary>Markerad för multi-select share.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private bool _isSelected;

    /// <summary>Styr om förhandsvisningen är expanderad.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    public string DisplayLabel =>
        IsCopied
            ? $"✓  Chunk {Index + 1}  ·  {ProjectName}"
            : $"Chunk {Index + 1}  ·  {ProjectName}";

    public string SubLabel =>
        IsCopied
            ? "Kopierad till urklipp"
            : $"~{EstimatedTokens:N0} tokens";

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    /// <summary>
    /// Listar filnamnen i chunken genom att plocka ut alla rubriker på formen
    /// "==== sökväg/till/Fil.cs ====" och visa bara filnamnet (sista segmentet).
    /// Visar aldrig kod — bara en lista med de filer som ingår i chunken.
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

    // Matchar rubriker som "==== sökväg/till/Fil.cs ===="
    [GeneratedRegex(@"^====\s+(?<path>.+?)\s+====$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();
}
