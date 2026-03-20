using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// A toggleable wildcard file-exclusion pattern shown in the
/// EXCLUDE FILE PATTERNS card.
///
/// Patterns use '*' as a wildcard matched against file names only
/// (not paths). Examples: <c>*Test*</c>, <c>*.min.*</c>, <c>Program.cs</c>.
///
/// <see cref="IsAutoAdded"/> marks patterns that were created
/// automatically when the user tapped the exclude icon in the
/// Fetched Files browser, so they can be cleaned up on re-include
/// without touching user-defined patterns.
/// </summary>
public partial class FilePatternFilter : ObservableObject
{
    /// <summary>Wildcard pattern matched against file names.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Whether this pattern is currently active (red chip = active).</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// True when this pattern was auto-created from the Fetched Files browser.
    /// Auto-added patterns are removed when the user re-includes the file.
    /// They are also excluded from the workspace save.
    /// </summary>
    public bool IsAutoAdded { get; set; }
}
