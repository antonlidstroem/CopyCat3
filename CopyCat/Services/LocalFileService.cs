namespace CopyCat.Services;

public class LocalFileService : ILocalFileService
{
    public async Task<List<(string Path, string Content)>> ReadFilesAsync(
        string              localPath,
        IEnumerable<string> extensions,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns,
        IProgress<string>?  progress          = null,
        CancellationToken   cancellationToken = default)
    {
        // Normalise path
        var trimmed = localPath.Trim().Trim('"');

        // If a .sln or .csproj was provided, use its directory
        string baseDir;
        if (trimmed.EndsWith(".sln",    StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            baseDir = Path.GetDirectoryName(trimmed)
                      ?? throw new DirectoryNotFoundException($"Cannot determine directory for: {trimmed}");
        }
        else
        {
            baseDir = trimmed;
        }

        if (!Directory.Exists(baseDir))
            throw new DirectoryNotFoundException(
                $"Directory not found: {baseDir}\n" +
                "Make sure the path exists and is accessible.");

        var extSet = extensions
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();

        var excludedFolderSet = excludedFolders
            .Select(f => f.ToLowerInvariant().Trim('/'))
            .ToHashSet();

        var patternList = excludedFilePatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        progress?.Report($"Scanning {baseDir}…");

        var allFiles = Directory
            .EnumerateFiles(baseDir, "*", SearchOption.AllDirectories)
            .ToList();

        progress?.Report($"Found {allFiles.Count} files, filtering…");

        var results = new List<(string Path, string Content)>();

        foreach (var fullPath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(baseDir, fullPath)
                                   .Replace('\\', '/');

            if (IsInExcludedFolder(relativePath, excludedFolderSet)) continue;

            var ext = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
            if (!extSet.Contains(ext)) continue;

            var fileName = System.IO.Path.GetFileName(fullPath);
            if (patternList.Count > 0 && MatchesAnyPattern(fileName, patternList)) continue;

            try
            {
                var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                results.Add((relativePath, content));
            }
            catch
            {
                results.Add((relativePath, $"// [Could not read: {relativePath}]"));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "No files found with the selected file types. " +
                "Check that the correct file types are enabled and that folder filters are not blocking everything.");

        progress?.Report($"Done! {results.Count} local files loaded.");
        return results.OrderBy(f => f.Path).ToList();
    }

    private static bool IsInExcludedFolder(string relativePath, HashSet<string> excluded)
    {
        var parts = relativePath.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
            if (excluded.Contains(parts[i].ToLowerInvariant()))
                return true;
        return false;
    }

    private static bool MatchesAnyPattern(string fileName, List<string> patterns)
    {
        foreach (var p in patterns)
            if (MatchesGlob(fileName, p))
                return true;
        return false;
    }

    private static bool MatchesGlob(string text, string pattern)
    {
        var t    = text.ToLowerInvariant();
        var p    = pattern.ToLowerInvariant();
        var parts = p.Split('*');

        if (parts.Length == 1) return t.Equals(p, StringComparison.Ordinal);

        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            int found = t.IndexOf(parts[i], pos, StringComparison.Ordinal);
            if (found < 0) return false;
            if (i == 0 && !p.StartsWith('*') && found != 0) return false;
            pos = found + parts[i].Length;
        }
        if (!p.EndsWith('*') && parts[^1].Length > 0 && !t.EndsWith(parts[^1]))
            return false;
        return true;
    }
}
