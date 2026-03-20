using CopyCat.Models;
using CopyCat.Models.Catalog;          // ← BuiltInPrompts.All lives here
using CopyCat.Services.Interfaces;
using SQLite;

namespace CopyCat.Services;

/// <summary>
/// SQLite-backed implementation of both <see cref="IRepoRepository"/>
/// and <see cref="IPromptRepository"/>.
///
/// SINGLE CLASS, TWO INTERFACES
/// ─────────────────────────────
/// Both repository interfaces share one database file and one
/// <see cref="SQLiteAsyncConnection"/>.  A single concrete class
/// avoids the overhead of two separate connections while still
/// letting consumers depend on only the interface they need.
///
/// INIT GUARD
/// ──────────
/// <see cref="InitializeAsync"/> is guarded by a flag so it is safe
/// to call from both <c>RepositoryViewModel</c> and <c>PromptsViewModel</c>
/// independently — only the first call creates the schema.
///
/// LINKER SAFETY
/// ─────────────
/// All model types (SavedRepo, PromptRecord) are preserved by Linker.xml
/// via <c>&lt;namespace name="CopyCat.Models" /&gt;</c>.  Without this,
/// SQLite-net's reflection-based column mapping silently returns empty
/// objects in Android Release builds.
/// </summary>
public class DatabaseService : IRepoRepository, IPromptRepository
{
    private SQLiteAsyncConnection? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // ── Initialisation ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return; // double-checked inside the lock

            var dbPath = Path.Combine(
                FileSystem.AppDataDirectory,
                "copycat.db3");

            _db = new SQLiteAsyncConnection(dbPath,
                SQLiteOpenFlags.ReadWrite |
                SQLiteOpenFlags.Create   |
                SQLiteOpenFlags.SharedCache);

            await _db.CreateTableAsync<SavedRepo>();
            await _db.CreateTableAsync<PromptRecord>();

            await SeedPromptsIfEmptyAsync();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task SeedPromptsIfEmptyAsync()
    {
        var count = await _db!.Table<PromptRecord>().CountAsync();
        if (count > 0) return;

        // BuiltInPrompts resolves to CopyCat.Models.Catalog.BuiltInPrompts (8 prompts)
        foreach (var seed in BuiltInPrompts.All)
        {
            await _db.InsertAsync(new PromptRecord
            {
                Title     = seed.Title,
                Content   = seed.Content,
                SortOrder = seed.SortOrder,
                IsBuiltIn = true,
            });
        }
    }

    // ── IRepoRepository ───────────────────────────────────────────────────────

    public async Task<List<SavedRepo>> GetSavedReposAsync()
    {
        await EnsureInitAsync();
        return await _db!
            .Table<SavedRepo>()
            .OrderByDescending(r => r.Id)
            .ToListAsync();
    }

    public async Task<SavedRepo> UpsertRepoAsync(SavedRepo repo)
    {
        await EnsureInitAsync();
        if (repo.Id == 0)
            await _db!.InsertAsync(repo);
        else
            await _db!.UpdateAsync(repo);
        return repo;
    }

    public async Task DeleteRepoAsync(int id)
    {
        await EnsureInitAsync();
        await _db!.DeleteAsync<SavedRepo>(id);
    }

    public async Task UpdateRepoWorkspaceAsync(SavedRepo repo)
    {
        await EnsureInitAsync();
        await _db!.UpdateAsync(repo);
    }

    // ── IPromptRepository ─────────────────────────────────────────────────────

    public async Task<List<PromptRecord>> GetPromptsAsync()
    {
        await EnsureInitAsync();
        return await _db!
            .Table<PromptRecord>()
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    public async Task<PromptRecord> UpsertPromptAsync(PromptRecord record)
    {
        await EnsureInitAsync();
        if (record.Id == 0)
            await _db!.InsertAsync(record);
        else
            await _db!.UpdateAsync(record);
        return record;
    }

    public async Task DeletePromptAsync(int id)
    {
        await EnsureInitAsync();
        await _db!.DeleteAsync<PromptRecord>(id);
    }

    public async Task ResetPromptsToDefaultAsync()
    {
        await EnsureInitAsync();

        // Delete all custom prompts
        await _db!.Table<PromptRecord>()
            .Where(p => !p.IsBuiltIn)
            .DeleteAsync();

        // Reset all built-in prompts to factory text
        foreach (var seed in BuiltInPrompts.All)
        {
            var existing = await _db!
                .Table<PromptRecord>()
                .Where(p => p.IsBuiltIn && p.SortOrder == seed.SortOrder)
                .FirstOrDefaultAsync();

            if (existing is not null)
            {
                existing.Title      = seed.Title;
                existing.Content    = seed.Content;
                existing.IsModified = false;
                await _db!.UpdateAsync(existing);
            }
            else
            {
                // Seed missing built-in (e.g. after a corrupt delete)
                await _db!.InsertAsync(new PromptRecord
                {
                    Title     = seed.Title,
                    Content   = seed.Content,
                    SortOrder = seed.SortOrder,
                    IsBuiltIn = true,
                });
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private Task EnsureInitAsync() =>
        _initialized ? Task.CompletedTask : InitializeAsync();
}
