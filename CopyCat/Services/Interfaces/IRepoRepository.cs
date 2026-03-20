using CopyCat.Models;

namespace CopyCat.Services.Interfaces;

/// <summary>
/// Data-access contract for <see cref="SavedRepo"/> persistence.
///
/// WHY SPLIT FROM IDatabaseService
/// ────────────────────────────────
/// The original <c>IDatabaseService</c> mixed repo CRUD and prompt CRUD
/// in a single interface, violating the Interface Segregation Principle:
/// a consumer that only needs repo operations was forced to depend on
/// prompt methods it never calls, and vice versa.
///
/// After the split:
///   • <c>RepositoryViewModel</c> depends on <see cref="IRepoRepository"/> only.
///   • <c>PromptsViewModel</c>    depends on <see cref="IPromptRepository"/> only.
///   • Neither can accidentally call the other domain's persistence layer.
///
/// INITIALISATION
/// ──────────────
/// <see cref="InitializeAsync"/> is intentionally present on BOTH interfaces.
/// The concrete <c>DatabaseService</c> implements them as a single idempotent
/// method (guarded by a flag) so calling it from either consumer is safe.
/// In a future refactor each repository can own its own DB connection.
/// </summary>
public interface IRepoRepository
{
    /// <summary>
    /// Ensures the database file and schema exist.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    Task InitializeAsync();

    /// <summary>Returns all saved repositories ordered by most-recently-used first.</summary>
    Task<List<Models.SavedRepo>> GetSavedReposAsync();

    /// <summary>
    /// Inserts a new repo or updates an existing one (matched by <c>Id</c>).
    /// Returns the saved entity with its database-assigned <c>Id</c>.
    /// </summary>
    Task<Models.SavedRepo> UpsertRepoAsync(Models.SavedRepo repo);

    /// <summary>Deletes the repo with the given <c>Id</c>. No-op if not found.</summary>
    Task DeleteRepoAsync(int id);

    /// <summary>
    /// Persists the workspace snapshot fields on an existing repo row
    /// (SavedEnabledExts, SavedExcludedFolders, SavedPatterns,
    /// SavedMaxTokens, SavedPromptSortOrder).
    /// </summary>
    Task UpdateRepoWorkspaceAsync(Models.SavedRepo repo);
}
