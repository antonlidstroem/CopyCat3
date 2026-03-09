using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyCat.Models;
using CopyCat.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace CopyCat.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IGitHubService         _gitHubService;
    private readonly IChunkingService       _chunkingService;
    private readonly IClipboardService      _clipboard;
    private readonly IShareService          _shareService;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _cts;
    private bool                     _initialized;
    private bool                     _disposed;

    private readonly List<(FileTypeFilter Filter, PropertyChangedEventHandler Handler)>
        _fileTypeHandlers = [];

    private readonly List<(CodeChunk Chunk, PropertyChangedEventHandler Handler)>
        _chunkHandlers = [];

    // ── Observable properties ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _repoUrl = string.Empty;

    partial void OnRepoUrlChanged(string value)
    {
        // Rensa grenar när URL ändras så att användaren vet att de behöver hämta igen
        if (!string.IsNullOrWhiteSpace(value) && !value.Contains("github.com"))
            return;
        BranchOptions.Clear();
        OnPropertyChanged(nameof(HasBranchOptions));
    }

    [ObservableProperty]
    private string _accessToken = string.Empty;

    [ObservableProperty]
    private string _branch = "main";

    [ObservableProperty]
    private bool _tokenIsSaved;

    [ObservableProperty]
    private bool _isFileTypesExpanded = true;

    [ObservableProperty]
    private bool _isFoldersExpanded;

    [ObservableProperty]
    private bool _isFilePatternsExpanded;

    [ObservableProperty]
    private bool _isFetchingBranches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxTokensLabel))]
    [NotifyPropertyChangedFor(nameof(SliderWarningText))]
    [NotifyPropertyChangedFor(nameof(SliderWarningColor))]
    private double _maxTokensPerChunk = 10000;

    public string MaxTokensLabel => $"{MaxTokensPerChunk:N0}";

    // ── Slider contextual warnings ─────────────────────────────────────────────

    public string SliderWarningText
    {
        get
        {
            var t = (int)MaxTokensPerChunk;
            return t switch
            {
                < 2000  => "⚠️ Mycket få tokens — ger många chunks. Använd multiselect för att kombinera.",
                < 4097  => "ℹ️ Passar GPT-3.5 (4K) och de flesta enklare AI-chattgränssnitt.",
                < 8193  => "✅ Bra balans — fungerar med GPT-4, Claude och Gemini.",
                < 16001 => "ℹ️ Över GPT-3.5-gränsen. Fungerar med GPT-4 Turbo, Claude 3+ och Gemini 1.5.",
                < 25001 => "⚠️ Stora chunks — vissa AI-gränssnitt kanske inte accepterar inklistrad text. Föredra share sheet (↗).",
                _       => "🚨 Mycket stora chunks — använd share sheet (↗) istället för urklipp för bäst resultat."
            };
        }
    }

    public Color SliderWarningColor
    {
        get
        {
            var t = (int)MaxTokensPerChunk;
            if (t < 2000 || t > 24999)
                return Color.FromArgb("#FF7070"); // TextError – röd/orange
            if (t is >= 4097 and < 8193)
                return Color.FromArgb("#22C55E"); // AccentGreen – grön
            return Color.FromArgb("#A0A0C0");     // TextSecondary – neutral
        }
    }

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

    // ── Multi-select state ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShareSelectedLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedTokensLabel))]
    [NotifyCanExecuteChangedFor(nameof(ShareSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;

    public string ShareSelectedLabel => $"Dela markerade  ({SelectedCount})";

    public string SelectedTokensLabel
    {
        get
        {
            var total = Chunks
                .Where(c => c.IsSelected)
                .Sum(c => c.EstimatedTokens);
            return total == 0 ? string.Empty : $"~{total:N0} tokens markerade";
        }
    }

    // ── Collections ────────────────────────────────────────────────────────────

    public ObservableCollection<CodeChunk>       Chunks             { get; } = [];
    public ObservableCollection<FileTypeFilter>  FileTypeFilters    { get; } = [];
    public ObservableCollection<FolderFilter>    FolderFilters      { get; } = [];
    public ObservableCollection<FilePatternFilter> FilePatternFilters { get; } = [];
    public ObservableCollection<string>          RecentUrls         { get; } = [];
    public ObservableCollection<string>          BranchOptions      { get; } = [];

    public bool HasRecentUrls    => RecentUrls.Count > 0;
    public bool HasBranchOptions => BranchOptions.Count > 0;

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel(
        IGitHubService         gitHubService,
        IChunkingService       chunkingService,
        IClipboardService      clipboard,
        IShareService          shareService,
        ILogger<MainViewModel> logger)
    {
        _gitHubService   = gitHubService;
        _chunkingService = chunkingService;
        _clipboard       = clipboard;
        _shareService    = shareService;
        _logger          = logger;

        InitFileTypeFilters();
        InitFolderFilters();
        InitFilePatternFilters();

        RecentUrls.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentUrls));
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var token = await SecureStorage.Default.GetAsync("github_token");
            if (!string.IsNullOrWhiteSpace(token))
            {
                AccessToken  = token;
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

    // ── Filter init ────────────────────────────────────────────────────────────

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

    private void InitFilePatternFilters()
    {
        var defaults = new[]
        {
            new FilePatternFilter { Pattern = "*Test*",        IsEnabled = false },
            new FilePatternFilter { Pattern = "*Spec*",        IsEnabled = false },
            new FilePatternFilter { Pattern = "*_test.*",      IsEnabled = false },
            new FilePatternFilter { Pattern = "*.spec.*",      IsEnabled = false },
            new FilePatternFilter { Pattern = "*.min.*",       IsEnabled = true  },
            new FilePatternFilter { Pattern = "*.generated.*", IsEnabled = true  },
            new FilePatternFilter { Pattern = "*.Designer.*",  IsEnabled = true  },
            new FilePatternFilter { Pattern = "*Reference*",   IsEnabled = false },
            new FilePatternFilter { Pattern = "*Migration*",   IsEnabled = false },
        };

        foreach (var f in defaults)
            FilePatternFilters.Add(f);
    }

    // ── Chunk subscription ─────────────────────────────────────────────────────

    private void SubscribeChunk(CodeChunk chunk)
    {
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName != nameof(CodeChunk.IsSelected)) return;
            RefreshSelectedCount();
        };
        chunk.PropertyChanged += handler;
        _chunkHandlers.Add((chunk, handler));
    }

    private void UnsubscribeAllChunks()
    {
        foreach (var (chunk, handler) in _chunkHandlers)
            chunk.PropertyChanged -= handler;
        _chunkHandlers.Clear();
    }

    private void RefreshSelectedCount()
    {
        SelectedCount = Chunks.Count(c => c.IsSelected);
        OnPropertyChanged(nameof(SelectedTokensLabel));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

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

    private IEnumerable<string> GetExcludedFilePatterns() =>
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

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsBusy        = true;
        HasError      = false;
        ErrorText     = string.Empty;
        HasChunks     = false;
        CopiedCount   = 0;
        SelectedCount = 0;

        UnsubscribeAllChunks();
        Chunks.Clear();
        ChunkCount = 0;

        try
        {
            var extensions      = GetSelectedExtensions();
            var excludedFolders = GetExcludedFolders().ToList();
            var excludedPatterns = GetExcludedFilePatterns().ToList();
            var progress        = new Progress<string>(msg => StatusText = msg);

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

            var token  = _cts.Token;
            var chunks = await Task.Run(
                () => _chunkingService.CreateChunks(files, (int)MaxTokensPerChunk, token),
                token);

            TotalTokens   = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();

            foreach (var chunk in chunks)
            {
                SubscribeChunk(chunk);
                Chunks.Add(chunk);
            }

            ChunkCount = Chunks.Count;
            HasChunks  = ChunkCount > 0;

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
            HasError   = true;
            ErrorText  = ex.Message;
            StatusText = "Fel — se meddelande nedan.";
            _logger.LogError(ex, "Fel vid hämtning av repo.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Hämtar grenar för det angivna repot och fyller BranchOptions-listan.
    /// </summary>
    [RelayCommand]
    private async Task FetchBranchesAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || !RepoUrl.Contains("github.com"))
        {
            StatusText = "⚠️ Ange en giltig GitHub-URL innan du hämtar grenar.";
            return;
        }

        IsFetchingBranches = true;
        try
        {
            var branches = await _gitHubService.FetchBranchesAsync(
                RepoUrl.Trim(), AccessToken);

            BranchOptions.Clear();
            foreach (var b in branches)
                BranchOptions.Add(b);

            OnPropertyChanged(nameof(HasBranchOptions));

            if (BranchOptions.Count == 0)
            {
                StatusText = "⚠️ Inga grenar hittades (publikt repo kräver token för gren-listning).";
                return;
            }

            // Välj automatiskt aktuell gren om den finns, annars välj första
            if (!BranchOptions.Contains(Branch))
                Branch = BranchOptions[0];

            StatusText = $"Hittade {BranchOptions.Count} grenar.";
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Kunde inte hämta grenar: {ex.Message}";
            _logger.LogWarning(ex, "Kunde inte hämta grenar.");
        }
        finally
        {
            IsFetchingBranches = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Kopierar en enskild chunk till urklipp.
    /// Återställer IsCopied på alla andra chunks (bara en aktiv åt gången).
    /// </summary>
    [RelayCommand]
    private async Task CopyChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            await _clipboard.SetTextAsync(chunk.Content);

            // Återställ alla andra chunks — bara en grön åt gången
            foreach (var c in Chunks)
                c.IsCopied = false;

            chunk.IsCopied = true;
            CopiedCount    = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Kunde inte kopiera: {ex.Message}";
            _logger.LogWarning(ex, "Kunde inte kopiera chunk till urklipp.");
        }
    }

    /// <summary>Skickar en enskild chunk direkt till share sheet.</summary>
    [RelayCommand]
    private async Task ShareChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            var title = $"Chunk {chunk.Index + 1} · {chunk.ProjectName}";
            await _shareService.ShareTextAsync(chunk.Content, title);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Kunde inte öppna share sheet: {ex.Message}";
            _logger.LogWarning(ex, "Share sheet misslyckades för enskild chunk.");
        }
    }

    /// <summary>Togglar förhandsvisningen av chunk-innehållet.</summary>
    [RelayCommand]
    private void TogglePreview(CodeChunk chunk)
    {
        if (chunk is null) return;
        chunk.IsPreviewExpanded = !chunk.IsPreviewExpanded;
    }

    /// <summary>Togglar IsSelected för multi-select.</summary>
    [RelayCommand]
    private void ToggleChunkSelection(CodeChunk chunk)
    {
        if (chunk is null) return;
        chunk.IsSelected = !chunk.IsSelected;
    }

    /// <summary>Slår ihop alla markerade chunks och öppnar share sheet.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ShareSelectedAsync()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var chunk in selected)
        {
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(chunk.Content);
        }

        var title = selected.Count == 1
            ? $"Chunk {selected[0].Index + 1} · {selected[0].ProjectName}"
            : $"{selected.Count} chunks · {string.Join(", ", selected.Select(c => c.ProjectName).Distinct())}";

        try
        {
            await _shareService.ShareTextAsync(sb.ToString(), title);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Kunde inte öppna share sheet: {ex.Message}";
            _logger.LogWarning(ex, "Share sheet misslyckades för markerade chunks.");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelection()
    {
        foreach (var chunk in Chunks)
            chunk.IsSelected = false;
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
        try   { SecureStorage.Default.Remove("github_token"); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kunde inte ta bort GitHub-token från SecureStorage.");
        }

        AccessToken  = string.Empty;
        TokenIsSaved = false;
    }

    [RelayCommand]
    private void Reset()
    {
        UnsubscribeAllChunks();
        Chunks.Clear();
        ChunkCount    = 0;
        CopiedCount   = 0;
        SelectedCount = 0;
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

    [RelayCommand]
    private void ToggleFilePatterns() => IsFilePatternsExpanded = !IsFilePatternsExpanded;

    // ── IDisposable ────────────────────────────────────────────────────────────

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

        UnsubscribeAllChunks();
    }
}
