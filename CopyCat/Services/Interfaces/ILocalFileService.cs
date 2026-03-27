namespace CopyCat.Services;

public interface ILocalFileService
{
    Task<List<(string Path, string Content)>> ReadFilesAsync(
        string localPath,
        IEnumerable<string> extensions,
        IEnumerable<string> excludedFolders,
        IEnumerable<string> excludedFilePatterns,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
