using System.IO.Compression;
using System.Net.Http.Headers;

namespace CopyCat.Services;

public class GitHubService : IGitHubService
{
    private readonly IHttpClientFactory _httpFactory;

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

        var trimmedBranch = branch.Trim();
        var candidates = string.IsNullOrWhiteSpace(trimmedBranch)
            ? new[] { "main", "master", "develop" }
            : new[] { trimmedBranch }
              .Concat(new[] { "main", "master", "develop" }
                  .Where(b => !b.Equals(trimmedBranch, StringComparison.OrdinalIgnoreCase)))
              .ToArray();

        // FIX 1: "using var" säkerställer att HttpClient-objektet disposed efter varje anrop.
        // IHttpClientFactory pooler den underliggande HttpClientHandler, men
        // HttpClient-omslaget ska ändå kasseras för att undvika läckor vid upprepade hämtningar.
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

                // FIX: Specifik hantering av 403 för att tydliggöra rate-limit-fel.
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

                // Håll response vid liv — strömmen läses efter loopen.
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

        // FIX 2: Ström-baserad nedladdning eliminerar en hel kopia av ZIP-filen i minnet.
        //
        // TIDIGARE (2× minnesåtgång):
        //   zipBytes  = ReadAsByteArrayAsync()   → allokerar byte[] med hela ZIPen
        //   memStream = new MemoryStream(zipBytes) → kopierar byte[] till en ny buffer
        //
        // NU (1× minnesåtgång):
        //   ReadAsStreamAsync() → nätverksström, ingen heap-allokering
        //   CopyToAsync(memStream) → en enda MemoryStream-buffer
        //
        // ZipArchive kräver en sökbar ström (seek), så vi måste buffra till MemoryStream,
        // men vi undviker den extra byte[]-kopian som ReadAsByteArrayAsync skapade.
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

                if (string.IsNullOrEmpty(relativePath))            continue;
                if (IsInExcludedFolder(relativePath, excludedSet)) continue;

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
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                "Inga filer hittades med de valda filtyperna. " +
                "Kontrollera att du valt rätt filtyper och att mappfiltren inte blockerar allt.");

        progress?.Report($"Klart! {results.Count} filer från '{usedBranch}'.");
        return results.OrderBy(f => f.Path).ToList();
    }

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
