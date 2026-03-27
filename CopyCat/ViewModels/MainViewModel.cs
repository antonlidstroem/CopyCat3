using CommunityToolkit.Mvvm.ComponentModel;
using CopyCat.Services;
using Microsoft.Extensions.Logging;

namespace CopyCat.ViewModels;

/// <summary>
/// Facade / root ViewModel.
/// MainPage binds to this; all XAML paths go through the child VM properties:
///   Repo.Xxx, Filter.Xxx, Chunking.Xxx, Chunks.Xxx, Prompts.Xxx
///
/// This class owns no logic of its own — it just wires the children together
/// and exposes them so XAML and code-behind can reach them through one object.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    // ── Child ViewModels ───────────────────────────────────────────────────

    public RepositoryViewModel Repo { get; }
    public FilterViewModel Filter { get; }
    public ChunkingViewModel Chunking { get; }
    public ChunkListViewModel Chunks { get; }
    public PromptsViewModel Prompts { get; }

    // ── Constructor ────────────────────────────────────────────────────────

    public MainViewModel(
        RepositoryViewModel repo,
        FilterViewModel filter,
        ChunkingViewModel chunking,
        ChunkListViewModel chunks,
        PromptsViewModel prompts)
    {
        Repo = repo;
        Filter = filter;
        Chunking = chunking;
        Chunks = chunks;
        Prompts = prompts;
    }

    // ── Initialisation ─────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await Repo.InitializeAsync();
        await Prompts.InitializeAsync();
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        Chunking.Dispose();
        Chunks.Dispose();
        Filter.Dispose();
        Prompts.Dispose();
    }

    // I klassen MainViewModel
    public async Task AddCustomXmlTagAsync(string label, string open, string close, string placeholder)
    {
        // Här lägger du logiken för att spara den nya taggen.
        // Exempelvis genom att anropa en TagService eller spara i PromptsViewModel.

        // Just nu kör vi bara en placeholder för att lösa felet:
        await Task.CompletedTask;
    }
}