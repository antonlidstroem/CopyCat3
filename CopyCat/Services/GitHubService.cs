using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace CopyCat.Services;

public class GitHubService : IGitHubService
{
    private readonly IHttpClientFactory _httpFactory;

    // Cache compiled glob→regex conversions so we don't recompile on every file.
    // Key: glob pattern string. Value: compiled Regex.
    private static readonly ConcurrentDictionary<string, Regex> GlobCache = new();

    public GitHubService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public async Task<List<(string Path, string Content)>> FetchFilesAsync(
        string              repoUrl,
        IEnumerable<string> extensions,
        string?             accessToken,
        string              branch,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns,
        IProgress<string>?  progress          = null,
        CancellationToken   cancellationToken = default)
    {
        var (owner, repo) = GitHubUrlParser.Parse(repoUrl);

        var extSet = extensions
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();

        var excludedSet = excludedFolders
            .Select(f => f.ToLowerInvariant().Trim('/'))
            .ToHashSet();

        // Materialise once; filter out blank/null entries defensively.
        var patternList = excludedFilePatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        bool useApi = !string.IsNullOrWhiteSpace(accessToken);

        progress?.Report($"Ansluter till {owner}/{repo}…");

        var trimmedBranch = branch.Trim();
        var candidates = string.IsNullOrWhiteSpace(trimmedBranch)
            ? new[] { "main", "master", "develop" }
            : new[] { trimmedBranch }
              .Concat(new[] { "main", "master", "develop" }
                  .Where(b => !b.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase)))
              .ToArray();

        byte[]? zipBytes   = null;
        string? usedBranch = null;

        var http = _httpFactory.CreateClient("github");

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = useApi
                ? $"https://api.github.com/repos/{owner}/{repo}/zipball/{candidate}"
                : $"https://github.com/{owner}/{repo}/archive/refs/heads/{candidate}.zip";

            progress?.Report($"Provar gren '{candidate}'…");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (useApi)
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("token", accessToken);
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                }

                var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    continue;

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"GitHub svarade {(int)response.StatusCode} för '{owner}/{repo}'. " +
                        (useApi
                            ? "Kontrollera att token är giltig och har rätt behörighet."
                            : "Kontrollera att repot existerar och är publikt."));

                progress?.Report($"Laddar ned arkiv (gren: '{candidate}')…");
                zipBytes   = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                usedBranch = candidate;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException)  { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Nätverksfel: {ex.Message}", ex);
            }
        }

        if (zipBytes is null)
            throw new InvalidOperationException(
                $"Repot '{owner}/{repo}' hittades inte på någon gren " +
                $"(provade: {string.Join(", ", candidates)}). " +
                "Kontrollera URL och grennamn.");

        if (!string.IsNullOrWhiteSpace(trimmedBranch) &&
            !usedBranch!.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(
                $"⚠️ Gren '{trimmedBranch}' hittades inte — använde '{usedBranch}' istället.");
            await Task.Delay(1500, cancellationToken);
        }

        progress?.Report("Packar upp och filtrerar…");

        var results = new List<(string Path, string Content)>();

        using var memStream = new MemoryStream(zipBytes);
        using var archive   = new ZipArchive(memStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.FullName.EndsWith('/')) continue;

            var slash        = entry.FullName.IndexOf('/');
            var relativePath = slash >= 0 ? entry.FullName[(slash + 1)..] : entry.FullName;

            if (string.IsNullOrEmpty(relativePath))                       continue;
            if (IsInExcludedFolder(relativePath, excludedSet))            continue;

            var ext = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
            if (!extSet.Contains(ext))                                    continue;

            // Skip files whose name matches any enabled glob pattern.
            if (patternList.Count > 0 &&
                patternList.Any(p => MatchesGlob(entry.Name, p)))         continue;

            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                results.Add((relativePath, await reader.ReadToEndAsync(cancellationToken)));
            }
            catch
            {
                results.Add((relativePath, $"// [Kunde inte läsa: {relativePath}]"));
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "Inga filer hittades med de valda filtyperna. " +
                "Kontrollera att du valt rätt filtyper och att filtren inte blockerar allt.");

        progress?.Report($"Klart! {results.Count} filer från '{usedBranch}'.");
        return results.OrderBy(f => f.Path).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static bool IsInExcludedFolder(string relativePath, HashSet<string> excluded)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (excluded.Contains(parts[i].ToLowerInvariant()))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Matches a file name against a glob pattern supporting * (any chars) and
    /// ? (single char), case-insensitive.
    ///
    /// Compiled Regex instances are cached in GlobCache so each unique pattern
    /// is only compiled once across the entire fetch operation.
    /// </summary>
    private static bool MatchesGlob(string fileName, string pattern)
    {
        var regex = GlobCache.GetOrAdd(pattern, p =>
        {
            var regexStr = "^" +
                Regex.Escape(p)
                     .Replace("\\*", ".*")
                     .Replace("\\?", ".") +
                "$";
            return new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });

        return regex.IsMatch(fileName);
    }
}
