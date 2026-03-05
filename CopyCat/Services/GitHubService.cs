using System.IO.Compression;
using System.Net.Http.Headers;

namespace CopyCat.Services;

public class GitHubService : IGitHubService
{
    // Single static HttpClient — handles redirects (needed for private-repo API calls)
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect    = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public async Task<List<(string Path, string Content)>> FetchFilesAsync(
        string              repoUrl,
        IEnumerable<string> extensions,
        string?             accessToken,
        string              branch,
        IEnumerable<string> excludedFolders,
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

        bool useApi = !string.IsNullOrWhiteSpace(accessToken);

        progress?.Report($"Ansluter till {owner}/{repo}…");

        // Build branch candidates — try the specified branch first, then fallbacks
        var candidates = string.IsNullOrWhiteSpace(branch)
            ? new[] { "main", "master", "develop" }
            : (new[] { branch.Trim() })
              .Concat(new[] { "main", "master", "develop" }
                  .Where(b => !b.Equals(branch.Trim(), StringComparison.OrdinalIgnoreCase)))
              .ToArray();

        byte[]? zipBytes   = null;
        string? usedBranch = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ── Public repos: direct zip download (no rate limit) ──────────
            // ── Private repos: GitHub API zipball (auth required)    ──────────
            var url = useApi
                ? $"https://api.github.com/repos/{owner}/{repo}/zipball/{candidate}"
                : $"https://github.com/{owner}/{repo}/archive/refs/heads/{candidate}.zip";

            progress?.Report($"Provar gren '{candidate}'…");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (useApi)
                {
                    // Per-request auth so the static HttpClient doesn't leak credentials
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("token", accessToken);
                    request.Headers.UserAgent.ParseAdd("CopyCat/1.0");
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                }

                var response = await _http.SendAsync(
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
                throw new InvalidOperationException($"Nätverksfel: {ex.Message}");
            }
        }

        if (zipBytes is null)
            throw new InvalidOperationException(
                $"Repot '{owner}/{repo}' hittades inte på gren '{branch}'. " +
                "Kontrollera URL och grennamn.");

        progress?.Report("Packar upp och filtrerar…");

        var results = new List<(string Path, string Content)>();

        using var memStream = new MemoryStream(zipBytes);
        using var archive   = new ZipArchive(memStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.FullName.EndsWith('/')) continue;

            // Strip the top-level archive folder (e.g. "repo-main/")
            var slash        = entry.FullName.IndexOf('/');
            var relativePath = slash >= 0 ? entry.FullName[(slash + 1)..] : entry.FullName;
            if (string.IsNullOrEmpty(relativePath)) continue;

            // Skip excluded folders
            if (IsInExcludedFolder(relativePath, excludedSet)) continue;

            // Skip unwanted extensions
            var ext = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
            if (!extSet.Contains(ext)) continue;

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
                "Kontrollera att du valt rätt filtyper och att mappfiltren inte blockerar allt.");

        progress?.Report($"Klart! {results.Count} filer från '{usedBranch}'.");
        return results.OrderBy(f => f.Path).ToList();
    }

    /// <summary>
    /// Returns true if any path segment (excluding the filename) matches an excluded folder name.
    /// </summary>
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
}
