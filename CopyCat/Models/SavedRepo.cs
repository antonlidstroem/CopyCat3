using SQLite;

namespace CopyCat.Models;

/// <summary>
/// A persisted repository record stored in the SQLite database.
///
/// C6 — Workspace save/restore:
///   Five new columns store filter and preference snapshots so a developer can
///   restore their exact configuration in one tap the next session.
///   New columns require an ALTER TABLE migration (see DatabaseService.MigrateWorkspaceColumnsAsync).
/// </summary>
[Table("SavedRepos")]
public class SavedRepo
{
    [PrimaryKey, AutoIncrement]
    public int    Id       { get; set; }

    /// <summary>User-given friendly name. Falls back to TrimmedUrl when empty.</summary>
    public string Name     { get; set; } = string.Empty;

    public string Url      { get; set; } = string.Empty;
    public string Branch   { get; set; } = string.Empty;

    /// <summary>True when a GitHub token for this repo is stored in SecureStorage.</summary>
    public bool   HasToken { get; set; }

    /// <summary>Unix timestamp (UTC seconds) of last access — used for sort order.</summary>
    public long   LastUsed { get; set; }

    // ── C6: Workspace snapshot columns ────────────────────────────────────

    /// <summary>
    /// Saved token limit in tokens (0 = not saved, use app default).
    /// Restored as MaxTokensPerChunk when this repo is selected.
    /// </summary>
    public int SavedMaxTokens { get; set; } = 0;

    /// <summary>
    /// JSON array of enabled FileTypeFilter.Label strings (e.g. [".cs",".xaml"]).
    /// Empty string = not saved. Labels not found in current filters are silently skipped.
    /// </summary>
    public string SavedEnabledExts { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of excluded FolderFilter.Name strings (e.g. ["bin","obj",".git"]).
    /// Empty string = not saved.
    /// </summary>
    public string SavedExcludedFolders { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of enabled FilePatternFilter.Pattern strings.
    /// Empty string = not saved.
    /// </summary>
    public string SavedPatterns { get; set; } = string.Empty;

    /// <summary>
    /// SortOrder of the PromptItem to pre-select for share (-1 = not saved).
    /// Matched against PromptItem.OriginalSortOrder on restore.
    /// </summary>
    public int SavedPromptSortOrder { get; set; } = -1;

    // ── Derived ───────────────────────────────────────────────────────────

    [Ignore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? TrimmedUrl
            : $"{Name}  ·  {TrimmedUrl}";

    public override string ToString() => DisplayName;

    /// <summary>True when at least one workspace field has been saved.</summary>
    [Ignore]
    public bool HasWorkspace =>
        SavedMaxTokens > 0 ||
        !string.IsNullOrEmpty(SavedEnabledExts) ||
        !string.IsNullOrEmpty(SavedExcludedFolders) ||
        !string.IsNullOrEmpty(SavedPatterns) ||
        SavedPromptSortOrder >= 0;

    [Ignore]
    private string TrimmedUrl =>
        Url.Replace("https://github.com/", "").TrimEnd('/');
}
