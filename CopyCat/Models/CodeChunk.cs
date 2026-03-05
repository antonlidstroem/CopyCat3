using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    public int    Index          { get; set; }
    public string ProjectName   { get; set; } = string.Empty;
    public string Content       { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    private bool _isCopied;

    public string DisplayLabel =>
        IsCopied
            ? $"✓  Chunk {Index + 1}  ·  {ProjectName}"
            : $"Chunk {Index + 1}  ·  {ProjectName}";

    public string SubLabel =>
        IsCopied
            ? "Kopierad till urklipp"
            : $"~{EstimatedTokens:N0} tokens";
}
