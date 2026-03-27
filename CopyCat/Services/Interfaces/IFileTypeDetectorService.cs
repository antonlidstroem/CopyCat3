namespace CopyCat.Services;



public interface IFileTypeDetectorService
{
    Task<List<string>> DetectExtensionsAsync(
        string repoUrlOrPath,
        string? accessToken,
        string branch,
        CancellationToken cancellationToken = default);
}
