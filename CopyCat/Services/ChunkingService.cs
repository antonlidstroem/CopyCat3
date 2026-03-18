using CopyCat.Models;
using System.Text;

namespace CopyCat.Services;

/// <summary>
/// Packs source files into token-bounded <see cref="CodeChunk"/> objects.
///
/// Phase 1 bug fixes:
///   B1 – Header token cost is now deducted from the effective budget before
///        line-level splitting, so split chunks never exceed maxTokensPerChunk.
///   B2 – FileSeparator accounting verified correct; explanatory comment added.
/// </summary>
public class ChunkingService : IChunkingService
{
    private const double CharsPerToken = 4.0;
    private const string FileSeparator = "\n\n";

    public int EstimateTokens(string text) => EstimateTokensStatic(text);

    private static int EstimateTokensStatic(string text) =>
        (int)Math.Ceiling(text.Length / CharsPerToken);

    /// <summary>
    /// Returns the token cost of the ==== path ==== header line including its
    /// trailing newline. Pre-deducted from the budget in SplitLargeSection.
    /// </summary>
    private static int HeaderTokens(string path) =>
        EstimateTokensStatic($"==== {path} ====\n");

    public List<CodeChunk> CreateChunks(
        List<(string Path, string Content)> files,
        int maxTokensPerChunk,
        CancellationToken cancellationToken = default)
    {
        if (files == null || files.Count == 0) return [];

        var projectRoots = DetectProjectRoots(files);

        var grouped = files
            .GroupBy(f => ResolveProject(f.Path, projectRoots))
            .OrderBy(g => g.Key)
            .ToList();

        var allChunks   = new List<CodeChunk>();
        int globalIndex = 0;

        foreach (var projectGroup in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectFiles  = projectGroup.OrderBy(f => f.Path).ToList();
            var projectChunks = ChunkProjectFiles(
                projectGroup.Key, projectFiles, maxTokensPerChunk,
                ref globalIndex, cancellationToken);
            allChunks.AddRange(projectChunks);
        }

        return allChunks;
    }

