using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// Represents one file section within a <see cref="CodeChunk"/>.
///
/// FIX 6 — memory: ContentStart / ContentLength are offsets into the parent
/// CodeChunk.Content string. No substring copy is allocated here; the copy
/// happens once in CodeChunk.IncludedContent when the user presses Copy.
/// </summary>
public partial class CodeChunkFile : ObservableObject
{
    /// <summary>Full relative path extracted from the ==== header ==== line.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Leaf file name for compact display in the preview panel.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Estimated token count for this section alone (~4 chars / token).</summary>
    public int EstimatedTokens { get; init; }

    // Offsets into the parent CodeChunk.Content — internal so only CodeChunk reads them.
    internal int ContentStart  { get; init; }
    internal int ContentLength { get; init; }

    /// <summary>
    /// Controls whether this file's section is included in the clipboard payload.
    /// Default: <c>true</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isIncluded = true;
}
