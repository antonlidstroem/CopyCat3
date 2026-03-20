namespace CopyCat.Models.Catalog;

/// <summary>
/// The six factory-default AI prompts shipped with CopyCat.
///
/// STABLE SORT ORDERS
/// ──────────────────
/// Each prompt has a fixed <c>SortOrder</c> (1–6) that never changes,
/// even after the user edits the prompt's text.  This lets the workspace-
/// restore feature re-select the correct prompt by sort order even after a
/// "Reset all prompts" operation regenerates the DB rows with new IDs.
///
/// USAGE
/// ─────
/// <c>BuiltInPrompts.BySortOrder.TryGetValue(order, out var seed)</c>
/// returns the original title + content for a ResetSinglePrompt operation.
///
/// The database service uses <see cref="All"/> to seed the DB on first run.
/// </summary>
public static class BuiltInPrompts
{
    /// <summary>
    /// Immutable seed record for a single built-in prompt.
    /// </summary>
    public sealed record PromptSeed(int SortOrder, string Title, string Content);

    /// <summary>All six built-in prompts in display order.</summary>
    public static readonly IReadOnlyList<PromptSeed> All =
    [
        new(1,
            "Code Review",
            "Please review the following code for correctness, clarity, and potential improvements. "
            + "Note any bugs, code smells, or opportunities to simplify.\n\n[PASTE CHUNK]"),

        new(2,
            "Explain Architecture",
            "Explain the architecture and design of the following code. "
            + "Describe the key classes, their responsibilities, and how they interact.\n\n[PASTE CHUNK]"),

        new(3,
            "Generate Documentation",
            "Write comprehensive XML doc comments and a markdown summary for the following code. "
            + "Cover all public types, methods, and non-obvious parameters.\n\n[PASTE CHUNK]"),

        new(4,
            "Find Bugs",
            "Carefully analyse the following code for bugs, edge-case failures, null-reference risks, "
            + "and logic errors. List each issue with a suggested fix.\n\n[PASTE CHUNK]"),

        new(5,
            "Refactor Suggestions",
            "Suggest concrete refactoring improvements for the following code. "
            + "Focus on SOLID principles, reducing duplication, and improving readability. "
            + "Show before/after examples where helpful.\n\n[PASTE CHUNK]"),

        new(6,
            "Unit Test Ideas",
            "Based on the following code, suggest a comprehensive set of unit tests. "
            + "Cover happy paths, edge cases, and failure modes. "
            + "Use xUnit + FluentAssertions style.\n\n[PASTE CHUNK]"),
    ];

    /// <summary>
    /// Fast lookup by <c>SortOrder</c> for the reset-single-prompt feature.
    /// Key = SortOrder (1–6).  Value = the original seed.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, PromptSeed> BySortOrder =
        All.ToDictionary(p => p.SortOrder);
}
