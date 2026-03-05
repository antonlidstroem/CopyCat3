using CopyCat.Models;
using System.Text;

namespace CopyCat.Services;

public class ChunkingService : IChunkingService
{
    private const double CharsPerToken  = 4.0;
    private const string FileSeparator  = "\n\n";

    // ── Token estimation ──────────────────────────────────────────────────────
    public int EstimateTokens(string text) =>
        (int)Math.Ceiling(text.Length / CharsPerToken);

    // ── Public entry point ────────────────────────────────────────────────────
    public List<CodeChunk> CreateChunks(
        List<(string Path, string Content)> files,
        int maxTokensPerChunk)
    {
        if (files == null || files.Count == 0) return [];

        // Group files by their detected project (csproj-based)
        var projectRoots = DetectProjectRoots(files);

        var grouped = files
            .GroupBy(f => ResolveProject(f.Path, projectRoots))
            .OrderBy(g => g.Key)
            .ToList();

        var allChunks  = new List<CodeChunk>();
        int globalIndex = 0;

        foreach (var projectGroup in grouped)
        {
            var projectFiles  = projectGroup.OrderBy(f => f.Path).ToList();
            var projectChunks = ChunkProjectFiles(
                projectGroup.Key, projectFiles, maxTokensPerChunk, ref globalIndex);

            allChunks.AddRange(projectChunks);
        }

        return allChunks;
    }

    // ── Project detection ─────────────────────────────────────────────────────
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
            .OrderByDescending(p => p.dir.Length)   // deepest match wins
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
            {
                if (string.Equals(current, dir, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = NormalDir(parent);
        }

        var root = projectRoots.FirstOrDefault(p => p.Dir == "");
        return root.Name ?? "Root";
    }

    private static string NormalDir(string dir) =>
        dir.Replace('\\', '/').TrimEnd('/');

    // ── Chunking ───────────────────────────────────────────────────────────────
    private List<CodeChunk> ChunkProjectFiles(
        string projectName,
        List<(string Path, string Content)> files,
        int maxTokensPerChunk,
        ref int globalIndex)
    {
        var sections = files
            .Select(f => BuildSection(f.Path, f.Content))
            .ToList();

        var result       = new List<CodeChunk>();
        var buffer       = new StringBuilder();
        int bufferTokens = 0;

        foreach (var section in sections)
        {
            int sectionTokens = EstimateTokens(section);

            // Section too large on its own — flush buffer then split by line
            if (sectionTokens > maxTokensPerChunk)
            {
                if (buffer.Length > 0)
                {
                    result.Add(Finalize(globalIndex++, projectName,
                        buffer.ToString(), bufferTokens));
                    buffer.Clear();
                    bufferTokens = 0;
                }

                result.AddRange(
                    SplitLargeSection(projectName, section, maxTokensPerChunk, ref globalIndex));
                continue;
            }

            int sepTokens = buffer.Length > 0 ? EstimateTokens(FileSeparator) : 0;

            // Would overflow — flush
            if (bufferTokens + sepTokens + sectionTokens > maxTokensPerChunk
                && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName,
                    buffer.ToString(), bufferTokens));
                buffer.Clear();
                bufferTokens = 0;
                sepTokens    = 0;
            }

            if (buffer.Length > 0)
            {
                buffer.Append(FileSeparator);
                bufferTokens += sepTokens;
            }

            buffer.Append(section);
            bufferTokens += sectionTokens;
        }

        if (buffer.Length > 0)
            result.Add(Finalize(globalIndex++, projectName,
                buffer.ToString(), bufferTokens));

        return result;
    }

    private List<CodeChunk> SplitLargeSection(
        string projectName,
        string section,
        int    maxTokensPerChunk,
        ref int globalIndex)
    {
        var result = new List<CodeChunk>();
        var lines  = section.Split('\n');
        var buffer = new StringBuilder();
        int tokens = 0;

        foreach (var line in lines)
        {
            var lineWithNl  = line + "\n";
            int lineTokens  = EstimateTokens(lineWithNl);

            if (tokens + lineTokens > maxTokensPerChunk && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName, buffer.ToString(), tokens));
                buffer.Clear();
                tokens = 0;
            }

            buffer.Append(lineWithNl);
            tokens += lineTokens;
        }

        if (buffer.Length > 0)
            result.Add(Finalize(globalIndex++, projectName, buffer.ToString(), tokens));

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
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
        int    estimatedTokens) =>
        new()
        {
            Index           = index,
            ProjectName     = projectName,
            Content         = content,
            EstimatedTokens = estimatedTokens,
            IsCopied        = false
        };
}
