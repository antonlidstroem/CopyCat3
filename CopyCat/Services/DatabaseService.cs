using CopyCat.Models;
using SQLite;

namespace CopyCat.Services;

/// <summary>
/// SQLite-net implementation of <see cref="IDatabaseService"/>.
///
/// Migration history:
///   V1 — Initial: SavedRepos, Prompts
///   V2 — C6: Added 5 workspace columns to SavedRepos via ALTER TABLE
///   V3 — Phase 4: Added XmlTagButtons table
/// </summary>
public class DatabaseService : IDatabaseService, IAsyncDisposable
{
    private SQLiteAsyncConnection? _db;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public DatabaseService()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "copycat.db");
    }

    // ── Init ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_db is not null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_db is not null) return;

            SQLitePCL.Batteries_V2.Init();

            var connection = new SQLiteAsyncConnection(
                _dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

            await connection.CreateTableAsync<SavedRepo>();
            await connection.CreateTableAsync<PromptRecord>();
            await connection.CreateTableAsync<XmlTagButton>();

            _db = connection;

            // Run all migrations before seeding
            await MigrateWorkspaceColumnsAsync();
            await SeedBuiltInPromptsIfEmptyAsync();
            await SeedBuiltInXmlTagsIfEmptyAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// V2 migration: adds the five C6 workspace columns to SavedRepos.
    /// Each ALTER TABLE is idempotent — duplicate column errors are swallowed.
    /// </summary>
    private async Task MigrateWorkspaceColumnsAsync()
    {
        if (_db is null) return;

        var migrations = new[]
        {
            "ALTER TABLE SavedRepos ADD COLUMN SavedMaxTokens INTEGER DEFAULT 0",
            "ALTER TABLE SavedRepos ADD COLUMN SavedEnabledExts TEXT DEFAULT ''",
            "ALTER TABLE SavedRepos ADD COLUMN SavedExcludedFolders TEXT DEFAULT ''",
            "ALTER TABLE SavedRepos ADD COLUMN SavedPatterns TEXT DEFAULT ''",
            "ALTER TABLE SavedRepos ADD COLUMN SavedPromptSortOrder INTEGER DEFAULT -1",
        };

        foreach (var sql in migrations)
        {
            try { await _db.ExecuteAsync(sql); }
            catch { /* Column already exists — safe to ignore */ }
        }
    }

    private async Task<SQLiteAsyncConnection> Db()
    {
        if (_db is null) await InitializeAsync();
        return _db ?? throw new InvalidOperationException(
            "Database could not be initialised. Check storage permissions and available space.");
    }

    // ── Saved repos ────────────────────────────────────────────────────────

    public async Task<List<SavedRepo>> GetSavedReposAsync()
    {
        var db = await Db();
        return await db.Table<SavedRepo>()
            .OrderByDescending(r => r.LastUsed)
            .ToListAsync();
    }

    public async Task<SavedRepo> UpsertRepoAsync(SavedRepo repo)
    {
        var db = await Db();
        repo.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (repo.Id == 0) await db.InsertAsync(repo);
        else await db.UpdateAsync(repo);
        return repo;
    }

    public async Task DeleteRepoAsync(int id)
    {
        var db = await Db();
        await db.DeleteAsync<SavedRepo>(id);
    }

    public async Task ClearAllReposAsync()
    {
        var db = await Db();
        await db.DeleteAllAsync<SavedRepo>();
    }

    public async Task UpdateRepoWorkspaceAsync(SavedRepo repo)
    {
        var db = await Db();
        await db.ExecuteAsync(
            @"UPDATE SavedRepos
              SET SavedMaxTokens        = ?,
                  SavedEnabledExts      = ?,
                  SavedExcludedFolders  = ?,
                  SavedPatterns         = ?,
                  SavedPromptSortOrder  = ?
              WHERE Id = ?",
            repo.SavedMaxTokens,
            repo.SavedEnabledExts,
            repo.SavedExcludedFolders,
            repo.SavedPatterns,
            repo.SavedPromptSortOrder,
            repo.Id);
    }

    // ── Prompts ────────────────────────────────────────────────────────────

    public async Task<List<PromptRecord>> GetPromptsAsync()
    {
        var db = await Db();
        return await db.Table<PromptRecord>()
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    public async Task<PromptRecord> UpsertPromptAsync(PromptRecord prompt)
    {
        var db = await Db();
        if (prompt.Id == 0) await db.InsertAsync(prompt);
        else await db.UpdateAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(int id)
    {
        var db = await Db();
        await db.DeleteAsync<PromptRecord>(id);
    }

    public async Task ResetPromptsToDefaultAsync()
    {
        var db = await Db();
        await db.DeleteAllAsync<PromptRecord>();
        await SeedBuiltInPromptsAsync(db);
    }

    // ── XML tag buttons ────────────────────────────────────────────────────

    public async Task<List<XmlTagButton>> GetXmlTagButtonsAsync()
    {
        var db = await Db();
        return await db.Table<XmlTagButton>()
            .OrderBy(t => t.SortOrder)
            .ToListAsync();
    }

    public async Task<XmlTagButton> UpsertXmlTagButtonAsync(XmlTagButton tag)
    {
        var db = await Db();
        if (tag.Id == 0) await db.InsertAsync(tag);
        else await db.UpdateAsync(tag);
        return tag;
    }

    public async Task DeleteXmlTagButtonAsync(int id)
    {
        var db = await Db();
        await db.DeleteAsync<XmlTagButton>(id);
    }

    // ── Seeding ────────────────────────────────────────────────────────────

    private async Task SeedBuiltInPromptsIfEmptyAsync()
    {
        var db = await Db();
        var count = await db.Table<PromptRecord>().CountAsync();
        if (count > 0) return;
        await SeedBuiltInPromptsAsync(db);
    }

    private static async Task SeedBuiltInPromptsAsync(SQLiteAsyncConnection db)
    {
        foreach (var (sortOrder, seed) in BuiltInPrompts.BySortOrder)
        {
            await db.InsertAsync(new PromptRecord
            {
                Title = seed.Title,
                Content = seed.Content,
                IsBuiltIn = true,
                SortOrder = sortOrder,
            });
        }
    }

    private async Task SeedBuiltInXmlTagsIfEmptyAsync()
    {
        var db = await Db();
        var count = await db.Table<XmlTagButton>().CountAsync();
        if (count > 0) return;

        for (int i = 0; i < BuiltInXmlTags.Seeds.Count; i++)
        {
            var seed = BuiltInXmlTags.Seeds[i];
            await db.InsertAsync(new XmlTagButton
            {
                Label = seed.Label,
                XmlOpen = seed.XmlOpen,
                XmlClose = seed.XmlClose,
                Placeholder = seed.Placeholder,
                IsBuiltIn = true,
                SortOrder = i,
            });
        }
    }

    // ── Dispose ────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.CloseAsync();
            _db = null;
        }
        _initLock.Dispose();
    }
}