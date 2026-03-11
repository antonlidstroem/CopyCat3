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
        var trimmed = localPath.Trim().Trim('"');

        // If a solution or project file was provided, use its directory
        string baseDir;
        if (trimmed.EndsWith(".sln",    StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            baseDir = System.IO.Path.GetDirectoryName(trimmed)
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

        // BUG FIX #13: Directory.EnumerateFiles with AllDirectories throws
        // UnauthorizedAccessException if any subdirectory is inaccessible
        // (very common on macOS/iOS sandboxes and Windows protected folders),
        // failing the entire scan.  We now walk the tree manually so that
        // locked directories are skipped rather than crashing the operation.
        var allFiles = EnumerateFilesSafe(baseDir, excludedFolderSet, cancellationToken);

        var results = new List<(string Path, string Content)>();
        int scanned = 0;

        await foreach (var fullPath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = System.IO.Path.GetRelativePath(baseDir, fullPath)
                                             .Replace('\\', '/');

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

            scanned++;
            if (scanned % 50 == 0)
                progress?.Report($"Scanned {scanned} files…");
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "No files found with the selected file types. " +
                "Check that the correct file types are enabled and that folder filters are not blocking everything.");

        progress?.Report($"Done! {results.Count} local files loaded.");
        return results.OrderBy(f => f.Path).ToList();
    }

    /// <summary>
    /// Recursively enumerates files, silently skipping any directories that
    /// raise UnauthorizedAccessException or other IO errors.
    /// </summary>
    private static async IAsyncEnumerable<string> EnumerateFilesSafe(
        string              root,
        HashSet<string>     excludedFolderSet,
        CancellationToken   cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = queue.Dequeue();

            // Yield files in this directory
            IEnumerable<string> files;
            try   { files = Directory.EnumerateFiles(current); }
            catch { continue; }   // skip inaccessible directory

            foreach (var file in files)
                yield return file;

            // Queue subdirectories, respecting the excluded-folder list
            IEnumerable<string> subdirs;
            try   { subdirs = Directory.EnumerateDirectories(current); }
            catch { continue; }

            foreach (var sub in subdirs)
            {
                var name = System.IO.Path.GetFileName(sub).ToLowerInvariant();
                if (!excludedFolderSet.Contains(name))
                    queue.Enqueue(sub);
            }

            // Yield control periodically to keep the UI responsive
            await Task.Yield();
        }
    }

    // ── Pattern helpers ────────────────────────────────────────────────────

    private static bool MatchesAnyPattern(string fileName, List<string> patterns)
    {
        foreach (var p in patterns)
            if (MatchesGlob(fileName, p))
                return true;
        return false;
    }

    private static bool MatchesGlob(string text, string pattern)
    {
        var t     = text.ToLowerInvariant();
        var p     = pattern.ToLowerInvariant();
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
