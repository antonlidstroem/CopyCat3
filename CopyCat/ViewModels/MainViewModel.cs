using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyCat.Models;
using CopyCat.Services;
using Microsoft.Extensions.Logging;

namespace CopyCat.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IGitHubService _gitHubService;
    private readonly IChunkingService _chunkingService;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _cts;
    private bool _initialized;
    private bool _disposed;
    private readonly List<(FileTypeFilter Filter, PropertyChangedEventHandler Handler)>
        _fileTypeHandlers = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _repoUrl = string.Empty;

    [ObservableProperty]
    private string _accessToken = string.Empty;

    [ObservableProperty]
    private string _branch = "master";

    [ObservableProperty]
    private bool _tokenIsSaved;

    [ObservableProperty]
    private bool _isFileTypesExpanded = true;

    [ObservableProperty]
    private bool _isFoldersExpanded;

    [ObservableProperty]
    private bool _isPatternsExpanded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPatternCommand))]
    private string _newPatternName = string.Empty;

    [ObservableProperty]
    private double _maxTokensPerChunk = 10000;
    public string MaxTokensLabel => $"{MaxTokensPerChunk:N0}";
    partial void OnMaxTokensPerChunkChanged(double value) =>
        OnPropertyChanged(nameof(MaxTokensLabel));

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

    public ObservableCollection<CodeChunk> Chunks { get; } = [];
    public ObservableCollection<FileTypeFilter> FileTypeFilters { get; } = [];
    public ObservableCollection<FolderFilter> FolderFilters { get; } = [];
    public ObservableCollection<FilePatternFilter> FilePatternFilters { get; } = [];
    public ObservableCollection<string> RecentUrls { get; } = [];
    public bool HasRecentUrls => RecentUrls.Count > 0;

    public MainViewModel(
        IGitHubService gitHubService,
        IChunkingService chunkingService,
        IClipboardService clipboard,
        ILogger<MainViewModel> logger)
    {
        _gitHubService = gitHubService;
        _chunkingService = chunkingService;
        _clipboard = clipboard;
        _logger = logger;
        InitFileTypeFilters();
        InitFolderFilters();
        InitPatternFilters();
        RecentUrls.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentUrls));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var token = await SecureStorage.Default.GetAsync("github_token");
            if (!string.IsNullOrWhiteSpace(token))
            {
                AccessToken = token;
                TokenIsSaved = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kunde inte läsa GitHub-token från SecureStorage.");
        }
        try
        {
            var recents = Preferences.Default.Get("recent_urls", string.Empty);
            if (!string.IsNullOrWhiteSpace(recents))
            {
                foreach (var url in recents.Split('|')
                             .Where(u => !string.IsNullOrWhiteSpace(u))
                             .Take(5))
                    RecentUrls.Add(url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kunde inte läsa senaste URL:er från Preferences.");
        }
    }

    // ── Init ─────────────────────────────────────────────────────────

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
            PropertyChangedEventHandler handler =
                (_, _) => FetchCommand.NotifyCanExecuteChanged();
            f.PropertyChanged += handler;
            _fileTypeHandlers.Add((f, handler));
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

    private void InitPatternFilters()
    {
        var defaults = new[]
        {
            "*.Designer.cs",
            "*.g.cs",
            "*.g.i.cs",
            "*.AssemblyInfo.cs",
        };
        foreach (var pattern in defaults)
            FilePatternFilters.Add(new FilePatternFilter { Pattern = pattern, IsEnabled = true });
    }

    // ── CanExecute ───────────────────────────────────────────────────

    private bool CanFetch() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(RepoUrl) &&
        FileTypeFilters.Any(f => f.IsEnabled);

    private bool CanAddFolder() => !string.IsNullOrWhiteSpace(NewFolderName);
    private bool CanAddPattern() => !string.IsNullOrWhiteSpace(NewPatternName);

    // ── Data helpers ─────────────────────────────────────────────────

    private List<string> GetSelectedExtensions() =>
        FileTypeFilters
            .Where(f => f.IsEnabled)
            .SelectMany(f => f.Extensions)
            .ToList();

    private IEnumerable<string> GetExcludedFolders() =>
        FolderFilters
            .Where(f => f.IsExcluded)
            .Select(f => f.Name);

    private IEnumerable<string> GetExcludedPatterns() =>
        FilePatternFilters
            .Where(f => f.IsEnabled)
            .Select(f => f.Pattern);

    private void SaveRecentUrl(string url)
    {
        if (RecentUrls.Contains(url))
            RecentUrls.Remove(url);
        RecentUrls.Insert(0, url);
        while (RecentUrls.Count > 5)
            RecentUrls.RemoveAt(RecentUrls.Count - 1);
        try
        {
            Preferences.Default.Set("recent_urls", string.Join("|", RecentUrls));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kunde inte spara senaste URL:er i Preferences.");
        }
    }

    // ── Folder commands ───────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAddFolder))]
    private void AddFolder()
    {
        var name = NewFolderName.Trim().Trim('/').ToLowerInvariant();
        if (string.IsNullOrEmpty(name)) return;
        if (FolderFilters.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            NewFolderName = string.Empty;
            return;
        }
        FolderFilters.Add(new FolderFilter { Name = name, IsExcluded = true });
        NewFolderName = string.Empty;
    }

    [RelayCommand]
    private void RemoveFolder(FolderFilter folder)
    {
        if (folder is not null)
            FolderFilters.Remove(folder);
    }

    [RelayCommand]
    private void ToggleFolders() => IsFoldersExpanded = !IsFoldersExpanded;

    // ── Pattern commands ──────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAddPattern))]
    private void AddPattern()
    {
        var pattern = NewPatternName.Trim();
        if (string.IsNullOrEmpty(pattern)) return;
        if (FilePatternFilters.Any(f =>
                string.Equals(f.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
        {
            NewPatternName = string.Empty;
            return;
        }
        FilePatternFilters.Add(new FilePatternFilter { Pattern = pattern, IsEnabled = true });
        NewPatternName = string.Empty;
    }

    [RelayCommand]
    private void RemovePattern(FilePatternFilter pattern)
    {
        if (pattern is not null)
            FilePatternFilters.Remove(pattern);
    }

    [RelayCommand]
    private void TogglePatterns() => IsPatternsExpanded = !IsPatternsExpanded;

    // ── Preview command ───────────────────────────────────────────────

    /// <summary>
    /// Shows a dialog listing the files contained in the given chunk.
    ///
    /// Uses Application.Current.MainPage.DisplayAlert — NOT Shell.Current —
    /// because this app uses NavigationPage, not Shell. Shell.Current is null.
    /// </summary>
    [RelayCommand]
    private async Task ShowPreviewAsync(CodeChunk chunk)
    {
        if (chunk is null) return;

        var files = chunk.FileNames;
        var message = files.Count > 0
            ? string.Join("\n", files)
            : "(inga filer)";

        var page = Application.Current?.MainPage;
        if (page is null) return;

        await page.DisplayAlert(
            $"Chunk {chunk.Index + 1} · {chunk.ProjectName}",
            message,
            "Stäng");
    }

    // ── Fetch ─────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsBusy = true;
        HasError = false;
        ErrorText = string.Empty;
        HasChunks = false;
        CopiedCount = 0;
        Chunks.Clear();
        ChunkCount = 0;

        try
        {
            var extensions = GetSelectedExtensions();
            var excludedFolders = GetExcludedFolders().ToList();
            var excludedPatterns = GetExcludedPatterns().ToList();
            var progress = new Progress<string>(msg => StatusText = msg);

            var files = await _gitHubService.FetchFilesAsync(
                RepoUrl.Trim(),
                extensions,
                AccessToken,
                Branch,
                excludedFolders,
                excludedPatterns,
                progress,
                _cts.Token);

            TotalFiles = files.Count;
            StatusText = $"Chunkar {files.Count} filer…";

            var chunks = await Task.Run(
                () => _chunkingService.CreateChunks(files, (int)MaxTokensPerChunk),
                _cts.Token);

            TotalTokens = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();

            foreach (var chunk in chunks)
                Chunks.Add(chunk);

            ChunkCount = Chunks.Count;
            HasChunks = ChunkCount > 0;
            StatusText =
                $"✅  {files.Count} filer · {TotalProjects} projekt · " +
                $"{ChunkCount} chunks · ~{TotalTokens:N0} tokens";

            SaveRecentUrl(RepoUrl.Trim());

            if (!string.IsNullOrWhiteSpace(AccessToken))
            {
                try
                {
                    await SecureStorage.Default.SetAsync("github_token", AccessToken);
                    TokenIsSaved = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Kunde inte spara GitHub-token i SecureStorage.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Avbruten.";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorText = ex.Message;
            StatusText = "Fel — se meddelande nedan.";
            _logger.LogError(ex, "Fel vid hämtning av repo.");
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
            CopiedCount = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Kunde inte kopiera: {ex.Message}";
            _logger.LogWarning(ex, "Kunde inte kopiera chunk till urklipp.");
        }
    }

    [RelayCommand]
    private async Task ShareChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            var title = $"Chunk {chunk.Index + 1} · {chunk.ProjectName}";
            await _clipboard.ShareAsync(chunk.Content, title);
            // Mark as copied so the UI gives the same visual feedback.
            chunk.IsCopied = true;
            CopiedCount = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Kunde inte dela: {ex.Message}";
            _logger.LogWarning(ex, "Kunde inte dela chunk via share sheet.");
        }
    }

    [RelayCommand]
    private void ResetChunk(CodeChunk chunk)
    {
        if (chunk is null) return;
        chunk.IsCopied = false;
        CopiedCount = Chunks.Count(c => c.IsCopied);
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
        try { SecureStorage.Default.Remove("github_token"); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kunde inte ta bort GitHub-token från SecureStorage.");
        }
        AccessToken = string.Empty;
        TokenIsSaved = false;
    }

    [RelayCommand]
    private void Reset()
    {
        Chunks.Clear();
        ChunkCount = 0;
        CopiedCount = 0;
        HasChunks = false;
        HasError = false;
        ErrorText = string.Empty;
        TotalFiles = 0;
        TotalTokens = 0;
        TotalProjects = 0;
        StatusText = "Ange en GitHub-URL och tryck Hämta.";
    }

    [RelayCommand]
    private void ToggleFileTypes() => IsFileTypesExpanded = !IsFileTypesExpanded;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        foreach (var (filter, handler) in _fileTypeHandlers)
            filter.PropertyChanged -= handler;
        _fileTypeHandlers.Clear();
    }
}
