namespace CopyCat.Services;

/// <summary>
/// Single source of truth for the six built-in prompt texts.
/// <para>
/// PromptItem.ResetSinglePrompt uses this to restore a built-in to its
/// original content regardless of what was saved to SQLite.
/// Keys are the SortOrder values (0–5) which are stable identifiers.
/// </para>
/// </summary>
public static class BuiltInPrompts
{
    public record PromptSeed(string Title, string Content);

    /// <summary>
    /// Built-in prompt seeds keyed by SortOrder (0 = Code Writer … 5 = Bug Finder).
    /// </summary>
    public static readonly IReadOnlyDictionary<int, PromptSeed> BySortOrder =
        new Dictionary<int, PromptSeed>
        {
            [0] = new(
                "Code Writer",
                "You are an expert software engineer. I will provide code from a repository. " +
                "Implement the requested feature following the existing patterns, architecture, " +
                "and coding conventions. Ensure the solution is clean, maintainable, and integrates " +
                "seamlessly with the existing code.\n\nHere is the repository code:\n\n[PASTE CHUNK]"),

            [1] = new(
                "Code Analyzer",
                "You are an expert code reviewer. Analyze the following code from a repository. " +
                "Identify potential bugs, performance issues, security vulnerabilities, and areas " +
                "for improvement. Provide specific, actionable feedback with examples.\n\n" +
                "Here is the code:\n\n[PASTE CHUNK]"),

            [2] = new(
                "Code Planner",
                "You are a senior software architect. Based on the following codebase, help me " +
                "plan a development strategy. Identify the architecture patterns used, suggest " +
                "improvements, and outline a step-by-step plan for implementing [DESCRIBE YOUR GOAL].\n\n" +
                "Here is the repository code:\n\n[PASTE CHUNK]"),

            [3] = new(
                "Refactor Guide",
                "You are an expert in clean code and refactoring. Review the following code and " +
                "provide a detailed refactoring guide. Focus on improving readability, reducing " +
                "complexity, applying SOLID principles, and modernizing patterns where appropriate.\n\n" +
                "Here is the code:\n\n[PASTE CHUNK]"),

            [4] = new(
                "Test Writer",
                "You are a test-driven development expert. Based on the following code, write " +
                "comprehensive unit tests. Cover edge cases, happy paths, and error scenarios. " +
                "Follow any existing testing patterns; otherwise use best practices for the " +
                "detected framework.\n\nHere is the code:\n\n[PASTE CHUNK]"),

            [5] = new(
                "Bug Finder",
                "You are a debugging expert. Carefully read the following code and find all bugs, " +
                "logical errors, null-reference risks, race conditions, and edge cases that could " +
                "cause failures in production. For each issue, explain the root cause and suggest a fix.\n\n" +
                "Here is the code:\n\n[PASTE CHUNK]"),
        };
}
