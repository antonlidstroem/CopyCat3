namespace CopyCat.Services;

public interface ILocalFileService
{
    /// <summary>
    /// Reads source files from a local path.
    /// Accepts a directory path or a .sln / .csproj file (uses its parent directory).
    /// </summary>
    Task<List<(string Path, string Content)>> ReadFilesAsync(
        string              localPath,
        IEnumerable<string> extensions,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns,
        IProgress<string>?  progress          = null,
        CancellationToken   cancellationToken = default);
}
