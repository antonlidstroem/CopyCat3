using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyCat.Models;
using CopyCat.Services;
using System.Collections.ObjectModel;

namespace CopyCat.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly IGitHubService    _gitHubService;
    private readonly IChunkingService  _chunkingService;
    private readonly IClipboardService _clipboard;
    private CancellationTokenSource?   _cts;
    private bool                       _initialized;

    // ── Repository ────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _repoUrl = string.Empty;

    [ObservableProperty]
    private string _accessToken = string.Empty;

    [ObservableProperty]
    private string _branch = "main";

    [ObservableProperty]
    private bool _tokenIsSaved;

    // ── Section expand/collapse ────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isFileTypesExpanded = true;

    [ObservableProperty]
    private bool _isFoldersExpanded;

    // ── Token slider ──────────────────────────────────────────────────────────
    [ObservableProperty]
    private double _maxTokensPerChunk = 10000;

    public string MaxTokensLabel => $"{MaxTokensPerChunk:N0}";

    partial void OnMaxTokensPerChunkChanged(double value) =>
        OnPropertyChanged(nameof(MaxTokensLabel));

    // ── State ─────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ange en GitHub-URL och tryck Hämta.";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasChunks;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _totalTokens;

    [ObservableProperty]
    private int _totalProjects;

    [ObservableProperty]
    private int _chunkCount;

    [ObservableProperty]
    private int _copiedCount;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<CodeChunk>      Chunks          { get; } = [];
    public ObservableCollection<FileTypeFilter> FileTypeFilters { get; } = [];
    public ObservableCollection<FolderFilter>   FolderFilters   { get; } = [];
    public ObservableCollection<string>         RecentUrls      { get; } = [];

    public bool HasRecentUrls => RecentUrls.Count > 0;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainViewModel(
        IGitHubService    gitHubService,
        IChunkingService  chunkingService,
        IClipboardService clipboard)
    {
        _gitHubService   = gitHubService;
        _chunkingService = chunkingService;
        _clipboard       = clipboard;

        InitFileTypeFilters();
        InitFolderFilters();

        RecentUrls.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentUrls));
    }

    // ── Initialization (called from MainPage.OnAppearing) ─────────────────────
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // Load saved GitHub token from secure storage
        try
        {
            var token = await SecureStorage.Default.GetAsync("github_token");
            if (!string.IsNullOrWhiteSpace(token))
            {
                AccessToken  = token;
                TokenIsSaved = true;
            }
        }
        catch { /* SecureStorage unavailable on some platforms/emulators */ }

        // Load recent URLs from Preferences
        var recents = Preferences.Default.Get("recent_urls", string.Empty);
        if (!string.IsNullOrWhiteSpace(recents))
        {
            foreach (var url in recents.Split('|')
                         .Where(u => !string.IsNullOrWhiteSpace(u))
                         .Take(5))
                RecentUrls.Add(url);
        }
    }

    // ── Filter setup ──────────────────────────────────────────────────────────
    private void InitFileTypeFilters()
    {
        var filters = new[]
        {
            new FileTypeFilter { Label = ".cs",     Extensions = [".cs"],           IsEnabled = true  },
            new FileTypeFilter { Label = ".xaml",   Extensions = [".xaml"],         IsEnabled = true  },
            new FileTypeFilter { Label = ".json",   Extensions = [".json"],         IsEnabled = true  },
            new FileTypeFilter { Label = ".csproj", Extensions = [".csproj"],       IsEnabled = true  },
            new FileTypeFilter { Label = ".js",     Extensions = [".js", ".mjs"],   IsEnabled = false },
            new FileTypeFilter { Label = ".ts",     Extensions = [".ts", ".tsx"],   IsEnabled = false },
            new FileTypeFilter { Label = ".jsx",    Extensions = [".jsx"],          IsEnabled = false },
            new FileTypeFilter { Label = ".vue",    Extensions = [".vue"],          IsEnabled = false },
            new FileTypeFilter { Label = ".py",     Extensions = [".py"],           IsEnabled = false },
            new FileTypeFilter { Label = ".java",   Extensions = [".java"],         IsEnabled = false },
            new FileTypeFilter { Label = ".c/.h",   Extensions = [".c", ".h"],      IsEnabled = false },
            new FileTypeFilter { Label = ".cpp",    Extensions = [".cpp", ".hpp"],  IsEnabled = false },
            new FileTypeFilter { Label = ".md",     Extensions = [".md"],           IsEnabled = false },
            new FileTypeFilter { Label = ".sql",    Extensions = [".sql"],          IsEnabled = false },
            new FileTypeFilter { Label = ".yaml",   Extensions = [".yaml", ".yml"], IsEnabled = false },
        };

        foreach (var f in filters)
        {
            f.PropertyChanged += (_, _) => FetchCommand.NotifyCanExecuteChanged();
            FileTypeFilters.Add(f);
        }
    }

    private void InitFolderFilters()
    {
        var defaults = new[]
        {
            "bin", "obj", ".git", ".vs", "node_modules",
            "packages", "dist", "build", ".idea",
            "__pycache__", ".gradle", "out", ".next"
        };

        foreach (var name in defaults)
            FolderFilters.Add(new FolderFilter { Name = name, IsExcluded = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private bool CanFetch() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(RepoUrl) &&
        FileTypeFilters.Any(f => f.IsEnabled);

    private List<string> GetSelectedExtensions() =>
        FileTypeFilters
            .Where(f => f.IsEnabled)
            .SelectMany(f => f.Extensions)
            .ToList();

    private IEnumerable<string> GetExcludedFolders() =>
        FolderFilters
            .Where(f => f.IsExcluded)
            .Select(f => f.Name);

    private void SaveRecentUrl(string url)
    {
        if (RecentUrls.Contains(url))
            RecentUrls.Remove(url);

        RecentUrls.Insert(0, url);

        while (RecentUrls.Count > 5)
            RecentUrls.RemoveAt(RecentUrls.Count - 1);

        Preferences.Default.Set("recent_urls", string.Join("|", RecentUrls));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsBusy       = true;
        HasError     = false;
        ErrorText    = string.Empty;
        HasChunks    = false;
        CopiedCount  = 0;
        Chunks.Clear();
        ChunkCount = 0;

        try
        {
            var extensions      = GetSelectedExtensions();
            var excludedFolders = GetExcludedFolders().ToList();
            var progress        = new Progress<string>(msg => StatusText = msg);

            var files = await _gitHubService.FetchFilesAsync(
                RepoUrl.Trim(),
                extensions,
                AccessToken,
                Branch,
                excludedFolders,
                progress,
                _cts.Token);

            TotalFiles = files.Count;
            StatusText = $"Chunkar {files.Count} filer…";

            var chunks = await Task.Run(
                () => _chunkingService.CreateChunks(files, (int)MaxTokensPerChunk),
                _cts.Token);

            TotalTokens   = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();

            foreach (var chunk in chunks)
                Chunks.Add(chunk);

            ChunkCount = Chunks.Count;
            HasChunks  = ChunkCount > 0;

            StatusText =
                $"✅  {files.Count} filer · {TotalProjects} projekt · " +
                $"{ChunkCount} chunks · ~{TotalTokens:N0} tokens";

            SaveRecentUrl(RepoUrl.Trim());

            // Persist token if one was provided
            if (!string.IsNullOrWhiteSpace(AccessToken))
            {
                try
                {
                    await SecureStorage.Default.SetAsync("github_token", AccessToken);
                    TokenIsSaved = true;
                }
                catch { /* ignore on platforms without SecureStorage */ }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Avbruten.";
        }
        catch (Exception ex)
        {
            HasError  = true;
            ErrorText = ex.Message;
            StatusText = "Fel — se meddelande nedan.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task CopyChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            await _clipboard.SetTextAsync(chunk.Content);
            chunk.IsCopied = true;
            CopiedCount    = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex)
        {
            HasError  = true;
            ErrorText = $"Kunde inte kopiera: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetChunk(CodeChunk chunk)
    {
        if (chunk is null) return;
        chunk.IsCopied = false;
        CopiedCount    = Chunks.Count(c => c.IsCopied);
    }

    [RelayCommand]
    private async Task PasteUrlAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text) && text.Contains("github.com"))
            RepoUrl = text.Trim();
    }

    [RelayCommand]
    private void SelectRecentUrl(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
            RepoUrl = url;
    }

    [RelayCommand]
    private void ClearToken()
    {
        try { SecureStorage.Default.Remove("github_token"); } catch { }
        AccessToken  = string.Empty;
        TokenIsSaved = false;
    }

    [RelayCommand]
    private void Reset()
    {
        Chunks.Clear();
        ChunkCount    = 0;
        CopiedCount   = 0;
        HasChunks     = false;
        HasError      = false;
        ErrorText     = string.Empty;
        TotalFiles    = 0;
        TotalTokens   = 0;
        TotalProjects = 0;
        StatusText    = "Ange en GitHub-URL och tryck Hämta.";
    }

    [RelayCommand]
    private void ToggleFileTypes() => IsFileTypesExpanded = !IsFileTypesExpanded;

    [RelayCommand]
    private void ToggleFolders() => IsFoldersExpanded = !IsFoldersExpanded;
}
