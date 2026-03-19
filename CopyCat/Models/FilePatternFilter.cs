using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// A glob pattern used to exclude files by name during chunking.
///
/// IsEnabled = true means files matching this pattern are EXCLUDED from the chunk.
///
/// IsAutoAdded tracks patterns that were created automatically by the file browser
/// (C4) when a user toggles a <see cref="FetchedFileEntry"/> to excluded.
/// Auto-added patterns are removed when the corresponding FetchedFileEntry is
/// re-included, keeping the pattern list clean.
/// </summary>
public partial class FilePatternFilter : ObservableObject
{
    /// <summary>
    /// Glob pattern matched against file names (e.g. "*.min.*", "*Test*", "Program.cs").
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// When true, files whose names match <see cref="Pattern"/> are excluded from chunking.
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// When true this pattern was auto-created by the file browser (C4) rather than
    /// entered by the user. Auto-added patterns are removed automatically when the
    /// corresponding FetchedFileEntry is re-included so they don't accumulate.
    /// </summary>
    public bool IsAutoAdded { get; set; }
}
