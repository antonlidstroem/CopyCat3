namespace CopyCat.Models.Catalog;

/// <summary>
/// Static catalog of all supported file-type filter definitions.
///
/// WHY THIS EXISTS
/// ───────────────
/// Previously the 29 filter definitions lived inside
/// <c>MainViewModel.InitFileTypeFilters()</c>.  Adding a new language
/// required editing the ViewModel — a violation of the Open/Closed
/// Principle (open for extension, closed for modification).
///
/// With this catalog the data is fully separated from the behaviour.
/// The FilterViewModel simply calls <see cref="CreateDefaults"/> and
/// subscribes to <c>PropertyChanged</c> on each returned item; it never
/// needs to know the concrete list of languages.
///
/// Adding a new language in the future = one new line in <see cref="Entries"/>.
/// No other file needs to change.
///
/// DEFAULT ENABLED EXTENSIONS
/// ──────────────────────────
/// .cs / .xaml / .json / .csproj are enabled by default because CopyCat
/// itself is a .NET MAUI app and this matches the most common first use.
/// Auto-Detect overrides the defaults immediately for any real repo.
/// </summary>
public static class FileTypeCatalog
{
    /// <summary>
    /// Ordered list of (Label, Extensions[], defaultEnabled) tuples.
    /// Keep alphabetical within each language family for readability.
    /// </summary>
    private static readonly (string Label, string[] Extensions, bool DefaultOn)[] Entries =
    [
        // .NET / C#
        (".cs",      [".cs"],               true),
        (".xaml",    [".xaml"],             true),
        (".json",    [".json"],             true),
        (".csproj",  [".csproj"],           true),
        (".razor",   [".razor", ".cshtml"], false),
        (".xml",     [".xml"],              false),

        // Web
        (".html",    [".html", ".htm"],     false),
        (".css",     [".css"],              false),
        (".scss",    [".scss", ".sass"],    false),
        (".js",      [".js", ".mjs"],       false),
        (".jsx",     [".jsx"],              false),
        (".ts",      [".ts", ".tsx"],       false),
        (".vue",     [".vue"],              false),

        // Backend / systems
        (".py",      [".py"],               false),
        (".java",    [".java"],             false),
        (".kt",      [".kt"],               false),
        (".swift",   [".swift"],            false),
        (".go",      [".go"],               false),
        (".rs",      [".rs"],               false),
        (".rb",      [".rb"],               false),
        (".php",     [".php"],              false),
        (".c/.h",    [".c", ".h"],          false),
        (".cpp",     [".cpp", ".hpp"],      false),

        // Data / config / infra
        (".yaml",    [".yaml", ".yml"],     false),
        (".sql",     [".sql"],              false),
        (".proto",   [".proto"],            false),
        (".tf",      [".tf"],               false),
        (".md",      [".md"],               false),
        (".sh/.ps1", [".sh", ".ps1"],       false),
    ];

    /// <summary>
    /// Creates a fresh set of <see cref="FileTypeFilter"/> instances with
    /// their default enabled/disabled states.
    ///
    /// Returns new instances each call so callers can subscribe to
    /// <c>PropertyChanged</c> independently without shared-state bugs.
    /// </summary>
    public static IEnumerable<FileTypeFilter> CreateDefaults()
    {
        foreach (var (label, exts, on) in Entries)
            yield return new FileTypeFilter
            {
                Label = label,
                Extensions = exts.ToList(),  // <-- must be .ToList(), not raw string[]
                IsEnabled = on,
            };
    }

    /// <summary>
    /// Language → companion extension inference used by Auto-Detect.
    ///
    /// When a primary extension is detected in a repo, these companions
    /// are also enabled automatically.  Example: finding .cs files infers
    /// that .csproj and .xml are also relevant.
    ///
    /// Key   = language label from <see cref="Entries"/> (case-insensitive).
    /// Value = additional extensions to enable alongside the primary.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> CompanionInference =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["C#"]         = [".csproj", ".xml"],
            ["TypeScript"] = [".json"],
            ["JavaScript"] = [".json"],
            ["Python"]     = [],
            ["Java"]       = [],
            ["Kotlin"]     = [],
        };
}
