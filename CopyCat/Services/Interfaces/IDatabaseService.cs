using CopyCat.Models;

namespace CopyCat.Services;

/// <summary>
/// Defines persistent storage operations for CopyCat.
/// Implemented by <see cref="DatabaseService"/> using SQLite-net.
/// </summary>
public interface IDatabaseService
{
    Task InitializeAsync();

    // ── Saved repos ────────────────────────────────────────────────────────

    Task<List<SavedRepo>> GetSavedReposAsync();
    Task<SavedRepo>       UpsertRepoAsync(SavedRepo repo);
    Task                  DeleteRepoAsync(int id);
    Task                  ClearAllReposAsync();

    /// <summary>
    /// Persists the five workspace snapshot columns of <paramref name="repo"/>
    /// without touching LastUsed or other metadata.
    /// Called by SaveWorkspaceCommand after capturing filter state.
    /// </summary>
    Task UpdateRepoWorkspaceAsync(SavedRepo repo);

    // ── Prompts ────────────────────────────────────────────────────────────

    Task<List<PromptRecord>> GetPromptsAsync();
    Task<PromptRecord>       UpsertPromptAsync(PromptRecord prompt);
    Task                     DeletePromptAsync(int id);
    Task                     ResetPromptsToDefaultAsync();
}
