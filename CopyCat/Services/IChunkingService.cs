using CopyCat.Models;
namespace CopyCat.Services;
public interface IChunkingService
{
    int EstimateTokens(string text);
    List<CodeChunk> CreateChunks(
        List<(string Path, string Content)> files,
        int maxTokensPerChunk);
}
