using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CopyCat.Services;

public class GitHubService : IGitHubService
{
    private readonly IHttpClientFactory _httpFactory;

    public GitHubService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    // ── FetchBranchesAsync ─────────────────────────────────────────────────────

    public async Task<List<string>> FetchBranchesAsync(
        string            repoUrl,
        string?           accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (owner, repo) = GitHubUrlParser.Parse(repoUrl);

            using var http = _httpFactory.CreateClient("github");

            var url     = $"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("token", accessToken);

            var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var branches = doc.RootElement
                .EnumerateArray()
                .Select(e => e.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return branches;
        }
        catch
        {
            return [];
        }
    }

    // ── FetchFilesAsync ────────────────────────────────────────────────────────

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

        var excludedFolderSet = excludedFolders
            .Select(f => f.ToLowerInvariant().Trim('/'))
            .ToHashSet();

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

        using var http = _httpFactory.CreateClient("github");

        HttpResponseMessage? successResponse = null;
        string?              usedBranch      = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = useApi
                ? $"https://api.github.com/repos/{owner}/{repo}/zipball/{candidate}"
                : $"https://github.com/{owner}/{repo}/archive/refs/heads/{candidate}.zip";

            progress?.Report($"Provar gren '{candidate}'…");

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
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
                {
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    response.Dispose();
                    throw new InvalidOperationException(
                        "GitHub nekade åtkomst (403 Forbidden). " +
                        "Du kan ha nått API-gränsen för anonym användning. " +
                        "Ange ett GitHub-token för att höja gränsen.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    response.Dispose();
                    throw new InvalidOperationException(
                        $"GitHub svarade {(int)response.StatusCode} för '{owner}/{repo}'. " +
                        (useApi
                            ? "Kontrollera att token är giltig och har rätt behörighet."
                            : "Kontrollera att repot existerar och är publikt."));
                }

                successResponse = response;
                usedBranch      = candidate;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException)  { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Nätverksfel: {ex.Message}", ex);
            }
        }

        if (successResponse is null)
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

        progress?.Report("Laddar ned och packar upp…");

        var results = new List<(string Path, string Content)>();

        using (successResponse)
        {
            using var responseStream = await successResponse.Content
                .ReadAsStreamAsync(cancellationToken);

            var memStream = new MemoryStream();
            await responseStream.CopyToAsync(memStream, cancellationToken);
            memStream.Position = 0;

            using var archive = new ZipArchive(memStream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.FullName.EndsWith('/')) continue;

                var slash        = entry.FullName.IndexOf('/');
                var relativePath = slash >= 0 ? entry.FullName[(slash + 1)..] : entry.FullName;

                if (string.IsNullOrEmpty(relativePath))                             continue;
                if (IsInExcludedFolder(relativePath, excludedFolderSet))            continue;

                var ext = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!extSet.Contains(ext))                                          continue;

                // Filtrera bort filer som matchar uteslutna mönster
                if (patternList.Count > 0 && MatchesAnyPattern(entry.Name, patternList)) continue;

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
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "Inga filer hittades med de valda filtyperna. " +
                "Kontrollera att du valt rätt filtyper och att mappfiltren inte blockerar allt.");

        progress?.Report($"Klart! {results.Count} filer från '{usedBranch}'.");
        return results.OrderBy(f => f.Path).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

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
    /// Returnerar true om filnamnet matchar något av de angivna glob-mönstren.
    /// Stöder enkelt jokertecken: * matchar noll eller fler tecken (skiftlägesokänsligt).
    /// Exempel: "*Test*", "*.generated.*", "*_test.*"
    /// </summary>
    private static bool MatchesAnyPattern(string fileName, List<string> patterns)
    {
        foreach (var pattern in patterns)
            if (MatchesGlob(fileName, pattern))
                return true;
        return false;
    }

    private static bool MatchesGlob(string text, string pattern)
    {
        var t = text.ToLowerInvariant();
        var p = pattern.ToLowerInvariant();

        // Dela upp på * och matcha del för del
        var parts = p.Split('*');

        if (parts.Length == 1)
            return t.Equals(p, StringComparison.Ordinal);

        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;

            int found = t.IndexOf(parts[i], pos, StringComparison.Ordinal);
            if (found < 0) return false;

            // Första segmentet (utan ledande *) måste börja från position 0
            if (i == 0 && !p.StartsWith('*') && found != 0) return false;

            pos = found + parts[i].Length;
        }

        // Sista segmentet (utan avslutande *) måste vara ett suffix
        if (!p.EndsWith('*') && parts[^1].Length > 0 && !t.EndsWith(parts[^1]))
            return false;

        return true;
    }
}
