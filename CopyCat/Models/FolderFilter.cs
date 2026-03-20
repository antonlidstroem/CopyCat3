using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// A toggleable folder-exclusion chip shown in the EXCLUDE FOLDERS card.
///
/// When <see cref="IsExcluded"/> is true the folder (and all its
/// descendants) are skipped during both file enumeration and chunking.
/// Matching is case-insensitive against any path segment.
/// </summary>
public partial class FolderFilter : ObservableObject
{
    /// <summary>Folder name to match (e.g. "bin", "node_modules").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this folder is excluded from the fetch.
    /// Red-border chip = excluded; green-border chip = included.
    /// </summary>
    [ObservableProperty]
    private bool _isExcluded;
}
