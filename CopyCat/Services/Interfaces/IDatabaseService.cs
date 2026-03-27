using CopyCat.Models;

namespace CopyCat.Services;

public interface IDatabaseService
{
    Task InitializeAsync();

    // ── Saved repos ────────────────────────────────────────────────────────

    Task<List<SavedRepo>> GetSavedReposAsync();
    Task<SavedRepo> UpsertRepoAsync(SavedRepo repo);
    Task DeleteRepoAsync(int id);
    Task ClearAllReposAsync();
    Task UpdateRepoWorkspaceAsync(SavedRepo repo);

    // ── Prompts ────────────────────────────────────────────────────────────

    Task<List<PromptRecord>> GetPromptsAsync();
    Task<PromptRecord> UpsertPromptAsync(PromptRecord prompt);
    Task DeletePromptAsync(int id);
    Task ResetPromptsToDefaultAsync();

    // ── XML tag buttons ────────────────────────────────────────────────────

    Task<List<XmlTagButton>> GetXmlTagButtonsAsync();
    Task<XmlTagButton> UpsertXmlTagButtonAsync(XmlTagButton tag);
    Task DeleteXmlTagButtonAsync(int id);
}