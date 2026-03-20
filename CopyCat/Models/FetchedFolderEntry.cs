using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// Represents one distinct folder discovered after a fetch, shown in the
/// "From this repo" section of the Exclude Folders card.
///
/// Toggling <see cref="IsExcluded"/> syncs back to <c>FolderFilters</c>
/// via FilterViewModel so the folder chip appears in the main chip row.
/// </summary>
public partial class FetchedFolderEntry : ObservableObject
{
    /// <summary>Folder path relative to repo root (forward slashes).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Number of files directly inside this folder.</summary>
    public int FileCount { get; set; }

    /// <summary>Whether this folder is currently excluded from chunking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    private bool _isExcluded;

    public string ExcludeIcon      => IsExcluded ? "✕" : "○";
    public string FileCountLabel   => FileCount == 1 ? "1 file" : $"{FileCount} files";

    public Microsoft.Maui.Graphics.Color ExcludeIconColor =>
        IsExcluded
            ? Microsoft.Maui.Graphics.Color.FromArgb("#EF4444")
            : Microsoft.Maui.Graphics.Color.FromArgb("#6B7280");
}
