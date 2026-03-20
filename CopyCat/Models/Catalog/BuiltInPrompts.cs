namespace CopyCat.Models.Catalog;

/// <summary>
/// The eight factory-default AI prompts shipped with CopyCat.
///
/// STABLE SORT ORDERS
/// ──────────────────
/// Each prompt has a fixed SortOrder (1–8) that never changes, even after
/// the user edits the prompt's text. This lets workspace-restore reliably
/// re-select the correct prompt by sort order after a DB reset.
///
/// MIGRATION: Users upgrading from v1 (6 prompts) will have 7 and 8
/// automatically added by DatabaseService.SeedPromptsIfEmptyAsync — which
/// now seeds any MISSING built-ins rather than only running when the table
/// is empty. No data is lost.
/// </summary>
public static class BuiltInPrompts
{
    public sealed record PromptSeed(int SortOrder, string Title, string Content);

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

        // ── Multi-role prompts (added in v2) ──────────────────────────────────

        new(7,
            "Multi-Perspective Review",
            "Please analyse the following code from three expert perspectives in sequence.\n\n"
            + "<code_review>\n"
            + "Act as a Senior Developer. Review for correctness, naming, readability, and "
            + "obvious bugs. Flag any code smells or anti-patterns.\n"
            + "</code_review>\n\n"
            + "<security_review>\n"
            + "Act as a Security Engineer. Identify vulnerabilities: injection risks, "
            + "authentication flaws, insecure data handling, exposed secrets, and OWASP Top-10 patterns.\n"
            + "</security_review>\n\n"
            + "<performance_review>\n"
            + "Act as a Performance Engineer. Identify bottlenecks: unnecessary allocations, "
            + "N+1 queries, blocking async calls, and missing caching opportunities.\n"
            + "</performance_review>\n\n"
            + "Conclude with a prioritised list of the top 5 improvements across all three perspectives.\n\n"
            + "[PASTE CHUNK]"),

        new(8,
            "Architect + Implementer",
            "Review the following code wearing two hats in sequence.\n\n"
            + "<architect>\n"
            + "Assess structural decisions: SOLID compliance, separation of concerns, dependency direction, "
            + "and domain boundary clarity. Identify over-coupling and missing abstractions.\n"
            + "</architect>\n\n"
            + "<implementer>\n"
            + "Identify implementation-level issues the architect might miss: null-reference risks, "
            + "missing error handling, resource leaks, incorrect async usage, and edge-case failures.\n"
            + "</implementer>\n\n"
            + "<synthesis>\n"
            + "Summarise the three most impactful improvements that address findings from both perspectives. "
            + "For each, state: What to change, Why it matters, and Rough effort (small/medium/large).\n"
            + "</synthesis>\n\n"
            + "[PASTE CHUNK]"),
    ];

    public static readonly IReadOnlyDictionary<int, PromptSeed> BySortOrder =
        All.ToDictionary(p => p.SortOrder);
}
