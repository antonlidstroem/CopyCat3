namespace CopyCat.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task ShareAsync(string text, string title);
}

public interface IShareService
{
    Task ShareTextAsync(string text, string title);
}

public interface ILocalFileService
{
    Task<List<(string Path, string Content)>> ReadFilesAsync(
        string              localPath,
        IEnumerable<string> extensions,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns,
        IProgress<string>?  progress          = null,
        CancellationToken   cancellationToken = default);
}

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

    Task<IReadOnlyDictionary<string, int>> DetectFileTypesInRepoAsync(
        string            repoUrl,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken = default);
}

public interface IFileTypeDetectorService
{
    Task<List<string>> DetectExtensionsAsync(
        string            repoUrlOrPath,
        string?           accessToken,
        string            branch,
        CancellationToken cancellationToken = default);
}
