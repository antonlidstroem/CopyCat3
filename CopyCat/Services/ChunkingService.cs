using CopyCat.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace CopyCat.Services;

public class ChunkingService : IChunkingService
{
    private const double CharsPerToken = 3.0;
    private const string FileSeparator = "\n\n";

    // Matchar typdeklarationer (class / struct / record / interface / enum) på valfritt djup.
    // Klammerdjups-filtret i ExtractTypeUnits avgör om det är en toppnivå-deklaration
    // eller en nästlad klass — regex:en behöver därför inte begränsa indraget.
    private static readonly Regex TypeDeclRegex = new(
        @"^[ \t]*" +
        @"(?:(?:public|private|internal|protected|file|static|abstract|sealed|partial|readonly|unsafe|new|override|virtual)\s+)*" +
        @"(?:class|struct|record|interface|enum)\s+\w",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // ─────────────────────────────────────────────────────────────────
    //  Publik API
    // ─────────────────────────────────────────────────────────────────

    public int EstimateTokens(string text) =>
        (int)Math.Ceiling(text.Length / CharsPerToken);

    public List<CodeChunk> CreateChunks(
        List<(string Path, string Content)> files,
        int maxTokensPerChunk)
    {
        if (files == null || files.Count == 0) return [];

        var projectRoots = DetectProjectRoots(files);
        var grouped = files
            .GroupBy(f => ResolveProject(f.Path, projectRoots))
            .OrderBy(g => g.Key)
            .ToList();

        var allChunks = new List<CodeChunk>();
        int globalIndex = 0;

        foreach (var projectGroup in grouped)
        {
            var projectFiles = projectGroup.OrderBy(f => f.Path).ToList();
            var projectChunks = ChunkProjectFiles(
                projectGroup.Key, projectFiles, maxTokensPerChunk, ref globalIndex);
            allChunks.AddRange(projectChunks);
        }

        return allChunks;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Projektdetektering
    // ─────────────────────────────────────────────────────────────────

    private static List<(string Dir, string Name)> DetectProjectRoots(
        List<(string Path, string Content)> files)
    {
        return files
            .Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var dir = NormalDir(Path.GetDirectoryName(f.Path) ?? "");
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

    // ─────────────────────────────────────────────────────────────────
    //  Kärn-chunkning per projekt
    // ─────────────────────────────────────────────────────────────────

    private List<CodeChunk> ChunkProjectFiles(
        string projectName,
        List<(string Path, string Content)> files,
        int maxTokensPerChunk,
        ref int globalIndex)
    {
        var result = new List<CodeChunk>();
        var buffer = new StringBuilder();
        int bufferTokens = 0;

        foreach (var (path, content) in files)
        {
            string section = BuildSection(path, content);
            int sectionTokens = EstimateTokens(section);

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
                    SplitLargeFile(projectName, path, section, maxTokensPerChunk, ref globalIndex));
                continue;
            }

            int sepTokens = buffer.Length > 0 ? EstimateTokens(FileSeparator) : 0;
            if (bufferTokens + sepTokens + sectionTokens > maxTokensPerChunk
                && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName,
                    buffer.ToString(), bufferTokens));
                buffer.Clear();
                bufferTokens = 0;
                sepTokens = 0;
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

    // ─────────────────────────────────────────────────────────────────
    //  Splittra en stor fil på klassgränser
    // ─────────────────────────────────────────────────────────────────

    private List<CodeChunk> SplitLargeFile(
        string projectName,
        string filePath,
        string section,
        int maxTokensPerChunk,
        ref int globalIndex)
    {
        var units = ExtractTypeUnits(section, filePath);

        var result = new List<CodeChunk>();
        var buffer = new StringBuilder();
        int bufferTokens = 0;

        foreach (var unit in units)
        {
            int unitTokens = EstimateTokens(unit);

            if (unitTokens > maxTokensPerChunk)
            {
                if (buffer.Length > 0)
                {
                    result.Add(Finalize(globalIndex++, projectName,
                        buffer.ToString(), bufferTokens));
                    buffer.Clear();
                    bufferTokens = 0;
                }
                // Skicka med filePath så SplitByLines kan sätta rätt fortsättningsheader.
                result.AddRange(
                    SplitByLines(projectName, filePath, unit, maxTokensPerChunk, ref globalIndex));
                continue;
            }

            int sepTokens = buffer.Length > 0 ? EstimateTokens(FileSeparator) : 0;
            if (bufferTokens + sepTokens + unitTokens > maxTokensPerChunk
                && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName,
                    buffer.ToString(), bufferTokens));
                buffer.Clear();
                bufferTokens = 0;
                sepTokens = 0;
            }
            if (buffer.Length > 0)
            {
                buffer.Append(FileSeparator);
                bufferTokens += sepTokens;
            }
            buffer.Append(unit);
            bufferTokens += unitTokens;
        }

        if (buffer.Length > 0)
            result.Add(Finalize(globalIndex++, projectName,
                buffer.ToString(), bufferTokens));

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Extrahera typenheter med klammerdjups-filtrering
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delar en fil-sektion i en enhet per toppnivå-klass/struct/record/interface/enum.
    ///
    /// KÄRN-ALGORITM — klammerdjupsspårning:
    ///
    /// Spårar klammerdjupet rad för rad. Den FÖRSTA typdeklarationen fastställer
    /// topLevelDepth. Efterföljande typdeklarationer är toppnivå-syskon om och bara
    /// om braceDepth == topLevelDepth. Djupare deklarationer ignoreras (nästlade klasser).
    ///
    /// Korrekt för:
    ///   File-scoped namespace  → toppnivå-klass på djup 0, nästlad på djup 1+
    ///   Traditionellt namespace { } → toppnivå på djup 1, nästlad på djup 2+
    /// </summary>
    private static List<string> ExtractTypeUnits(string section, string filePath)
    {
        var text = section.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');

        // ── Fas 1: hitta toppnivå-klassgränser via klammerdjupsspårning ──────
        var classBoundaries = new List<int>();
        int braceDepth = 0;
        int topLevelDepth = -1;

        for (int i = 1; i < lines.Length; i++)
        {
            if (TypeDeclRegex.IsMatch(lines[i]))
            {
                if (topLevelDepth < 0)
                {
                    topLevelDepth = braceDepth;
                    classBoundaries.Add(i);
                }
                else if (braceDepth == topLevelDepth)
                {
                    classBoundaries.Add(i);
                }
            }

            foreach (char c in lines[i])
            {
                if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
            }
        }

        if (classBoundaries.Count <= 1)
            return [text];

        // ── Fas 2: beräkna verkliga blockstarter ──────────────────────────────
        var blockStarts = new int[classBoundaries.Count];
        for (int ci = 0; ci < classBoundaries.Count; ci++)
            blockStarts[ci] = FindBlockStart(lines, classBoundaries[ci]);

        var preambleSb = new StringBuilder();
        for (int i = 1; i < blockStarts[0]; i++)
        {
            preambleSb.Append(lines[i]);
            preambleSb.Append('\n');
        }
        string preamble = preambleSb.ToString();

        // ── Fas 3: bygg en enhet per toppnivå-klass ──────────────────────────
        var result = new List<string>();

        for (int ci = 0; ci < classBoundaries.Count; ci++)
        {
            int blockStart = blockStarts[ci];
            int blockEnd = (ci + 1 < classBoundaries.Count)
                ? blockStarts[ci + 1]
                : lines.Length;

            var sb = new StringBuilder();

            if (ci == 0)
            {
                for (int i = 0; i < blockEnd; i++)
                {
                    sb.Append(lines[i]);
                    if (i < blockEnd - 1) sb.Append('\n');
                }
            }
            else
            {
                sb.Append($"==== {filePath} (forts.) ====\n");
                if (preamble.Length > 0)
                    sb.Append(preamble);

                for (int i = blockStart; i < blockEnd; i++)
                {
                    sb.Append(lines[i]);
                    if (i < blockEnd - 1) sb.Append('\n');
                }
            }

            result.Add(sb.ToString());
        }

        return result;
    }

    private static int FindBlockStart(string[] lines, int classLineIndex)
    {
        int blockStart = classLineIndex;
        for (int i = classLineIndex - 1; i >= 1; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') ||
                trimmed.StartsWith("///") ||
                trimmed.StartsWith("//"))
            {
                blockStart = i;
            }
            else
            {
                break;
            }
        }
        return blockStart;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Fallback: splittra rad för rad
    //
    //  ROTORSAKSFIX: Tidigare skapades continuation-chunks utan ==== header ====.
    //  Det fick resten av stora filer (t.ex. ChunkingService.cs, MainViewModel.cs)
    //  att se ut som "saknad" kod — den fanns i nästa chunk men utan filkontext.
    //  Nu prefixas varje continuation-chunk med ==== path (forts. rad N) ====.
    // ─────────────────────────────────────────────────────────────────

    private List<CodeChunk> SplitByLines(
        string projectName,
        string filePath,
        string unit,
        int maxTokensPerChunk,
        ref int globalIndex)
    {
        var result = new List<CodeChunk>();
        var lines = unit.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Räkna rader vi faktiskt skriver ut för att ge ett meningsfullt rad-nummer i headern.
        int continuationLine = 1;

        var buffer = new StringBuilder();
        int tokens = 0;

        foreach (var line in lines)
        {
            var lineWithNl = line + "\n";
            int lineTokens = EstimateTokens(lineWithNl);

            if (lineTokens > maxTokensPerChunk)
            {
                if (buffer.Length > 0)
                {
                    result.Add(Finalize(globalIndex++, projectName, buffer.ToString(), tokens));
                    buffer.Clear();
                    tokens = 0;
                }
                result.AddRange(
                    SplitLongLine(projectName, filePath, lineWithNl, maxTokensPerChunk, ref globalIndex));
                continuationLine++;
                continue;
            }

            if (tokens + lineTokens > maxTokensPerChunk && buffer.Length > 0)
            {
                result.Add(Finalize(globalIndex++, projectName, buffer.ToString(), tokens));
                buffer.Clear();
                tokens = 0;

                // Continuation-header: visar vilken fil och ungefär var i filen vi är.
                var contHeader = $"==== {filePath} (forts. rad ~{continuationLine}) ====\n";
                buffer.Append(contHeader);
                tokens += EstimateTokens(contHeader);
            }

            buffer.Append(lineWithNl);
            tokens += lineTokens;
            continuationLine++;
        }

        if (buffer.Length > 0)
            result.Add(Finalize(globalIndex++, projectName, buffer.ToString(), tokens));

        return result;
    }

    private List<CodeChunk> SplitLongLine(
        string projectName,
        string filePath,
        string line,
        int maxTokensPerChunk,
        ref int globalIndex)
    {
        var result = new List<CodeChunk>();
        int maxChars = (int)(maxTokensPerChunk * CharsPerToken);
        int offset = 0;

        while (offset < line.Length)
        {
            int length = Math.Min(maxChars, line.Length - offset);
            string segment = offset == 0
                ? line.Substring(offset, length)
                : $"==== {filePath} (forts.) ====\n" + line.Substring(offset, length);
            result.Add(Finalize(globalIndex++, projectName,
                segment, EstimateTokens(segment)));
            offset += length;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Hjälpmetoder
    // ─────────────────────────────────────────────────────────────────

    private static string BuildSection(string path, string content)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"==== {path} ====");
        sb.Append(content.TrimEnd());
        return sb.ToString();
    }

    private static CodeChunk Finalize(
        int index,
        string projectName,
        string content,
        int estimatedTokens) =>
        new()
        {
            Index = index,
            ProjectName = projectName,
            Content = content,
            EstimatedTokens = estimatedTokens,
            IsCopied = false,
        };
}
