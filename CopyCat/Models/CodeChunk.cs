using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    public int    Index           { get; set; }
    public string ProjectName    { get; set; } = string.Empty;
    public string Content        { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    [NotifyPropertyChangedFor(nameof(SubLabel))]
    protected bool _isCopied;

    public string DisplayLabel =>
        IsCopied
            ? $"✓  Chunk {Index + 1}  ·  {ProjectName}"
            : $"Chunk {Index + 1}  ·  {ProjectName}";

    public string SubLabel =>
        IsCopied
            ? "Kopierad till urklipp"
            : $"~{EstimatedTokens:N0} tokens";

    // Lazily parsed list of file paths contained in this chunk.
    // Extracted from the ==== path ==== header lines in Content.
    // Continuation headers (==== path (forts.) ====) are deduplicated.
    private List<string>? _fileNames;
    public List<string> FileNames => _fileNames ??= ParseFileNames();

    private List<string> ParseFileNames()
    {
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();

        foreach (var line in Content.Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith("==== ") || !t.EndsWith(" ===="))
                continue;

            // Strip the ==== prefix and ==== suffix
            var raw = t[5..^5].Trim();

            // Strip any continuation suffix added by ChunkingService:
            //   "path (forts.)"         – type-level split
            //   "path (forts. rad ~42)" – line-level split
            var fortsIdx = raw.IndexOf(" (forts.", StringComparison.Ordinal);
            if (fortsIdx >= 0)
                raw = raw[..fortsIdx].TrimEnd();

            if (!string.IsNullOrEmpty(raw) && seen.Add(raw))
                names.Add(raw);
        }

        return names;
    }
}
