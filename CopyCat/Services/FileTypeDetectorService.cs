using System.Runtime.InteropServices;
using System.Text.Json;

// LibGit2Sharp is only available on desktop targets.
// The native libgit2 binary is NOT available for iOS or Android.
#if !ANDROID && !IOS
using LibGit2Sharp;
#endif

namespace CopyCat.Services;

/// <summary>
/// Detects which file extensions are present in a repository without
/// downloading file content.
///
/// FIX C-1: This service was implemented but never registered in DI.
/// It is now registered in MauiProgram and injected into MainViewModel,
/// replacing the duplicated directory-walk code that was in AutoDetectFileTypesAsync.
///
/// Strategy by source:
///   Local git repo    → libgit2sharp index scan (Windows/Mac only, 0 API calls)
///   Local directory   → Directory.EnumerateFiles safe walk (all platforms)
///   GitHub URL        → git/trees API — one lightweight JSON request, no content
/// </summary>
public class FileTypeDetectorService : IFileTypeDetectorService
{
    private readonly IHttpClientFactory _httpFactory;

    private static bool SupportsLibGit2 =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public FileTypeDetectorService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public async Task<List<string>> DetectExtensionsAsync(
        string            repoUrlOrPath,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return IsLocalPath(repoUrlOrPath)
                ? DetectFromLocal(repoUrlOrPath)
                : await DetectFromGitHubAsync(repoUrlOrPath, accessToken, branch, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }

    private static List<string> DetectFromLocal(string inputPath)
    {
        var dir = ResolveLocalDir(inputPath);
        if (!Directory.Exists(dir)) return [];

#if !ANDROID && !IOS
        if (SupportsLibGit2)
        {
            var gitResult = TryDetectFromGitIndex(dir);
            if (gitResult is not null) return gitResult;
        }
#endif
        return DetectFromDirectory(dir);
    }

#if !ANDROID && !IOS
    private static List<string>? TryDetectFromGitIndex(string dir)
    {
        try
        {
            var repoRoot = Repository.Discover(dir);
            if (repoRoot is null) return null;

            using var repo   = new Repository(repoRoot);
            var       counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in repo.Index)
            {
                var ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                counts[ext] = counts.TryGetValue(ext, out var n) ? n + 1 : 1;
            }

            return counts.Count == 0
                ? null
                : counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        }
        catch { return null; }
    }
#endif

    /// <summary>
    /// Safe manual directory walk — does not throw on inaccessible subdirectories.
    /// FIX: Directory.EnumerateFiles with AllDirectories throws UnauthorizedAccessException
    /// on Android sandboxed directories. This manual queue approach skips locked dirs.
    /// </summary>
    private static List<string> DetectFromDirectory(string baseDir)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue  = new Queue<string>(new[] { baseDir });

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            IEnumerable<string> files;
            try   { files = Directory.EnumerateFiles(current); }
            catch { continue; }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                counts[ext] = counts.TryGetValue(ext, out var n) ? n + 1 : 1;
            }

            IEnumerable<string> subdirs;
            try   { subdirs = Directory.EnumerateDirectories(current); }
            catch { continue; }

            foreach (var sub in subdirs) queue.Enqueue(sub);
        }

        return counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
    }

    private async Task<List<string>> DetectFromGitHubAsync(
        string            repoUrl,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken)
    {
        var (owner, repo) = GitHubUrlParser.Parse(repoUrl);
        var trimmedBranch = (branch ?? string.Empty).Trim();

        var defaultBranches = new[] { "HEAD", "main", "master", "develop" };
        var candidates = string.IsNullOrWhiteSpace(trimmedBranch)
            ? defaultBranches
            : new[] { trimmedBranch }
                .Concat(defaultBranches.Where(b => !b.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        using var http = _httpFactory.CreateClient("github");

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url     = $"https://api.github.com/repos/{owner}/{repo}/git/trees/{candidate}?recursive=1";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("token", accessToken);

            HttpResponseMessage response;
            try { response = await http.SendAsync(request, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { continue; }

            if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                                    or System.Net.HttpStatusCode.UnprocessableEntity)
            { response.Dispose(); continue; }

            if (!response.IsSuccessStatusCode) { response.Dispose(); continue; }

            using (response)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseExtensionsFromTree(json);
            }
        }

        return [];
    }

    private static List<string> ParseExtensionsFromTree(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tree", out var tree)) return [];

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in tree.EnumerateArray())
        {
            if (entry.TryGetProperty("type", out var type) &&
                type.GetString() != "blob") continue;

            if (!entry.TryGetProperty("path", out var pathProp)) continue;
            var path = pathProp.GetString();
            if (string.IsNullOrEmpty(path)) continue;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) continue;
            counts[ext] = counts.TryGetValue(ext, out var n) ? n + 1 : 1;
        }

        return counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
    }

    private static string ResolveLocalDir(string path)
    {
        var t = path.Trim().Trim('"');
        return (t.EndsWith(".sln",    StringComparison.OrdinalIgnoreCase) ||
                t.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            ? (Path.GetDirectoryName(t) ?? t)
            : t;
    }

    private static bool IsLocalPath(string p) =>
        (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':')
        || p.StartsWith('/')
        || p.StartsWith('~')
        || p.StartsWith("\\\\");
}
