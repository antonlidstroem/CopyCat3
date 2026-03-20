using SQLite;

namespace CopyCat.Models;

/// <summary>
/// Persisted repository entry stored in the local SQLite database.
///
/// Separation of concerns
/// ──────────────────────
/// This class is the single source of truth for everything the app
/// remembers about a repository between sessions:
///   • Identity  (URL, display name, branch)
///   • Token     (flag only — actual secret lives in SecureStorage)
///   • Workspace (the full filter/token/prompt snapshot for this repo)
///
/// It is intentionally a plain data object with no ViewModel or
/// INotifyPropertyChanged overhead — it never binds directly to XAML.
/// The ViewModel copies fields out of it into observable properties.
/// </summary>
[Table("SavedRepos")]
public class SavedRepo
{
    // ── Identity ─────────────────────────────────────────────────────────────

    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Full GitHub URL or absolute local path.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional user-facing display label.
    /// Falls back to <see cref="Url"/> when empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Branch selected the last time this repo was fetched.</summary>
    public string Branch { get; set; } = "main";

    /// <summary>
    /// Whether a token exists in SecureStorage for this repo.
    /// The token value itself is stored at key "repo_token_{Id}".
    /// </summary>
    public bool HasToken { get; set; }

    // ── Workspace snapshot ───────────────────────────────────────────────────
    // These fields store a full filter/settings snapshot that is restored
    // automatically when the user selects this repo from the recent list.

    /// <summary>JSON-serialised <c>List&lt;string&gt;</c> of enabled extension labels.</summary>
    public string SavedEnabledExts { get; set; } = string.Empty;

    /// <summary>JSON-serialised <c>List&lt;string&gt;</c> of excluded folder names.</summary>
    public string SavedExcludedFolders { get; set; } = string.Empty;

    /// <summary>JSON-serialised <c>List&lt;string&gt;</c> of active file-exclusion patterns.</summary>
    public string SavedPatterns { get; set; } = string.Empty;

    /// <summary>Last-used max-tokens value (0 = not saved).</summary>
    public int SavedMaxTokens { get; set; }

    /// <summary>
    /// <see cref="PromptItem.OriginalSortOrder"/> of the last-selected AI prompt.
    /// -1 means no prompt was selected.
    /// </summary>
    public int SavedPromptSortOrder { get; set; } = -1;

    // ── Computed helpers ─────────────────────────────────────────────────────

    /// <summary>True when at least one workspace field has been saved.</summary>
    [Ignore]
    public bool HasWorkspace =>
        SavedMaxTokens > 0 ||
        !string.IsNullOrEmpty(SavedEnabledExts) ||
        !string.IsNullOrEmpty(SavedExcludedFolders);

    /// <summary>
    /// Display name shown in the recent-repos list.
    /// Uses <see cref="Name"/> when set, otherwise falls back to the URL.
    /// </summary>
    [Ignore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? Url : Name;
}
