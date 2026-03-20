using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CopyCat.Messages;
using CopyCat.Models;
using CopyCat.Services;
using CopyCat.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace CopyCat.ViewModels;

/// <summary>
/// Orchestrates the fetch + chunk pipeline and owns app-mode state.
///
/// On success, publishes <see cref="FetchCompletedMessage"/> so
/// <see cref="ChunkListViewModel"/> can display the results without
/// a direct dependency on this VM.
///
/// On Reset, publishes <see cref="FetchResetMessage"/>.
///
/// Subscribes to <see cref="RepoSelectedMessage"/> to restore
/// the saved MaxTokensPerChunk from the workspace snapshot.
/// </summary>
public partial class ChunkingViewModel : ObservableObject,
    IRecipient<RepoSelectedMessage>
{
    private readonly IGitHubService            _gitHubService;
    private readonly IChunkingService          _chunkingService;
    private readonly ILocalFileService         _localFileService;
    private readonly ILogger<ChunkingViewModel> _logger;

    // Direct references to siblings — needed for synchronous reads during fetch
    private readonly RepositoryViewModel _repo;
    private readonly FilterViewModel     _filter;

    private CancellationTokenSource? _cts;

    public ChunkingViewModel(
        IGitHubService             gitHubService,
        IChunkingService           chunkingService,
        ILocalFileService          localFileService,
        RepositoryViewModel        repo,
        FilterViewModel            filter,
        ILogger<ChunkingViewModel> logger)
    {
        _gitHubService    = gitHubService;
        _chunkingService  = chunkingService;
        _localFileService = localFileService;
        _repo             = repo;
        _filter           = filter;
        _logger           = logger;

        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // ── Observable properties ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationMode))]
    [NotifyPropertyChangedFor(nameof(IsResultsAndNoSelection))]
    private bool _isResultsMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText   = "Enter a GitHub URL or local path, then press Chunk.";
    [ObservableProperty] private string _errorText    = string.Empty;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private bool   _hasChunks;
    [ObservableProperty] private int    _totalFiles;
    [ObservableProperty] private int    _totalTokens;
    [ObservableProperty] private int    _totalProjects;
    [ObservableProperty] private int    _chunkCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxTokensLabel))]
    [NotifyPropertyChangedFor(nameof(SliderWarningText))]
    [NotifyPropertyChangedFor(nameof(SliderWarningColor))]
    private double _maxTokensPerChunk = 16000;

    // ── Computed ────────────────────────────────────────────────────────────

    public bool IsConfigurationMode     => !IsResultsMode;
    public bool IsResultsAndNoSelection => IsResultsMode; // refined by ChunkListViewModel.HasSelection in XAML

    public string MaxTokensLabel => $"{MaxTokensPerChunk:N0}";

    public string CompactSummaryText
    {
        get
        {
            if (!IsResultsMode) return string.Empty;
            var name = string.IsNullOrWhiteSpace(_repo.RepoUrl) ? "—" : ShortRepoName;
            return $"{name}  •  {_repo.Branch}  •  ~{TotalTokens:N0} tokens";
        }
    }

    private string ShortRepoName =>
        _repo.RepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? _repo.RepoUrl.Replace("https://github.com/", "").TrimEnd('/')
            : Path.GetFileName(_repo.RepoUrl.TrimEnd('/', '\\'));

    public string SliderWarningText => (int)MaxTokensPerChunk switch
    {
        < 2000  => "⚠️ Very few tokens — many chunks.",
        < 4097  => "ℹ️ Fits GPT-3.5 (4K context).",
        < 8193  => "✅ Good balance — GPT-4, Claude and Gemini.",
        < 16001 => "ℹ️ Compatible with GPT-4 Turbo, Claude 3+, Gemini 1.5.",
        < 25001 => "⚠️ Large chunks — some interfaces may reject this.",
        _       => "🚨 Very large chunks — use the share sheet.",
    };

    public Color SliderWarningColor => (int)MaxTokensPerChunk switch
    {
        < 2000  => Color.FromArgb("#EF4444"),
        < 8193  => Color.FromArgb("#00B4BC"),
        < 25001 => Color.FromArgb("#F59E0B"),
        _       => Color.FromArgb("#EF4444"),
    };

    // ── Messenger ───────────────────────────────────────────────────────────

    public void Receive(RepoSelectedMessage message)
    {
        if (message.Repo.SavedMaxTokens > 0)
            MaxTokensPerChunk = message.Repo.SavedMaxTokens;
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        if (IsBusy) return;
        HasError = false; ErrorText = string.Empty;
        IsBusy = true; IsResultsMode = true;
        _filter.AutoDetectStatusText = string.Empty;

        _cts?.Cancel(); _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var extensions      = _filter.GetSelectedExtensions();
            var excludedFolders = _filter.GetExcludedFolders().ToList();
            var excludedPat     = _filter.GetExcludedFilePatterns().ToList();
            var progress        = new Progress<string>(msg => StatusText = msg);
            var inputPath       = _repo.RepoUrl.Trim().Trim('"');

            List<(string Path, string Content)> files;

            if (IsLocalPath(inputPath))
            {
                files = await _localFileService.ReadFilesAsync(
                    inputPath, extensions, excludedFolders, excludedPat, progress, _cts.Token);
            }
            else
            {
                files = await _gitHubService.FetchFilesAsync(
                    inputPath, extensions, _repo.AccessToken, _repo.Branch,
                    excludedFolders, excludedPat, progress, _cts.Token);

                if (!string.IsNullOrWhiteSpace(_repo.AccessToken))
                {
                    try { await SecureStorage.Default.SetAsync("github_token", _repo.AccessToken); _repo.TokenIsSaved = true; }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not save token."); }
                }
                await _repo.SaveCurrentRepoAsync(inputPath, _repo.Branch);
            }

            if (!string.IsNullOrWhiteSpace(_filter.KeywordFilter))
            {
                var kw = _filter.KeywordFilter.Trim();
                files  = files.Where(f => f.Content.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
                if (files.Count == 0)
                    throw new InvalidOperationException(
                        $"No files contain the keyword \"{kw}\". Clear the keyword filter or try a different term.");
            }

            _filter.PopulateFetchedFiles(files);

            TotalFiles = files.Count;
            StatusText = $"Chunking {files.Count} files…";

            var token  = _cts.Token;
            var chunks = await Task.Run(
                () => _chunkingService.CreateChunks(files, (int)MaxTokensPerChunk, token), token);

            TotalTokens   = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();
            ChunkCount    = chunks.Count;
            HasChunks     = ChunkCount > 0;
            StatusText    = $"✅  {files.Count} files · {TotalProjects} projects · {ChunkCount} chunks · ~{TotalTokens:N0} tokens";

            WeakReferenceMessenger.Default.Send(
                new FetchCompletedMessage(chunks, TotalFiles, TotalTokens, TotalProjects));

            OnPropertyChanged(nameof(CompactSummaryText));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled."; IsResultsMode = false;
        }
        catch (Exception ex)
        {
            HasError = true; ErrorText = ex.Message;
            StatusText = "Error — see message below."; IsResultsMode = false;
            _logger.LogError(ex, "Fetch error.");
        }
        finally { IsBusy = false; }
    }

    private bool CanFetch() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(_repo.RepoUrl) &&
        _filter.FileTypeFilters.Any(f => f.IsEnabled);

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void BackToConfiguration() => IsResultsMode = false;

    [RelayCommand]
    private void Reset()
    {
        HasChunks = HasError = false; ErrorText = string.Empty;
        IsResultsMode = false;
        ChunkCount = TotalFiles = TotalTokens = TotalProjects = 0;
        _filter.AutoDetectStatusText = string.Empty;
        StatusText = "Enter a GitHub URL or local path, then press Chunk.";
        WeakReferenceMessenger.Default.Send(new FetchResetMessage());
        OnPropertyChanged(nameof(CompactSummaryText));
    }

    [RelayCommand]
    private void SetPresetTokens(string value)
    {
        if (int.TryParse(value, out var t)) MaxTokensPerChunk = t;
    }

    // ── Private ─────────────────────────────────────────────────────────────

    private static bool IsLocalPath(string path) =>
        !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        (path.StartsWith('/') || path.StartsWith('~') ||
         (path.Length >= 2 && path[1] == ':') || path.StartsWith(@"\\"));

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
