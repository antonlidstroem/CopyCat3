namespace CopyCat.Models.Catalog;

/// <summary>
/// Static catalog of expert roles available in the guided prompt builder.
///
/// Each role produces a named XML tag block that instructs the AI to
/// analyse the pasted code from a specific expert perspective.
/// The user selects one or more roles; the builder assembles them into
/// a structured prompt with a clear separation of concerns.
///
/// DESIGN DECISION — XML tags, not plain prose
/// ────────────────────────────────────────────
/// Modern frontier models (Claude, GPT-4o, Gemini 1.5) respond well
/// to XML-delimited sections.  Using tags:
///   • Gives each analysis section a clear identity.
///   • Lets the model produce a structured, scannable response.
///   • Avoids role-bleed where a "find bugs" instruction accidentally
///     generates refactoring advice inside the security section.
/// </summary>
public static class PromptRoleCatalog
{
    /// <summary>
    /// A single role definition usable in the guided builder.
    /// </summary>
    /// <param name="Id">Short stable identifier (no spaces).</param>
    /// <param name="Label">Human-readable display label for the chip.</param>
    /// <param name="Icon">Emoji icon shown on the chip.</param>
    /// <param name="XmlTag">The XML tag name wrapping this role's instructions.</param>
    /// <param name="DefaultInstruction">Factory instruction text inside the tag.</param>
    /// <param name="Description">One-sentence tooltip shown next to the chip.</param>
    public sealed record RoleDefinition(
        string Id,
        string Label,
        string Icon,
        string XmlTag,
        string DefaultInstruction,
        string Description);

    public static readonly IReadOnlyList<RoleDefinition> All =
    [
        new("code_review",
            "Code Review",
            "🔍",
            "code_review",
            "Review the code for correctness, clarity, naming conventions, and potential bugs. "
            + "Highlight specific issues with line-level observations where possible.",
            "General quality review: bugs, naming, readability"),

        new("security",
            "Security",
            "🔒",
            "security_review",
            "Identify security vulnerabilities: injection risks, authentication flaws, "
            + "insecure data handling, exposed secrets, and OWASP Top-10 patterns.",
            "Security audit: vulnerabilities, OWASP risks"),

        new("performance",
            "Performance",
            "⚡",
            "performance_review",
            "Identify performance bottlenecks: unnecessary allocations, N+1 queries, "
            + "blocking async calls, inefficient algorithms, and missing caching opportunities.",
            "Performance analysis: allocations, queries, algorithms"),

        new("architecture",
            "Architecture",
            "🏗",
            "architecture_review",
            "Evaluate the structural design against SOLID principles, separation of concerns, "
            + "and domain-driven design. Identify coupling issues and suggest abstractions.",
            "SOLID principles, coupling, design patterns"),

        new("test_suggestions",
            "Test Coverage",
            "🧪",
            "test_suggestions",
            "Suggest a comprehensive set of unit tests covering happy paths, edge cases, "
            + "null inputs, boundary values, and error conditions. Use xUnit + FluentAssertions style.",
            "Unit test ideas: happy paths, edge cases, boundaries"),

        new("documentation",
            "Documentation",
            "📝",
            "documentation",
            "Write XML doc comments for all public types and members. "
            + "Include a markdown summary explaining purpose, key decisions, and usage examples.",
            "XML docs and markdown summary"),

        new("refactor",
            "Refactor",
            "♻",
            "refactor_suggestions",
            "Suggest concrete refactoring improvements: extract methods/classes, reduce duplication, "
            + "simplify control flow, and improve readability. Show before/after examples.",
            "Refactoring: DRY, extract, simplify"),

        new("maintainability",
            "Maintainability",
            "🔧",
            "maintainability_review",
            "Assess long-term maintainability: magic numbers, lack of constants, brittle string comparisons, "
            + "missing null checks, and code that will be hard to change safely.",
            "Long-term maintainability and change safety"),
    ];

    /// <summary>Fast lookup by Id.</summary>
    public static readonly IReadOnlyDictionary<string, RoleDefinition> ById =
        All.ToDictionary(r => r.Id);

    /// <summary>Output format options for the builder.</summary>
    public static readonly IReadOnlyList<(string Id, string Label, string Instruction)> OutputFormats =
    [
        ("free",    "Free text",        ""),
        ("list",    "Numbered list",    "Present your findings as a numbered list with a one-line summary followed by details."),
        ("table",   "Markdown table",   "Present your findings in a markdown table with columns: Issue | Severity | Location | Suggested Fix."),
        ("xml_out", "Structured XML",   "Wrap your response in XML tags matching each analysis section. Use <finding>, <severity>, <location>, and <recommendation> elements."),
    ];
}
