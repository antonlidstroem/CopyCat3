using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

/// <summary>
/// A token-bounded slice of source files ready to be copied to an AI chat window.
/// </summary>
public partial class CodeChunk : ObservableObject
{
    private int _index;

    public int Index
    {
        get => _index;
        set => _index = value;
    }

    public string ProjectName     { get; set; } = string.Empty;
    public string Content         { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    public List<ChunkFile> FileEntries { get; set; } = [];

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

    public Color ZoneABackground =>
        IsSelected ? Color.FromArgb("#1C1406") : Color.FromArgb("#0D2128");

    /// <summary>
    /// FIX B-1 note: CardBorderColor is consumed via ColorToBrushConverter in XAML
    /// because Border.Stroke is typed as Brush, not Color. The converter is applied
    /// in the binding expression — this property remains Color for type safety.
    /// </summary>
    public Color CardBorderColor =>
        IsCopied ? Color.FromArgb("#00B4BC") : Color.FromArgb("#1A3D4A");

    public double CardBorderThickness => IsCopied ? 2.5 : 1.5;

    public Color CopyButtonTextColor =>
        IsCopied ? Color.FromArgb("#00B4BC") : Color.FromArgb("#1A3D4A");

    public string ChunkWarningIcon => EstimatedTokens switch
    {
        > 128_000 => "⛔",
        >  32_000 => "⚠️",
        >  16_000 => "ℹ️",
        _         => string.Empty,
    };

    public void NotifyIndexChanged()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(SubLabel));
    }

    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";
    public string SubLabel     => $"~{EstimatedTokens:N0} tokens";
    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

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
