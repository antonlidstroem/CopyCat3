namespace CopyCat.Models.Catalog;

/// <summary>
/// Static catalog of folder names that are excluded from chunking by default.
///
/// These are build artefact, dependency, and IDE folders that are never
/// useful to paste into an AI.  The list covers the most common toolchains
/// used by CopyCat users (.NET, Node, Python, Java, Android, Go, etc.).
///
/// Adding a new default exclusion = one new string here.
/// No ViewModel code changes.
/// </summary>
public static class FolderCatalog
{
    /// <summary>
    /// Default excluded folder names, matched case-insensitively against
    /// any segment in a file's path.
    ///
    /// ORDER: most common first so the chip strip reads naturally.
    /// </summary>
    public static readonly string[] DefaultExclusions =
    [
        // .NET
        "bin",
        "obj",
        ".vs",
        "packages",

        // Version control
        ".git",

        // Node / JavaScript
        "node_modules",
        "dist",
        "build",
        ".next",

        // Python
        "__pycache__",

        // Java / Android / Gradle
        ".gradle",
        ".idea",
        "out",
    ];

    /// <summary>
    /// Creates a fresh <see cref="FolderFilter"/> for each default exclusion.
    /// Returns new instances each call (no shared state).
    /// </summary>
    public static IEnumerable<FolderFilter> CreateDefaults()
    {
        foreach (var name in DefaultExclusions)
            yield return new FolderFilter { Name = name, IsExcluded = true };
    }
}
