using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyCat.Models.Catalog;
using CopyCat.Services;
using System.Text.Json;

namespace CopyCat.ViewModels;

/// <summary>
/// Thin orchestrator ViewModel.
///
/// Responsibilities (only):
///   1. Hold and expose the five child ViewModels as named properties so
///      XAML can bind to them via path notation (e.g. {Binding Repo.RepoUrl}).
///   2. Wire SaveWorkspaceAsync, which reads from multiple children and
///      cannot live in any single child VM without breaking ISP.
///   3. Provide InitializeAsync, called once by MainPage.OnAppearing.
///
/// All domain logic lives in the child ViewModels.
/// Cross-VM communication uses WeakReferenceMessenger.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // ── Child ViewModels (public so XAML can bind through them) ──────────────

    public RepositoryViewModel Repo     { get; }
    public FilterViewModel     Filter   { get; }
    public ChunkingViewModel   Chunking { get; }
    public ChunkListViewModel  Chunks   { get; }
    public PromptsViewModel    Prompts  { get; }

    private bool _initialized;
    private bool _disposed;

    public MainViewModel(
        RepositoryViewModel repo,
        FilterViewModel     filter,
        ChunkingViewModel   chunking,
        ChunkListViewModel  chunks,
        PromptsViewModel    prompts)
    {
        Repo     = repo;
        Filter   = filter;
        Chunking = chunking;
        Chunks   = chunks;
        Prompts  = prompts;

        // SaveWorkspace needs data from Filter, Chunking, and Prompts.
        // Wire it here so RepositoryViewModel stays decoupled from siblings.
        // Keep FilterViewModel.AutoDetectCommand up-to-date with the current repo URL.
        // PropertyChanged fires on the main thread so no dispatcher needed.
        Repo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RepositoryViewModel.RepoUrl))
                Filter.UpdateRepoUrl(Repo.RepoUrl);
        };

        // SaveWorkspace command wired here because it spans Filter, Chunking, and Prompts.
        // RepositoryViewModel exposes SaveWorkspaceCommand as a settable IAsyncRelayCommand
        // so the XAML {Binding Repo.SaveWorkspaceCommand} still binds cleanly.
        Repo.SaveWorkspaceCommand = new AsyncRelayCommand(
            SaveWorkspaceAsync,
            () => Repo.HasSelectedRepo);
    }

    // ── Initialisation ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        await Repo.InitializeAsync();
        await Prompts.InitializeAsync();
    }

    // ── SaveWorkspace (cross-VM orchestration) ───────────────────────────────

    private async Task SaveWorkspaceAsync()
    {
        if (Repo.SelectedSavedRepo is null) return;
        try
        {
            var enabledExts = JsonSerializer.Serialize(
                Filter.FileTypeFilters.Where(f => f.IsEnabled).Select(f => f.Label).ToList(),
                CopyCatJsonContext.Default.ListString);

            var excludedFolders = JsonSerializer.Serialize(
                Filter.FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name).ToList(),
                CopyCatJsonContext.Default.ListString);

            var savedPatterns = JsonSerializer.Serialize(
                Filter.FilePatternFilters.Where(f => f.IsEnabled && !f.IsAutoAdded).Select(f => f.Pattern).ToList(),
                CopyCatJsonContext.Default.ListString);

            await Repo.PersistWorkspaceAsync(
                (int)Chunking.MaxTokensPerChunk,
                enabledExts,
                excludedFolders,
                savedPatterns,
                Prompts.SelectedPrompt?.OriginalSortOrder ?? -1);

            // Flash confirmation in the filter status label (visible in config panel)
            Filter.AutoDetectStatusText = "✓ Workspace saved";
            await Task.Delay(2000);
            if (Filter.AutoDetectStatusText == "✓ Workspace saved")
                Filter.AutoDetectStatusText = string.Empty;
        }
        catch (Exception) { /* errors surfaced by Repo.SaveWorkspaceAsync */ }
    }

    // ── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Chunking.Dispose();
        Filter.Dispose();
        Chunks.Dispose();
        Prompts.Dispose();
    }
}
