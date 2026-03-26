using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// Represents a single source file packed into a <see cref="CodeChunk"/>.
/// Drives the per-file preview rows inside the chunk card.
/// </summary>
public partial class ChunkFile : ObservableObject
{
    public string Path    { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    private bool _isCodeExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    private bool _isExcluded;

    public string FileName =>
        string.IsNullOrEmpty(Path)
            ? string.Empty
            : System.IO.Path.GetFileName(Path.Replace('\\', '/'));

    public Color RowBackground =>
        IsExcluded ? Color.FromArgb("#2A0D0D") : Colors.Transparent;

    public string ExcludeIcon => IsExcluded ? "✕" : "○";

    public Color ExcludeIconColor =>
        IsExcluded ? Color.FromArgb("#EF4444") : Color.FromArgb("#1A3D4A");

    public string ExpandIcon => IsCodeExpanded ? "▲" : "▼";
}
