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

    public async Task<List<string>> FetchBranchesAsync(
        string            repoUrl,
        string?           accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (owner, repo) = GitHubUrlParser.Parse(repoUrl);
            using var http    = _httpFactory.CreateClient("github");

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

            return doc.RootElement
                .EnumerateArray()
                .Select(e => e.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return []; }
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

        var extSet            = extensions.Select(e => e.ToLowerInvariant()).ToHashSet();
        var excludedFolderSet = excludedFolders.Select(f => f.ToLowerInvariant().Trim('/')).ToHashSet();
        var patternList       = excludedFilePatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        bool useApi           = !string.IsNullOrWhiteSpace(accessToken);

        progress?.Report($"Connecting to {owner}/{repo}…");

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

            progress?.Report($"Trying branch '{candidate}'…");

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
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    response.Dispose(); continue;
                }
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    response.Dispose();
                    throw new InvalidOperationException(
                        "GitHub denied access (403 Forbidden). You may have hit the anonymous rate limit. " +
                        "Enter a GitHub token to increase the limit.");
                }
                if (!response.IsSuccessStatusCode)
                {
                    response.Dispose();
                    throw new InvalidOperationException(
                        $"GitHub responded {(int)response.StatusCode} for '{owner}/{repo}'. " +
                        (useApi ? "Check that the token is valid and has the correct permissions."
                                : "Check that the repository exists and is public."));
                }

                successResponse = response;
                usedBranch      = candidate;
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException)  { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Network error: {ex.Message}", ex);
            }
        }

        if (successResponse is null)
            throw new InvalidOperationException(
                $"Repository '{owner}/{repo}' was not found on any branch " +
                $"(tried: {string.Join(", ", candidates)}). Check the URL and branch name.");

        if (!string.IsNullOrWhiteSpace(trimmedBranch) &&
            !usedBranch!.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report($"⚠️ Branch '{trimmedBranch}' not found — using '{usedBranch}' instead.");
            await Task.Delay(1500, cancellationToken);
        }

        progress?.Report("Downloading and extracting…");

        var results = new List<(string Path, string Content)>();

        using (successResponse)
        {
            using var responseStream = await successResponse.Content.ReadAsStreamAsync(cancellationToken);
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

                if (string.IsNullOrEmpty(relativePath))                  continue;
                if (IsInExcludedFolder(relativePath, excludedFolderSet)) continue;

                var ext = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!extSet.Contains(ext))                               continue;

                if (patternList.Count > 0 && MatchesAnyPattern(entry.Name, patternList)) continue;

                try
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                    results.Add((relativePath, await reader.ReadToEndAsync(cancellationToken)));
                }
                catch
                {
                    results.Add((relativePath, $"// [Could not read: {relativePath}]"));
                }
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "No files found with the selected file types. " +
                "Check that the correct file types are enabled and that folder filters are not blocking everything.");

        progress?.Report($"Done! {results.Count} files from '{usedBranch}'.");
        return results.OrderBy(f => f.Path).ToList();
    }

    private static bool IsInExcludedFolder(string relativePath, HashSet<string> excluded)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
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
        if (!p.EndsWith('*') && parts[^1].Length > 0 && !t.EndsWith(parts[^1])) return false;
        return true;
    }
}
