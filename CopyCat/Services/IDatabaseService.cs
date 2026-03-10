using CopyCat.Models;

namespace CopyCat.Services;

public interface IDatabaseService
{
    Task InitializeAsync();

    // ── Saved repos ────────────────────────────────────────────────────────
    Task<List<SavedRepo>> GetSavedReposAsync();
    Task<SavedRepo>       UpsertRepoAsync(SavedRepo repo);
    Task                  DeleteRepoAsync(int id);
    Task                  ClearAllReposAsync();

    // ── Prompts ────────────────────────────────────────────────────────────
    Task<List<PromptRecord>> GetPromptsAsync();
    Task<PromptRecord>       UpsertPromptAsync(PromptRecord prompt);
    Task                     DeletePromptAsync(int id);
}