    private static List<(string Dir, string Name)> DetectProjectRoots(
        List<(string Path, string Content)> files)
    {
        return files
            .Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var dir  = NormalDir(Path.GetDirectoryName(f.Path) ?? "");
                var name = Path.GetFileNameWithoutExtension(f.Path);
                return (dir, name);
            })
            .OrderByDescending(p => p.dir.Length)
            .ToList();
    }

    private static string ResolveProject(
        string filePath,
        List<(string Dir, string Name)> projectRoots)
    {
        if (projectRoots.Count == 0) return "Root";
        var current = NormalDir(Path.GetDirectoryName(filePath) ?? "");
        while (true)
        {
            foreach (var (dir, name) in projectRoots)
                if (string.Equals(current, dir, StringComparison.OrdinalIgnoreCase))
                    return name;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = NormalDir(parent);
        }
        var root = projectRoots.FirstOrDefault(p => p.Dir == "");
        return root.Name ?? "Root";
    }

    private static string NormalDir(string dir) =>
        dir.Replace('\\', '/').TrimEnd('/');

    private List<CodeChunk> ChunkProjectFiles(
        string projectName,
        List<(string Path, string Content)> files,
        int maxTokensPerChunk,
        ref int globalIndex,
        CancellationToken cancellationToken)
    {
        var result       = new List<CodeChunk>();
        var buffer       = new StringBuilder();
        int bufferTokens = 0;
        var bufferFiles  = new List<(string Path, string Content)>();

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file          = files[i];
            var section       = BuildSection(file.Path, file.Content);
            int sectionTokens = EstimateTokensStatic(section);

            if (sectionTokens > maxTokensPerChunk)
            {
                if (buffer.Length > 0)
                {
                    result.Add(Finalize(globalIndex++, projectName,
                        buffer.ToString(), bufferTokens, bufferFiles));
                    buffer.Clear(); bufferTokens = 0; bufferFiles = [];
                }
                result.AddRange(SplitLargeSection(projectName, file, section,
                    maxTokensPerChunk, ref globalIndex, cancellationToken));
                continue;
            }

            // B2: sepTokens is 0 when buffer is empty (no preceding content to
            // separate from), and resets to 0 after each flush because the new
            // buffer starts empty. Logic is correct as-is.
            int sepTokens = buffer.Length > 0 ? EstimateTokensStatic(FileSeparator) : 0;

            if (bufferTokens + sepTokens + sectionTokens > maxTokensPerChunk && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName,
                    buffer.ToString(), bufferTokens, bufferFiles));
                buffer.Clear(); bufferTokens = 0; bufferFiles = []; sepTokens = 0;
            }

            if (buffer.Length > 0) { buffer.Append(FileSeparator); bufferTokens += sepTokens; }
            buffer.Append(section);
            bufferTokens += sectionTokens;
            bufferFiles.Add(file);
        }

        if (buffer.Length > 0)
            result.Add(Finalize(globalIndex++, projectName,
                buffer.ToString(), bufferTokens, bufferFiles));

        return result;
    }

    /// <summary>
    /// Splits a single large section at the line level.
    ///
    /// B1 FIX: the effective budget is maxTokensPerChunk minus the header
    /// token cost. The ==== header is the first line of <paramref name="section"/>
    /// and counts against the budget. Without this deduction, the header itself
    /// can push the first accumulated chunk fractionally over the limit.
    /// </summary>
    private List<CodeChunk> SplitLargeSection(
        string projectName,
        (string Path, string Content) file,
        string section,
        int    maxTokensPerChunk,
        ref int globalIndex,
        CancellationToken cancellationToken)
    {
        var result = new List<CodeChunk>();
        var lines  = section.Split('\n');
        var buffer = new StringBuilder();
        int tokens = 0;

        // Deduct header cost so line accumulation never exceeds the limit.
        int effectiveBudget = Math.Max(1, maxTokensPerChunk - HeaderTokens(file.Path));

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineWithNl = line + "\n";
            int lineTokens = EstimateTokensStatic(lineWithNl);

            if (lineTokens > effectiveBudget)
            {
                if (buffer.Length > 0)
                {
                    result.Add(Finalize(globalIndex++, projectName,
                        buffer.ToString(), tokens, [file]));
                    buffer.Clear(); tokens = 0;
                }
                result.AddRange(SplitLongLine(projectName, file, lineWithNl,
                    maxTokensPerChunk, ref globalIndex));
                continue;
            }

            if (tokens + lineTokens > effectiveBudget && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName,
                    buffer.ToString(), tokens, [file]));
                buffer.Clear(); tokens = 0;
            }

            buffer.Append(lineWithNl);
            tokens += lineTokens;
        }

        if (buffer.Length > 0)
            result.Add(Finalize(globalIndex++, projectName,
                buffer.ToString(), tokens, [file]));

        return result;
    }

    private static List<CodeChunk> SplitLongLine(
        string projectName,
        (string Path, string Content) file,
        string line,
        int    maxTokensPerChunk,
        ref int globalIndex)
    {
        var result   = new List<CodeChunk>();
        int maxChars = (int)(maxTokensPerChunk * CharsPerToken);
        int offset   = 0;

        while (offset < line.Length)
        {
            int length  = Math.Min(maxChars, line.Length - offset);
            var segment = line.Substring(offset, length);
            result.Add(Finalize(globalIndex++, projectName,
                segment, EstimateTokensStatic(segment), [file]));
            offset += length;
        }

        return result;
    }

    private static string BuildSection(string path, string content)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"==== {path} ====");
        sb.AppendLine(content.TrimEnd());
        return sb.ToString();
    }

    private static CodeChunk Finalize(
        int    index,
        string projectName,
        string content,
        int    estimatedTokens,
        List<(string Path, string Content)> sourceFiles) =>
        new()
        {
            Index           = index,
            ProjectName     = projectName,
            Content         = content,
            EstimatedTokens = estimatedTokens,
            IsCopied        = false,
            FileEntries     = sourceFiles
                .Select(f => new ChunkFile { Path = f.Path, Content = f.Content })
                .ToList(),
        };
}
