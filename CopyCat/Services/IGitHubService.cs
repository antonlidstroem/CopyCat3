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

    Task<List<string>> FetchBranchesAsync(
        string            repoUrl,
        string?           accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the repository archive and counts file extensions from
    /// the ZIP central directory — NO GitHub API call, does not count
    /// against rate limits.
    ///
    /// Returns a dictionary of lowercase extension (e.g. ".cs") -> file count,
    /// sorted descending by count.  Returns an empty dictionary on any failure
    /// so callers can degrade gracefully.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> DetectFileTypesInRepoAsync(
        string            repoUrl,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken = default);
}
