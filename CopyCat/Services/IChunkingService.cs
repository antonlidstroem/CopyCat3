using CopyCat.Models;

namespace CopyCat.Services;

public interface IChunkingService
{
    int EstimateTokens(string text);

    // FIX: CancellationToken tillagd så att "Abort" verkligen avbryter
    // en pågående chunking-körning, inte bara väntar tills den är klar.
    List<CodeChunk> CreateChunks(
        List<(string Path, string Content)> files,
        int maxTokensPerChunk,
        CancellationToken cancellationToken = default);
}
