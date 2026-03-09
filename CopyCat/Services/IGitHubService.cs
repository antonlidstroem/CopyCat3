namespace CopyCat.Services;

public interface IGitHubService
{
    Task<List<(string Path, string Content)>> FetchFilesAsync(
        string              repoUrl,
        IEnumerable<string> extensions,
        string?             accessToken,
        string              branch,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns,
        IProgress<string>?  progress          = null,
        CancellationToken   cancellationToken = default);

    /// <summary>
    /// Hämtar grenar för ett GitHub-repo.
    /// Returnerar en tom lista vid fel (dålig URL, rate-limit, etc.).
    /// </summary>
    Task<List<string>> FetchBranchesAsync(
        string            repoUrl,
        string?           accessToken,
        CancellationToken cancellationToken = default);
}
