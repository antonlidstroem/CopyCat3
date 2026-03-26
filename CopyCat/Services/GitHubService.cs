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

    public async Task<IReadOnlyDictionary<string, int>> DetectFileTypesInRepoAsync(
        string            repoUrl,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var zipBytes = await DownloadZipAsync(repoUrl, accessToken, branch, cancellationToken);
            if (zipBytes is null) return new Dictionary<string, int>();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var ms      = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))     continue;
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                if (_binaryExtensions.Contains(ext)) continue;
                counts[ext] = counts.GetValueOrDefault(ext, 0) + 1;
            }

            return counts
                .Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new Dictionary<string, int>(); }
    }

    private static readonly HashSet<string> _binaryExtensions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",".jpg",".jpeg",".gif",".ico",".svg",".webp",".bmp",".tiff",
        ".mp3",".mp4",".wav",".ogg",".mov",".avi",
        ".zip",".gz",".tar",".rar",".7z",
        ".exe",".dll",".so",".dylib",".a",".lib",
        ".pdf",".doc",".docx",".xls",".xlsx",".pptx",
        ".ttf",".otf",".woff",".woff2",".eot",
        ".db",".sqlite",".lock",".bin",".dat",
    };

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
            request.Headers.UserAgent.ParseAdd("CopyCat-MAUI");

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("token", accessToken);

            var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new InvalidOperationException(
                    "Token invalid or expired (401). Clear the token field and generate a new one from " +
                    "GitHub → Settings → Developer settings → Fine-grained tokens.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw new InvalidOperationException(
                    "GitHub denied access (403). Check that the token has Contents: Read-only permission " +
                    "for this repository.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var hint = string.IsNullOrWhiteSpace(accessToken)
                    ? " If this is a private repo, add a GitHub token."
                    : " Check that the token grants access to this repo.";
                response.Dispose();
                throw new InvalidOperationException(
                    $"GitHub responded {(int)response.StatusCode} for '{owner}/{repo}'." + hint);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .EnumerateArray()
                .Select(e => e.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException)  { throw; }
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
        var extSet            = extensions.Select(e => e.ToLowerInvariant()).ToHashSet();
        var excludedFolderSet = excludedFolders.Select(f => f.ToLowerInvariant().Trim('/')).ToHashSet();
        var patternList       = excludedFilePatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        bool useApi           = !string.IsNullOrWhiteSpace(accessToken);

        progress?.Report("Connecting to repository…");

        var trimmedBranch = branch.Trim();
        var candidates = string.IsNullOrWhiteSpace(trimmedBranch)
            ? new[] { "main", "master", "develop" }
            : new[] { trimmedBranch }
              .Concat(new[] { "main", "master", "develop" }
                  .Where(b => !b.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase)))
              .ToArray();

        HttpResponseMessage? successResponse = null;
        string?              usedBranch      = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Trying branch '{candidate}'…");

            try
            {
                var response = await TryDownloadBranchAsync(
                    repoUrl, accessToken, candidate, useApi, cancellationToken);

                if (response?.IsSuccessStatusCode == true)
                {
                    successResponse = response;
                    usedBranch      = candidate;
                    break;
                }
                response?.Dispose();
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException)  { throw; }
        }

        if (successResponse is null)
        {
            var (owner, repo) = GitHubUrlParser.Parse(repoUrl);
            var tokenHint = string.IsNullOrWhiteSpace(accessToken)
                ? " If this is a private repo, add a GitHub token."
                : " Check that the token has Contents: Read-only permission for this repo.";
            throw new InvalidOperationException(
                $"Repository '{owner}/{repo}' not found on any branch " +
                $"(tried: {string.Join(", ", candidates)}). " +
                $"Check the URL and branch.{tokenHint}");
        }

        if (!string.IsNullOrWhiteSpace(trimmedBranch) &&
            !usedBranch!.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report($"⚠️ Branch '{trimmedBranch}' not found — using '{usedBranch}'.");
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

                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
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

    private async Task<byte[]?> DownloadZipAsync(
        string            repoUrl,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken)
    {
        bool useApi  = !string.IsNullOrWhiteSpace(accessToken);
        var  trimmed = branch.Trim();

        var candidates = string.IsNullOrWhiteSpace(trimmed)
            ? new[] { "main", "master", "develop" }
            : new[] { trimmed }
              .Concat(new[] { "main", "master", "develop" }
                  .Where(b => !b.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
              .ToArray();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await TryDownloadBranchAsync(
                repoUrl, accessToken, candidate, useApi, cancellationToken);

            if (response?.IsSuccessStatusCode == true)
            {
                using (response)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var ms     = new MemoryStream();
                    await stream.CopyToAsync(ms, cancellationToken);
                    return ms.ToArray();
                }
            }
            response?.Dispose();
        }
        return null;
    }

    private async Task<HttpResponseMessage?> TryDownloadBranchAsync(
        string            repoUrl,
        string?           accessToken,
        string            candidate,
        bool              useApi,
        CancellationToken cancellationToken)
    {
        var (owner, repo) = GitHubUrlParser.Parse(repoUrl);
        using var http    = _httpFactory.CreateClient("github");

        var url = useApi
            ? $"https://api.github.com/repos/{owner}/{repo}/zipball/{candidate}"
            : $"https://github.com/{owner}/{repo}/archive/refs/heads/{candidate}.zip";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CopyCat-MAUI");

        if (useApi)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("token", accessToken);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
        }

        var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        { response.Dispose(); return null; }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new InvalidOperationException(
                "GitHub denied access (401 Unauthorized). Token is invalid or expired. " +
                "Go to GitHub → Settings → Developer settings → Fine-grained tokens and generate a new one.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new InvalidOperationException(
                "GitHub denied access (403 Forbidden). " +
                "You may have hit the anonymous request limit, or the token lacks Contents: Read-only permission.");
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvalidOperationException(
                $"GitHub responded {(int)response.StatusCode} for '{owner}/{repo}'. " +
                (useApi
                    ? "Check that the token is valid and has the correct permissions."
                    : "Check that the repository exists and is public."));
        }

        return response;
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
