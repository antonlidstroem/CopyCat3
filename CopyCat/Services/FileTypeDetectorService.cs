using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net.Http;


// LibGit2Sharp NuGet package is required on desktop targets.
// Add to your .csproj inside a platform condition:
//
//   <ItemGroup Condition="'$(TargetFramework)'=='net9.0-windows10.0.19041.0'
//                       or '$(TargetFramework)'=='net9.0-maccatalyst'">
//     <PackageReference Include="LibGit2Sharp" Version="0.30.*" />
//   </ItemGroup>
//
// The native libgit2 binary is NOT available for iOS or Android,
// so any LibGit2Sharp usage is guarded by SupportsLibGit2 below.
#if !ANDROID && !IOS
using LibGit2Sharp;
#endif

namespace CopyCat.Services;

/// <summary>
/// Detects which file extensions are present in a repository without
/// downloading file content.
///
/// Detection strategy by source type:
///
/// ┌─────────────────────────────────────────────────────────────────────┐
/// │ Source            │ Method                    │ API calls │ Content │
/// ├───────────────────┼───────────────────────────┼───────────┼─────────┤
/// │ Local git repo    │ libgit2sharp index scan   │ 0         │ none    │
/// │ Local directory   │ Directory.EnumerateFiles  │ 0         │ none    │
/// │ Remote GitHub URL │ git/trees API (paths only)│ 1 (cheap) │ none    │
/// └─────────────────────────────────────────────────────────────────────┘
///
/// The GitHub Trees API is one lightweight JSON request returning file
/// paths only (~50–200 KB for most repos). It is NOT the Languages API
/// and does NOT consume that separate quota.
/// Anonymous limit: 60 req/hr.  With token: 5 000 req/hr.
/// </summary>
public class FileTypeDetectorService : IFileTypeDetectorService
{
    private readonly IHttpClientFactory _httpFactory;

    /// <summary>
    /// True when the current platform has a libgit2 native binary available.
    /// False on iOS and Android — those targets fall back to directory scan.
    /// </summary>
    private static bool SupportsLibGit2 =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public FileTypeDetectorService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    // ── Public entry point ─────────────────────────────────────────────────

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
                : await DetectFromGitHubAsync(repoUrlOrPath, accessToken,
                                              branch, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }

    // ── Local detection ────────────────────────────────────────────────────

    /// <summary>
    /// Detects extensions from a local path.
    ///
    /// Prefers libgit2sharp on desktop (Windows/macOS) to enumerate only
    /// git-tracked files, which respects .gitignore and avoids false hits
    /// in node_modules, bin/obj, etc.
    ///
    /// Falls back to Directory.EnumerateFiles on mobile or when the path
    /// is not a git repository (e.g. a plain directory).
    /// </summary>
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
    /// <summary>
    /// Uses libgit2sharp to walk the repository index (staged + working tree
    /// tracked files) and collect file extensions.
    ///
    /// Returns null if the directory is not a valid git repository or if
    /// libgit2 fails for any reason — callers fall back to directory scan.
    /// </summary>
    private static List<string>? TryDetectFromGitIndex(string dir)
    {
        try
        {
            // Discover the root of the git repository (handles sub-directories)
            var repoRoot = Repository.Discover(dir);
            if (repoRoot is null) return null;

            using var repo   = new Repository(repoRoot);
            var       counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Enumerate all entries in the git index (tracked files only).
            // This automatically respects .gitignore because untracked files
            // never appear in the index.
            foreach (var entry in repo.Index)
            {
                var ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                counts[ext] = counts.TryGetValue(ext, out var n) ? n + 1 : 1;
            }

            return counts.Count == 0
                ? null
                : counts.OrderByDescending(kv => kv.Value)
                         .Select(kv => kv.Key)
                         .ToList();
        }
        catch
        {
            // Swallow all libgit2 errors — fall back to directory scan
            return null;
        }
    }
#endif

    /// <summary>
    /// Fallback: walk the directory tree and collect extensions from all
    /// accessible files. Inaccessible subdirectories are silently skipped.
    /// </summary>
    private static List<string> DetectFromDirectory(string baseDir)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        //var queue  = new Queue<string>([baseDir]);
        var queue = new Queue<string>(new[] { baseDir });

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

        return counts.OrderByDescending(kv => kv.Value)
                     .Select(kv => kv.Key)
                     .ToList();
    }

    // ── GitHub Trees API ───────────────────────────────────────────────────

    /// <summary>
    /// Calls the GitHub git/trees API with recursive=1 to retrieve all
    /// file paths in the repository as a single JSON document.
    ///
    /// This is one HTTP request that returns path strings only — no file
    /// content is downloaded. Even for repos with 100 k+ files the typical
    /// response is under 5 MB (paths are short strings).
    ///
    /// When the response is "truncated" (GitHub's limit for very large repos)
    /// the paths still present are sufficient for reliable extension detection.
    ///
    /// This call uses the /git/trees endpoint, NOT /languages, so it does
    /// not consume the separate language-statistics API quota.
    /// </summary>
    private async Task<List<string>> DetectFromGitHubAsync(
        string            repoUrl,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken)
    {
        var (owner, repo) = GitHubUrlParser.Parse(repoUrl);

        var trimmedBranch = (branch ?? string.Empty).Trim();

        // Vi använder 'new[]' istället för '[]' för att hjälpa kompilatorn med typen
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
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
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

    /// <summary>
    /// Parses the "tree" array returned by the git/trees API.
    /// Only entries of type "blob" (files) are counted; "tree" entries
    /// (directories) are skipped.
    /// Returns extensions ordered by occurrence count descending.
    /// </summary>
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

        return counts.OrderByDescending(kv => kv.Value)
                     .Select(kv => kv.Key)
                     .ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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
