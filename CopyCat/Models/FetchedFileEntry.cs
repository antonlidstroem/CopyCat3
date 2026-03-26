using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// Represents a single file returned by the last fetch operation.
/// Kept for backward compatibility — the new Phase 5 tree view uses
/// FileTreeNode instead, but FetchedFileEntry is still populated for
/// the flat-list exclusion logic.
/// </summary>
public partial class FetchedFileEntry : ObservableObject
{
    public string Path     { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Folder   { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private bool _isExcluded;

    public string ExcludeIcon => IsExcluded ? "✕" : "○";

    public Color ExcludeIconColor =>
        IsExcluded ? Color.FromArgb("#EF4444") : Color.FromArgb("#1A3D4A");

    public Color RowBackground =>
        IsExcluded ? Color.FromArgb("#2A0D0D") : Colors.Transparent;
}
