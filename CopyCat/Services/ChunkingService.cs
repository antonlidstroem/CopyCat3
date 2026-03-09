using CopyCat.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace CopyCat.Services;

public class ChunkingService : IChunkingService
{
    private const double CharsPerToken = 4.0;
    private const string FileSeparator = "\n\n";

    // Matchar top-level typdeklarationer (class / struct / record / interface / enum).
    // Täcker:
    //   - File-scoped namespace (0 indragning): public class Foo
    //   - Traditionell namespace-block (4 spaces/1 tab): [4 sp]public class Foo
    //   - Alla kombinationer av modifierare (public, partial, abstract, sealed …)
    // Inre/nästade klasser (>=8 spaces) matchas INTE — de stannar i förälderklassens block.
    private static readonly Regex TopLevelTypeDeclRegex = new(
        @"^[ \t]*" + // Tillåt vilket indrag som helst
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

        var allChunks   = new List<CodeChunk>();
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
        var result       = new List<CodeChunk>();
        var buffer       = new StringBuilder();
        int bufferTokens = 0;

        foreach (var (path, content) in files)
        {
            string section       = BuildSection(path, content);
            int    sectionTokens = EstimateTokens(section);

            if (sectionTokens > maxTokensPerChunk)
            {
                // Filen ryms inte i en chunk — töm buffert och splittra på klassgränser.
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

            // Filen ryms — packa greedily i bufferten.
            int sepTokens = buffer.Length > 0 ? EstimateTokens(FileSeparator) : 0;
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

    // ─────────────────────────────────────────────────────────────────
    //  Splittra en stor fil på klassgränser
    // ─────────────────────────────────────────────────────────────────

    private List<CodeChunk> SplitLargeFile(
        string  projectName,
        string  filePath,
        string  section,
        int     maxTokensPerChunk,
        ref int globalIndex)
    {
        var units = ExtractTypeUnits(section, filePath);

        var result       = new List<CodeChunk>();
        var buffer       = new StringBuilder();
        int bufferTokens = 0;

        foreach (var unit in units)
        {
            int unitTokens = EstimateTokens(unit);

            if (unitTokens > maxTokensPerChunk)
            {
                // En enskild klass är för stor — sista utväg: splittra rad för rad.
                if (buffer.Length > 0)
                {
                    result.Add(Finalize(globalIndex++, projectName,
                        buffer.ToString(), bufferTokens));
                    buffer.Clear();
                    bufferTokens = 0;
                }
                result.AddRange(
                    SplitByLines(projectName, unit, maxTokensPerChunk, ref globalIndex));
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
                sepTokens    = 0;
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
    //  Extrahera typenheter med korrekt block-start-detektering
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delar en fil-sektion i en typ-enhet per klass/interface/struct/record/enum.
    ///
    /// NYCKELFIXAR jämfört med tidigare versioner:
    ///
    /// 1. FindBlockStart() — varje klass-block börjar vid den närmast föregående
    ///    [Attribut], /// doc-kommentaren eller // kommentaren, INTE vid klass-
    ///    deklarationsraden. Detta säkerställer att [Serializable], [ApiController]
    ///    etc. hamnar i RÄTT enhet och inte som en orphan i slutet av föregående enhet.
    ///
    /// 2. Preamble extraheras från lines[1..blockStarts[0]-1], d.v.s. EXKLUSIVE
    ///    det första klass-blockets attribut. Tidigare togs preamble fram till
    ///    class-deklarationsraden vilket råkade inkludera den första klassens
    ///    attribut i preamble — och därmed i VARJE fortsättningsenhet.
    ///
    /// GARANTI: Ingen rad tappas bort. Ingen klass bryts mitt i.
    /// </summary>
    private static List<string> ExtractTypeUnits(string section, string filePath)
    {
        // Normalisera radslut (kritiskt — GitHub-repon har ofta \r\n).
        var text  = section.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');

        // Hitta radindex för top-level typdeklarationer (starta på i=1; lines[0] = header).
        var classBoundaries = new List<int>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (TopLevelTypeDeclRegex.IsMatch(lines[i]))
                classBoundaries.Add(i);
        }

        // Inga eller bara en typdekleration — returnera hela sektionen orörd.
        if (classBoundaries.Count <= 1)
            return [text];

        // Beräkna det "verkliga" blockstarten för varje klass:
        // gå bakåt från class-raden över [Attribut] och doc-kommentarer.
        var blockStarts = new int[classBoundaries.Count];
        for (int ci = 0; ci < classBoundaries.Count; ci++)
            blockStarts[ci] = FindBlockStart(lines, classBoundaries[ci]);

        // Preamble = lines[1 .. blockStarts[0]-1]
        // (using-satser, namespace, globala attribut — UTAN den första klassens attribut)
        var preambleSb = new StringBuilder();
        for (int i = 1; i < blockStarts[0]; i++)
        {
            preambleSb.Append(lines[i]);
            preambleSb.Append('\n');
        }
        string preamble = preambleSb.ToString();

        var result = new List<string>();

        for (int ci = 0; ci < classBoundaries.Count; ci++)
        {
            int blockStart = blockStarts[ci];
            // Blocket slutar precis innan nästa klass-blocks attribut börjar.
            int blockEnd   = (ci + 1 < classBoundaries.Count)
                ? blockStarts[ci + 1]
                : lines.Length;

            var sb = new StringBuilder();

            if (ci == 0)
            {
                // Enhet 0: original fil-header (lines[0]) + preamble + klass 1-blocket.
                // = lines[0 .. blockEnd-1]
                for (int i = 0; i < blockEnd; i++)
                {
                    sb.Append(lines[i]);
                    if (i < blockEnd - 1) sb.Append('\n');
                }
            }
            else
            {
                // Enhet k: fortsättningsheader + preamble (usings/namespace) + klass k-blocket.
                sb.Append($"==== {filePath} (forts.) ====");
                sb.Append('\n');
                if (preamble.Length > 0)
                    sb.Append(preamble); // avslutas redan med \n

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

    /// <summary>
    /// Hittar det verkliga startindexet för ett klass-block genom att gå bakåt
    /// från klass-deklarationsraden över [Attribut]-rader och kommentarer.
    ///
    /// Stannar vid:
    ///   - En blank rad (den stannar hos föregående block).
    ///   - En rad som INTE börjar med '[', '///' eller '//'.
    ///     (t.ex. '}' från föregående klass, en namespace-rad, etc.)
    ///
    /// Det innebär att:
    ///   [Serializable]
    ///   public class Foo    ← blockStart pekar på [Serializable]-raden, inte Foo
    ///
    ///   /// <summary>...</summary>
    ///   [ApiController]
    ///   public class Bar    ← blockStart pekar på ///-raden
    ///
    /// Inre rader i föregående klass (}, {, kod) avbryter sökningen direkt.
    /// </summary>
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
                // Blank rad, '}', kod etc. — blockstart är fastställt.
                break;
            }
        }
        return blockStart;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Fallback: splittra rad för rad
    //  Används BARA när en enskild klass överstiger maxTokensPerChunk.
    // ─────────────────────────────────────────────────────────────────

    private List<CodeChunk> SplitByLines(
        string  projectName,
        string  unit,
        int     maxTokensPerChunk,
        ref int globalIndex)
    {
        var result = new List<CodeChunk>();
        var lines  = unit.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
                    SplitLongLine(projectName, lineWithNl, maxTokensPerChunk, ref globalIndex));
                continue;
            }

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

    private List<CodeChunk> SplitLongLine(
        string  projectName,
        string  line,
        int     maxTokensPerChunk,
        ref int globalIndex)
    {
        var result   = new List<CodeChunk>();
        int maxChars = (int)(maxTokensPerChunk * CharsPerToken);
        int offset   = 0;

        while (offset < line.Length)
        {
            int    length  = Math.Min(maxChars, line.Length - offset);
            string segment = line.Substring(offset, length);
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
        sb.AppendLine($"==== {path} ===="); // AppendLine för header-raden är ok
        sb.Append(content.TrimEnd());       // Append (ej AppendLine) — undvik extra radbrytning
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
            IsCopied        = false,
        };
}
