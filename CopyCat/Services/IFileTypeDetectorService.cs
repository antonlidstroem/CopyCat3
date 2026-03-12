namespace CopyCat.Services;

public interface IFileTypeDetectorService
{
    /// <summary>
    /// Scans a repository or local path and returns the distinct file
    /// extensions it contains, ordered by occurrence count descending.
    ///
    /// For GitHub URLs this calls the lightweight Trees API
    /// (one JSON request — no ZIP download, no Languages API).
    /// For local paths this enumerates files without reading content.
    ///
    /// Returns an empty list on any failure so callers degrade gracefully.
    /// </summary>
    Task<List<string>> DetectExtensionsAsync(
        string            repoUrlOrPath,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken = default);
}
