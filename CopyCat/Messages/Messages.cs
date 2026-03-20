using CopyCat.Models;

namespace CopyCat.Messages;

/// <summary>
/// Published by <c>RepositoryViewModel</c> when the user selects a saved repo.
/// Subscribers: FilterViewModel, ChunkingViewModel, PromptsViewModel.
/// </summary>
public sealed record RepoSelectedMessage(SavedRepo Repo);

/// <summary>
/// Published by <c>ChunkingViewModel</c> after a successful fetch + chunk.
/// Subscriber: ChunkListViewModel.
/// </summary>
public sealed record FetchCompletedMessage(
    List<CodeChunk> Chunks,
    int             TotalFiles,
    int             TotalTokens,
    int             TotalProjects);

/// <summary>
/// Published by <c>ChunkingViewModel</c> on Reset.
/// Subscriber: ChunkListViewModel.
/// </summary>
public sealed record FetchResetMessage;

/// <summary>
/// Published by <c>PromptsViewModel</c> when the selected prompt changes.
/// Subscriber: ChunkListViewModel.
/// </summary>
public sealed record PromptSelectionChangedMessage(PromptItem? SelectedPrompt);

/// <summary>
/// Published by <c>FilterViewModel</c> after PopulateFetchedFolders completes.
/// Carries the distinct folder list so FoldersCard can show the "From this repo" picker.
/// Subscriber: (future) FoldersCard — currently consumed directly via FilterViewModel.FetchedFolders.
/// </summary>
public sealed record FetchedFoldersAvailableMessage(int FolderCount);
