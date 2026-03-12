using CopyCat.Models;
using SQLite;

namespace CopyCat.Services;

public class DatabaseService : IDatabaseService, IAsyncDisposable
{
    private SQLiteAsyncConnection? _db;
    private readonly string        _dbPath;
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

            _db = connection;

            await SeedBuiltInPromptsIfEmptyAsync();
        }
        finally
        {
            _initLock.Release();
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
        else              await db.UpdateAsync(repo);
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
        else                await db.UpdateAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(int id)
    {
        var db = await Db();
        await db.DeleteAsync<PromptRecord>(id);
    }

    /// <summary>
    /// Wipes ALL prompt rows and re-seeds the default built-in set.
    /// Separated from <see cref="SeedBuiltInPromptsIfEmptyAsync"/> so
    /// the seed logic can be called unconditionally without a count guard.
    /// </summary>
    public async Task ResetPromptsToDefaultAsync()
    {
        var db = await Db();
        await db.DeleteAllAsync<PromptRecord>();
        await SeedBuiltInPromptsAsync(db);
    }

    // ── Seeding ────────────────────────────────────────────────────────────

    /// <summary>Seeds only when the table is empty (used on first launch).</summary>
    private async Task SeedBuiltInPromptsIfEmptyAsync()
    {
        var db    = await Db();
        var count = await db.Table<PromptRecord>().CountAsync();
        if (count > 0) return;
        await SeedBuiltInPromptsAsync(db);
    }

    /// <summary>Unconditionally inserts the six built-in prompts (table must already be cleared).</summary>
    private static async Task SeedBuiltInPromptsAsync(SQLiteAsyncConnection db)
    {
        var builtIns = new[]
        {
            new PromptRecord
            {
                Title     = "Code Writer",
                Content   = "You are an expert software engineer. I will provide code from a repository. " +
                            "Implement the requested feature following the existing patterns, architecture, " +
                            "and coding conventions. Ensure the solution is clean, maintainable, and integrates " +
                            "seamlessly with the existing code.\n\nHere is the repository code:\n\n[PASTE CHUNK]",
                IsBuiltIn = true, SortOrder = 0,
            },
            new PromptRecord
            {
                Title     = "Code Analyzer",
                Content   = "You are an expert code reviewer. Analyze the following code from a repository. " +
                            "Identify potential bugs, performance issues, security vulnerabilities, and areas " +
                            "for improvement. Provide specific, actionable feedback with examples.\n\n" +
                            "Here is the code:\n\n[PASTE CHUNK]",
                IsBuiltIn = true, SortOrder = 1,
            },
            new PromptRecord
            {
                Title     = "Code Planner",
                Content   = "You are a senior software architect. Based on the following codebase, help me " +
                            "plan a development strategy. Identify the architecture patterns used, suggest " +
                            "improvements, and outline a step-by-step plan for implementing [DESCRIBE YOUR GOAL].\n\n" +
                            "Here is the repository code:\n\n[PASTE CHUNK]",
                IsBuiltIn = true, SortOrder = 2,
            },
            new PromptRecord
            {
                Title     = "Refactor Guide",
                Content   = "You are an expert in clean code and refactoring. Review the following code and " +
                            "provide a detailed refactoring guide. Focus on improving readability, reducing " +
                            "complexity, applying SOLID principles, and modernizing patterns where appropriate.\n\n" +
                            "Here is the code:\n\n[PASTE CHUNK]",
                IsBuiltIn = true, SortOrder = 3,
            },
            new PromptRecord
            {
                Title     = "Test Writer",
                Content   = "You are a test-driven development expert. Based on the following code, write " +
                            "comprehensive unit tests. Cover edge cases, happy paths, and error scenarios. " +
                            "Follow any existing testing patterns; otherwise use best practices for the " +
                            "detected framework.\n\nHere is the code:\n\n[PASTE CHUNK]",
                IsBuiltIn = true, SortOrder = 4,
            },
            new PromptRecord
            {
                Title     = "Bug Finder",
                Content   = "You are a debugging expert. Carefully read the following code and find all bugs, " +
                            "logical errors, null-reference risks, race conditions, and edge cases that could " +
                            "cause failures in production. For each issue, explain the root cause and suggest a fix.\n\n" +
                            "Here is the code:\n\n[PASTE CHUNK]",
                IsBuiltIn = true, SortOrder = 5,
            },
        };

        foreach (var p in builtIns) await db.InsertAsync(p);
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
