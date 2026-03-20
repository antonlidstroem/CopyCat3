using CopyCat.Models;

namespace CopyCat.Services.Interfaces;

/// <summary>
/// Data-access contract for <see cref="PromptRecord"/> persistence.
///
/// See <see cref="IRepoRepository"/> for the rationale behind splitting
/// the original <c>IDatabaseService</c>.
/// </summary>
public interface IPromptRepository
{
    /// <summary>
    /// Ensures the database file and schema exist.
    /// Safe to call multiple times — idempotent.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Returns all prompt records ordered by <c>SortOrder</c>.
    /// Seeds the six built-in prompts on first run if the table is empty.
    /// </summary>
    Task<List<PromptRecord>> GetPromptsAsync();

    /// <summary>
    /// Inserts or updates a prompt record (matched by <c>Id</c>).
    /// Returns the saved entity with its database-assigned <c>Id</c>.
    /// </summary>
    Task<PromptRecord> UpsertPromptAsync(PromptRecord record);

    /// <summary>Permanently deletes the prompt with the given <c>Id</c>.</summary>
    Task DeletePromptAsync(int id);

    /// <summary>
    /// Deletes all custom prompts and resets all built-in prompts to their
    /// factory-default text, using <c>BuiltInPrompts.All</c> as the seed.
    /// </summary>
    Task ResetPromptsToDefaultAsync();
}
