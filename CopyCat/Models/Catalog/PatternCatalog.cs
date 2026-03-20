namespace CopyCat.Models.Catalog;

/// <summary>
/// Static catalog of default file-exclusion wildcard patterns.
///
/// Patterns are matched case-insensitively against file names (not paths).
/// '*' is the only supported wildcard character.
///
/// The first three entries are enabled by default because they match
/// auto-generated files that are never useful to an AI reviewer.
/// The remaining four are opt-in (tests, specs, mocks) because some
/// users explicitly want to review test coverage.
/// </summary>
public static class PatternCatalog
{
    private static readonly (string Pattern, bool DefaultOn)[] Entries =
    [
        // Always-on: generated files add noise, not signal
        ("*.min.*",       true),
        ("*.generated.*", true),
        ("*.Designer.*",  true),

        // Opt-in: tests are often useful to review separately
        ("*Test*",        false),
        ("*Spec*",        false),
        ("*_test.*",      false),
        ("*Mock*",        false),
    ];

    /// <summary>
    /// Creates a fresh <see cref="FilePatternFilter"/> for each default pattern.
    /// Returns new instances each call (no shared state).
    /// </summary>
    public static IEnumerable<FilePatternFilter> CreateDefaults()
    {
        foreach (var (pattern, on) in Entries)
            yield return new FilePatternFilter { Pattern = pattern, IsEnabled = on };
    }
}
