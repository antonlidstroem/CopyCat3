using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyCat.Models;
using CopyCat.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace CopyCat.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IGitHubService         _gitHubService;
    private readonly IChunkingService       _chunkingService;
    private readonly IClipboardService      _clipboard;
    private readonly IShareService          _shareService;
    private readonly IDatabaseService       _db;
    private readonly ILocalFileService      _localFileService;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _cts;
    private bool                     _initialized;
    private bool                     _disposed;
    private CancellationTokenSource? _autoDetectDebounce;

    // ── Event-handler lists for explicit unsubscription ────────────────────
    private readonly List<(FileTypeFilter Filter, PropertyChangedEventHandler Handler)> _fileTypeHandlers = [];
    private readonly List<(CodeChunk      Chunk,  PropertyChangedEventHandler Handler)> _chunkHandlers    = [];
    private readonly List<(PromptItem     Prompt, PropertyChangedEventHandler Handler)> _promptHandlers   = [];

    // ── Events for code-behind dialogs ─────────────────────────────────────
    public event EventHandler<List<string>>?           BranchPickerRequested;
    public event EventHandler?                         TokenInfoRequested;
    public event EventHandler<SavedRepo>?              RepoRenameRequested;
    public event EventHandler<List<Models.SavedRepo>>? ShowHistoryRequested;
    public event EventHandler<string>?                 ShowInfoRequested;

    /// <summary>Raised by NavigateToPromptsPageCommand — MainPage pushes PromptsPage.</summary>
    public event EventHandler? NavigateToPromptsPageRequested;

    /// <summary>Raised by GoBackCommand inside PromptsPage — triggers PopAsync.</summary>
    public event EventHandler? GoBackRequested;

    // ── Language → extension inference map ────────────────────────────────
    private static readonly IReadOnlyDictionary<string, string[]> LanguageExtensionInference =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["C#"]         = [".csproj", ".xml"],
            ["TypeScript"] = [".json"],
            ["JavaScript"] = [".json"],
            ["Python"]     = [],
            ["Java"]       = [],
            ["Kotlin"]     = [],
        };

    // ── Observable properties ──────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _repoUrl = string.Empty;

    partial void OnRepoUrlChanged(string _)
    {
        BranchOptions.Clear();
        OnPropertyChanged(nameof(HasBranchOptions));
        OnPropertyChanged(nameof(CanAutoDetect));
        AutoDetectStatusText = string.Empty;
    }

    [ObservableProperty] private string _accessToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchButtonLabel))]
    [NotifyPropertyChangedFor(nameof(CompactSummaryText))]
    private string _branch = "main";

    partial void OnBranchChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (IsBusy || IsAutoDetecting) return;
        if (string.IsNullOrWhiteSpace(RepoUrl)) return;

        _autoDetectDebounce?.Cancel();
        _autoDetectDebounce = new CancellationTokenSource();
        var token = _autoDetectDebounce.Token;

        Task.Delay(400, token).ContinueWith(t =>
        {
            if (!t.IsCanceled && !IsBusy && !IsAutoDetecting)
                MainThread.BeginInvokeOnMainThread(() =>
                    AutoDetectFileTypesCommand.Execute(null));
        }, TaskScheduler.Default);
    }

    [ObservableProperty] private bool _tokenIsSaved;

    [ObservableProperty] private bool _isFileTypesExpanded;
    [ObservableProperty] private bool _isFoldersExpanded;
    [ObservableProperty] private bool _isFilePatternsExpanded;
    [ObservableProperty] private bool _isKeywordExpanded;
    [ObservableProperty] private bool _isFetchingBranches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchedFilesCountLabel))]
    private bool _isFetchedFilesExpanded;

    [ObservableProperty] private string _keywordFilter      = string.Empty;
    [ObservableProperty] private string _customFolderInput  = string.Empty;
    [ObservableProperty] private string _customPatternInput = string.Empty;

    // ── FilteredChunks — ObservableCollection ─────────────────────────────
    private readonly ObservableCollection<CodeChunk> _filteredChunks = [];

    /// <summary>
    /// ObservableCollection so BindableLayout receives change notifications.
    /// Rebuilt by RefreshFilteredChunks() whenever Chunks or ChunkSearchText changes.
    /// </summary>
    public ObservableCollection<CodeChunk> FilteredChunks => _filteredChunks;

    public bool HasFilteredChunks => _filteredChunks.Count > 0;

    public string ChunkSearchSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ChunkSearchText)) return string.Empty;
            return _filteredChunks.Count == 0
                ? "No chunks match"
                : $"{_filteredChunks.Count} of {Chunks.Count} chunk{(Chunks.Count == 1 ? "" : "s")} match";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChunkSearchSummary))]
    [NotifyPropertyChangedFor(nameof(HasFilteredChunks))]
    private string _chunkSearchText = string.Empty;

    partial void OnChunkSearchTextChanged(string _) => RefreshFilteredChunks();

    /// <summary>
    /// Rebuilds FilteredChunks from Chunks filtered by ChunkSearchText.
    /// Performance: skips rebuild when the result set is unchanged (SequenceEqual).
    /// forceRebuild=true is used when Chunks itself has changed (after fetch or merge).
    /// </summary>
    private void RefreshFilteredChunks(bool forceRebuild = false)
    {
        var src = string.IsNullOrWhiteSpace(ChunkSearchText)
            ? (IEnumerable<CodeChunk>)Chunks
            : Chunks.Where(c =>
                c.DisplayLabel.Contains(ChunkSearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains(ChunkSearchText, StringComparison.OrdinalIgnoreCase));

        var newList = src.ToList();

        if (!forceRebuild && _filteredChunks.SequenceEqual(newList))
        {
            OnPropertyChanged(nameof(ChunkSearchSummary));
            return;
        }

        _filteredChunks.Clear();
        foreach (var c in newList) _filteredChunks.Add(c);
        OnPropertyChanged(nameof(HasFilteredChunks));
        OnPropertyChanged(nameof(ChunkSearchSummary));
    }

    // ── Auto-detect state ──────────────────────────────────────────────────

    [ObservableProperty] private bool _isAutoDetecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoDetectStatus))]
    private string _autoDetectStatusText = string.Empty;

    public bool HasAutoDetectStatus => !string.IsNullOrEmpty(AutoDetectStatusText);

    public bool CanAutoDetect =>
        !IsBusy && !string.IsNullOrWhiteSpace(RepoUrl);

    // ── Collapsed filter summaries ─────────────────────────────────────────

    public string FileTypeCardSummary
    {
        get
        {
            var enabled = FileTypeFilters.Where(f => f.IsEnabled).Select(f => f.Label).ToList();
            if (enabled.Count == 0) return "no types selected";
            const int maxVisible = 4;
            var labels = enabled.Count <= maxVisible
                ? string.Join("  ", enabled)
                : string.Join("  ", enabled.Take(maxVisible)) + $"  +{enabled.Count - maxVisible} more";
            return TotalFiles > 0 ? $"{labels}  ·  {TotalFiles:N0} files" : labels;
        }
    }

    public string FolderFilterSummary
    {
        get
        {
            var excluded = FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name).ToList();
            var included = FolderFilters.Where(f => !f.IsExcluded).Select(f => f.Name).ToList();
            var parts = new List<string>();
            if (excluded.Count > 0) parts.Add($"Excluded: {string.Join(", ", excluded)}");
            if (included.Count > 0) parts.Add($"Included: {string.Join(", ", included)}");
            return parts.Count == 0 ? "no folders configured" : string.Join("  ·  ", parts);
        }
    }

    public string FilePatternSummary
    {
        get
        {
            var active = FilePatternFilters.Where(f => f.IsEnabled).Select(f => f.Pattern).ToList();
            return active.Count == 0 ? "no patterns active" : $"Excluding: {string.Join(", ", active)}";
        }
    }

    // ── AI Prompt compact display ──────────────────────────────────────────

    /// <summary>One-line label shown in the compact prompt card.</summary>
    public string SelectedPromptLabel =>
        SelectedPrompt is not null
            ? $"✓ {SelectedPrompt.Title}"
            : "None selected — tap ⚙ Manage Prompts to choose one";

    /// <summary>True when a prompt is actively selected for prepending to copies.</summary>
    public bool HasSelectedPrompt => SelectedPrompt is not null;

    // ── Fetched file browser ───────────────────────────────────────────────

    public string FetchedFilesCountLabel =>
        $"FETCHED FILES ({FetchedFiles.Count})";

    public bool HasFetchedFiles => FetchedFiles.Count > 0;

    // ── App state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationMode))]
    [NotifyPropertyChangedFor(nameof(IsResultsAndNoSelection))]
    private bool _isResultsMode;

    public bool IsConfigurationMode    => !IsResultsMode;
    public bool IsResultsAndNoSelection => IsResultsMode && !HasSelection;

    public string CompactSummaryText
    {
        get
        {
            if (!IsResultsMode) return string.Empty;
            var name = string.IsNullOrWhiteSpace(RepoUrl) ? "—" : ShortRepoName;
            var br   = string.IsNullOrWhiteSpace(Branch)  ? "—" : Branch;
            return $"{name}  •  {br}  •  ~{TotalTokens:N0} tokens";
        }
    }

    private string ShortRepoName =>
        RepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? RepoUrl.Replace("https://github.com/", "").TrimEnd('/')
            : System.IO.Path.GetFileName(RepoUrl.TrimEnd('/', '\\'));

    public string BranchButtonLabel =>
        string.IsNullOrWhiteSpace(Branch) ? "Select branch…" : Branch;

    // ── Slider ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxTokensLabel))]
    [NotifyPropertyChangedFor(nameof(SliderWarningText))]
    [NotifyPropertyChangedFor(nameof(SliderWarningColor))]
    private double _maxTokensPerChunk = 10000;

    public string MaxTokensLabel => $"{MaxTokensPerChunk:N0}";

    public string SliderWarningText => (int)MaxTokensPerChunk switch
    {
        < 2000  => "⚠️ Very few tokens — many chunks. Use multi-select to combine before sharing.",
        < 4097  => "ℹ️ Fits GPT-3.5 (4K context) and most basic AI chat interfaces.",
        < 8193  => "✅ Good balance — works with GPT-4, Claude and Gemini.",
        < 16001 => "ℹ️ Compatible with GPT-4 Turbo, Claude 3+ and Gemini 1.5.",
        < 25001 => "⚠️ Large chunks — some AI interfaces may reject this size.",
        _       => "🚨 Very large chunks — use the share sheet for best results."
    };

    public Color SliderWarningColor => (int)MaxTokensPerChunk switch
    {
        < 2000  => Color.FromArgb("#EF4444"),
        < 8193  => Color.FromArgb("#00B4BC"),
        < 25001 => Color.FromArgb("#F59E0B"),
        _       => Color.FromArgb("#EF4444"),
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool _) => OnPropertyChanged(nameof(CanAutoDetect));

    [ObservableProperty] private string _statusText   = "Enter a GitHub URL or local path, then press Chunk.";
    [ObservableProperty] private string _errorText    = string.Empty;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private bool   _hasChunks;
    [ObservableProperty] private int    _totalFiles;
    [ObservableProperty] private int    _totalTokens;
    [ObservableProperty] private int    _totalProjects;
    [ObservableProperty] private int    _chunkCount;
    [ObservableProperty] private int    _copiedCount;

    partial void OnTotalFilesChanged(int _)  => OnPropertyChanged(nameof(FileTypeCardSummary));
    partial void OnCopiedCountChanged(int _)
    {
        OnPropertyChanged(nameof(CopyProgressLabel));
        OnPropertyChanged(nameof(HasCopyProgress));
    }

    public string CopyProgressLabel
    {
        get
        {
            if (CopiedCount == 0 || ChunkCount == 0) return string.Empty;
            return CopiedCount == ChunkCount
                ? $"All {ChunkCount} chunks copied ✓"
                : $"Chunk {CopiedCount} of {ChunkCount} copied";
        }
    }

    public bool HasCopyProgress => !string.IsNullOrEmpty(CopyProgressLabel);

    // ── Multi-select token warnings ────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShareSelectedLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedTokensLabel))]
    [NotifyPropertyChangedFor(nameof(SelectAllChunksLabel))]
    [NotifyPropertyChangedFor(nameof(IsResultsAndNoSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionWarningLabel))]
    [NotifyPropertyChangedFor(nameof(SelectionWarningColor))]
    [NotifyPropertyChangedFor(nameof(HasSelectionWarning))]
    [NotifyCanExecuteChangedFor(nameof(ShareSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeSelectedCommand))]
    private int _selectedCount;

    public bool   HasSelection       => SelectedCount > 0;
    public string ShareSelectedLabel => $"Share selected ({SelectedCount})";

    public string SelectAllChunksLabel =>
        Chunks.Count > 0 && Chunks.All(c => c.IsSelected) ? "Deselect all" : "Select all";

    public string SelectedTokensLabel
    {
        get
        {
            var total = Chunks.Where(c => c.IsSelected).Sum(c => c.EstimatedTokens);
            return total == 0 ? string.Empty : $"~{total:N0} tokens selected";
        }
    }

    public string SelectionWarningLabel
    {
        get
        {
            var total = Chunks.Where(c => c.IsSelected).Sum(c => c.EstimatedTokens);
            return total switch
            {
                > 200_000 => "⛔ Exceeds Gemini 1.5 Pro share limit (~200K tokens)",
                > 128_000 => "⚠️ May exceed Claude.ai context window (128K tokens)",
                >  32_000 => "⚠️ Too large for GPT-3.5 — use GPT-4 Turbo, Claude, or Gemini",
                >  16_000 => "ℹ️ Above GPT-3.5 limit — works with GPT-4 Turbo, Claude 3+, Gemini 1.5",
                >       0 => "✅ Fits all major AI interfaces",
                _         => string.Empty,
            };
        }
    }

    public Color SelectionWarningColor
    {
        get
        {
            var total = Chunks.Where(c => c.IsSelected).Sum(c => c.EstimatedTokens);
            return total switch
            {
                > 128_000 => Color.FromArgb("#EF4444"),
                >  32_000 => Color.FromArgb("#F59E0B"),
                _         => Color.FromArgb("#00B4BC"),
            };
        }
    }

    public bool HasSelectionWarning => !string.IsNullOrEmpty(SelectionWarningLabel);
    public bool CanMerge => SelectedCount >= 2;

    // ── Saved repos ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRepo))]
    [NotifyPropertyChangedFor(nameof(HasWorkspaceSaved))]
    private SavedRepo? _selectedSavedRepo;

    partial void OnSelectedSavedRepoChanged(SavedRepo? value)
    {
        if (value is null) return;
        RepoUrl = value.Url;
        Branch  = value.Branch;
        if (value.HasToken) FireAndForget(LoadTokenForRepoAsync(value));
        FireAndForget(RestoreWorkspaceAsync(value));
    }

    public bool HasSavedRepos    => SavedRepos.Count > 0;
    public bool HasSelectedRepo  => SelectedSavedRepo is not null;
    public bool HasWorkspaceSaved => SelectedSavedRepo?.HasWorkspace ?? false;

    // ── Prompt selection ───────────────────────────────────────────────────

    public PromptItem? SelectedPrompt => Prompts.FirstOrDefault(p => p.IsSelectedForShare);

    // ── Collections ────────────────────────────────────────────────────────

    public ObservableCollection<CodeChunk>         Chunks             { get; } = [];
    public ObservableCollection<FileTypeFilter>    FileTypeFilters    { get; } = [];
    public ObservableCollection<FolderFilter>      FolderFilters      { get; } = [];
    public ObservableCollection<FilePatternFilter> FilePatternFilters { get; } = [];
    public ObservableCollection<string>            BranchOptions      { get; } = [];
    public ObservableCollection<SavedRepo>         SavedRepos         { get; } = [];
    public ObservableCollection<PromptItem>        Prompts            { get; } = [];
    public ObservableCollection<FetchedFileEntry>  FetchedFiles       { get; } = [];

    public bool HasBranchOptions => BranchOptions.Count > 0;

    // ── Constructor ────────────────────────────────────────────────────────

    public MainViewModel(
        IGitHubService         gitHubService,
        IChunkingService       chunkingService,
        IClipboardService      clipboard,
        IShareService          shareService,
        IDatabaseService       db,
        ILocalFileService      localFileService,
        ILogger<MainViewModel> logger)
    {
        _gitHubService    = gitHubService;
        _chunkingService  = chunkingService;
        _clipboard        = clipboard;
        _shareService     = shareService;
        _db               = db;
        _localFileService = localFileService;
        _logger           = logger;

        InitFileTypeFilters();
        InitFolderFilters();
        InitFilePatternFilters();

        SavedRepos.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSavedRepos));

        Chunks.CollectionChanged += (_, _) =>
        {
            // forceRebuild=true: the source collection changed, must rebuild regardless of SequenceEqual
            RefreshFilteredChunks(forceRebuild: true);
            OnPropertyChanged(nameof(CopyProgressLabel));
            OnPropertyChanged(nameof(HasCopyProgress));
        };

        FetchedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFetchedFiles));
            OnPropertyChanged(nameof(FetchedFilesCountLabel));
        };
    }

    // ── Fire-and-forget ────────────────────────────────────────────────────

    private void FireAndForget(Task task) =>
        task.ContinueWith(
            t => _logger.LogWarning(t.Exception, "Fire-and-forget task faulted."),
            TaskContinuationOptions.OnlyOnFaulted);

    // ── Init ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try { await _db.InitializeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "DB init failed."); }

        try
        {
            var token = await SecureStorage.Default.GetAsync("github_token");
            if (!string.IsNullOrWhiteSpace(token)) { AccessToken = token; TokenIsSaved = true; }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read token."); }

        await MigrateOldRecentUrlsAsync();
        await RefreshSavedReposAsync();
        await RefreshPromptsAsync();
    }

    private async Task MigrateOldRecentUrlsAsync()
    {
        try
        {
            var old = Preferences.Default.Get("recent_urls", string.Empty);
            if (string.IsNullOrWhiteSpace(old)) return;
            var existing = (await _db.GetSavedReposAsync())
                .Select(r => r.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var url in old.Split('|').Where(u => !string.IsNullOrWhiteSpace(u)).Reverse())
                if (!existing.Contains(url))
                    await _db.UpsertRepoAsync(new SavedRepo { Url = url, Branch = "main" });
            Preferences.Default.Remove("recent_urls");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "URL migration failed."); }
    }

    private async Task RefreshSavedReposAsync()
    {
        try
        {
            var repos = await _db.GetSavedReposAsync();
            SavedRepos.Clear();
            foreach (var r in repos.Take(10)) SavedRepos.Add(r);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load saved repos."); }
    }

    private async Task RefreshPromptsAsync()
    {
        try
        {
            UnsubscribeAllPrompts();
            Prompts.Clear();
            foreach (var r in await _db.GetPromptsAsync())
            {
                var item = PromptItem.FromRecord(r);
                SubscribePrompt(item);
                Prompts.Add(item);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load prompts."); }
    }

    private async Task LoadTokenForRepoAsync(SavedRepo repo)
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync($"repo_token_{repo.Id}");
            if (!string.IsNullOrWhiteSpace(token)) { AccessToken = token; TokenIsSaved = true; }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load token for repo {Id}.", repo.Id); }
    }

    // ── Workspace restore ──────────────────────────────────────────────────

    private async Task RestoreWorkspaceAsync(SavedRepo repo)
    {
        try
        {
            if (repo.SavedMaxTokens > 0)
                MaxTokensPerChunk = repo.SavedMaxTokens;

            if (!string.IsNullOrEmpty(repo.SavedEnabledExts))
            {
                var labels = JsonSerializer.Deserialize<List<string>>(repo.SavedEnabledExts) ?? [];
                if (labels.Count > 0)
                    foreach (var f in FileTypeFilters)
                        f.IsEnabled = labels.Contains(f.Label, StringComparer.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(repo.SavedExcludedFolders))
            {
                var names = JsonSerializer.Deserialize<List<string>>(repo.SavedExcludedFolders) ?? [];
                if (names.Count > 0)
                    foreach (var f in FolderFilters)
                        f.IsExcluded = names.Contains(f.Name, StringComparer.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(repo.SavedPatterns))
            {
                var patterns = JsonSerializer.Deserialize<List<string>>(repo.SavedPatterns) ?? [];
                foreach (var pattern in patterns)
                {
                    if (!FilePatternFilters.Any(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                    {
                        var pf = new FilePatternFilter { Pattern = pattern, IsEnabled = true };
                        pf.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
                        FilePatternFilters.Add(pf);
                    }
                    else
                    {
                        var existing = FilePatternFilters.First(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase));
                        existing.IsEnabled = true;
                    }
                }
            }

            if (repo.SavedPromptSortOrder >= 0)
            {
                var prompt = Prompts.FirstOrDefault(p => p.OriginalSortOrder == repo.SavedPromptSortOrder);
                if (prompt is not null)
                {
                    foreach (var p in Prompts) p.IsSelectedForShare = false;
                    prompt.IsSelectedForShare = true;
                    OnPropertyChanged(nameof(SelectedPrompt));
                    OnPropertyChanged(nameof(SelectedPromptLabel));
                    OnPropertyChanged(nameof(HasSelectedPrompt));
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not restore workspace for repo {Id}.", repo.Id); }

        await Task.CompletedTask;
    }

    // ── Filter init ────────────────────────────────────────────────────────

    private void InitFileTypeFilters()
    {
        var filters = new[]
        {
            new FileTypeFilter { Label = ".cs",      Extensions = [".cs"],               IsEnabled = true  },
            new FileTypeFilter { Label = ".xaml",    Extensions = [".xaml"],             IsEnabled = true  },
            new FileTypeFilter { Label = ".json",    Extensions = [".json"],             IsEnabled = true  },
            new FileTypeFilter { Label = ".csproj",  Extensions = [".csproj"],           IsEnabled = true  },
            new FileTypeFilter { Label = ".css",     Extensions = [".css"],              IsEnabled = false },
            new FileTypeFilter { Label = ".scss",    Extensions = [".scss", ".sass"],    IsEnabled = false },
            new FileTypeFilter { Label = ".html",    Extensions = [".html", ".htm"],     IsEnabled = false },
            new FileTypeFilter { Label = ".razor",   Extensions = [".razor", ".cshtml"], IsEnabled = false },
            new FileTypeFilter { Label = ".js",      Extensions = [".js", ".mjs"],       IsEnabled = false },
            new FileTypeFilter { Label = ".ts",      Extensions = [".ts", ".tsx"],       IsEnabled = false },
            new FileTypeFilter { Label = ".jsx",     Extensions = [".jsx"],              IsEnabled = false },
            new FileTypeFilter { Label = ".vue",     Extensions = [".vue"],              IsEnabled = false },
            new FileTypeFilter { Label = ".py",      Extensions = [".py"],               IsEnabled = false },
            new FileTypeFilter { Label = ".java",    Extensions = [".java"],             IsEnabled = false },
            new FileTypeFilter { Label = ".kt",      Extensions = [".kt"],               IsEnabled = false },
            new FileTypeFilter { Label = ".swift",   Extensions = [".swift"],            IsEnabled = false },
            new FileTypeFilter { Label = ".c/.h",    Extensions = [".c", ".h"],          IsEnabled = false },
            new FileTypeFilter { Label = ".cpp",     Extensions = [".cpp", ".hpp"],      IsEnabled = false },
            new FileTypeFilter { Label = ".go",      Extensions = [".go"],               IsEnabled = false },
            new FileTypeFilter { Label = ".rs",      Extensions = [".rs"],               IsEnabled = false },
            new FileTypeFilter { Label = ".rb",      Extensions = [".rb"],               IsEnabled = false },
            new FileTypeFilter { Label = ".php",     Extensions = [".php"],              IsEnabled = false },
            new FileTypeFilter { Label = ".xml",     Extensions = [".xml"],              IsEnabled = false },
            new FileTypeFilter { Label = ".yaml",    Extensions = [".yaml", ".yml"],     IsEnabled = false },
            new FileTypeFilter { Label = ".md",      Extensions = [".md"],               IsEnabled = false },
            new FileTypeFilter { Label = ".sql",     Extensions = [".sql"],              IsEnabled = false },
            new FileTypeFilter { Label = ".proto",   Extensions = [".proto"],            IsEnabled = false },
            new FileTypeFilter { Label = ".tf",      Extensions = [".tf"],               IsEnabled = false },
            new FileTypeFilter { Label = ".sh/.ps1", Extensions = [".sh", ".ps1"],       IsEnabled = false },
        };

        foreach (var f in filters)
        {
            PropertyChangedEventHandler h = (_, _) =>
            {
                FetchCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(FileTypeCardSummary));
            };
            f.PropertyChanged += h;
            _fileTypeHandlers.Add((f, h));
            FileTypeFilters.Add(f);
        }
    }

    private void InitFolderFilters()
    {
        foreach (var name in new[]
        {
            "bin", "obj", ".git", ".vs", "node_modules", "packages",
            "dist", "build", ".idea", "__pycache__", ".gradle", "out", ".next"
        })
        {
            var f = new FolderFilter { Name = name, IsExcluded = true };
            f.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FolderFilterSummary));
            FolderFilters.Add(f);
        }
    }

    private void InitFilePatternFilters()
    {
        var defaults = new[]
        {
            new FilePatternFilter { Pattern = "*.min.*",       IsEnabled = true  },
            new FilePatternFilter { Pattern = "*.generated.*", IsEnabled = true  },
            new FilePatternFilter { Pattern = "*.Designer.*",  IsEnabled = true  },
            new FilePatternFilter { Pattern = "*Test*",        IsEnabled = false },
            new FilePatternFilter { Pattern = "*Spec*",        IsEnabled = false },
            new FilePatternFilter { Pattern = "*_test.*",      IsEnabled = false },
            new FilePatternFilter { Pattern = "*Mock*",        IsEnabled = false },
        };
        foreach (var p in defaults)
        {
            p.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
            FilePatternFilters.Add(p);
        }
    }

    // ── Prompt subscription helpers ────────────────────────────────────────

    private void SubscribePrompt(PromptItem prompt)
    {
        PropertyChangedEventHandler h = (_, e) =>
        {
            if (e.PropertyName == nameof(PromptItem.IsSelectedForShare))
            {
                OnPropertyChanged(nameof(SelectedPrompt));
                OnPropertyChanged(nameof(SelectedPromptLabel));
                OnPropertyChanged(nameof(HasSelectedPrompt));
            }
        };
        prompt.PropertyChanged += h;
        _promptHandlers.Add((prompt, h));
    }

    private void UnsubscribePrompt(PromptItem prompt)
    {
        var entry = _promptHandlers.FirstOrDefault(x => x.Prompt == prompt);
        if (entry.Prompt is not null)
        {
            prompt.PropertyChanged -= entry.Handler;
            _promptHandlers.Remove(entry);
        }
    }

    private void UnsubscribeAllPrompts()
    {
        foreach (var (p, h) in _promptHandlers) p.PropertyChanged -= h;
        _promptHandlers.Clear();
    }

    // ── Chunk subscription helpers ─────────────────────────────────────────

    private void SubscribeChunk(CodeChunk chunk)
    {
        PropertyChangedEventHandler h = (_, e) =>
        {
            if (e.PropertyName == nameof(CodeChunk.IsSelected))
            {
                SelectedCount = Chunks.Count(c => c.IsSelected);
                OnPropertyChanged(nameof(SelectAllChunksLabel));
                OnPropertyChanged(nameof(SelectedTokensLabel));
                OnPropertyChanged(nameof(CanMerge));
            }
        };
        chunk.PropertyChanged += h;
        _chunkHandlers.Add((chunk, h));
    }

    private void UnsubscribeAllChunks()
    {
        foreach (var (c, h) in _chunkHandlers) c.PropertyChanged -= h;
        _chunkHandlers.Clear();
    }

    private void UnsubscribeChunk(CodeChunk chunk)
    {
        var entry = _chunkHandlers.FirstOrDefault(x => x.Chunk == chunk);
        if (entry.Chunk is not null)
        {
            chunk.PropertyChanged -= entry.Handler;
            _chunkHandlers.Remove(entry);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string WithPrompt(string content)
    {
        var prompt = SelectedPrompt;
        if (prompt is null) return content;
        var text = prompt.Content;
        return text.Contains("[PASTE CHUNK]")
            ? text.Replace("[PASTE CHUNK]", content)
            : $"{text}\n\n{content}";
    }

    private static bool IsLocalPath(string path) =>
        !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        (path.StartsWith('/') || path.StartsWith('~') || (path.Length >= 2 && path[1] == ':'));

    private static string BuildChunkContent(CodeChunk chunk)
    {
        if (chunk.FileEntries.Count == 0 || !chunk.FileEntries.Any(f => f.IsExcluded))
            return chunk.Content;
        var sb = new StringBuilder();
        foreach (var file in chunk.FileEntries.Where(f => !f.IsExcluded))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.AppendLine($"==== {file.Path} ====");
            sb.Append(file.Content.TrimEnd());
        }
        return sb.ToString();
    }

    private IEnumerable<string> GetSelectedExtensions() =>
        FileTypeFilters.Where(f => f.IsEnabled).SelectMany(f => f.Extensions);

    private IEnumerable<string> GetExcludedFolders() =>
        FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name);

    private IEnumerable<string> GetExcludedFilePatterns() =>
        FilePatternFilters.Where(f => f.IsEnabled).Select(f => f.Pattern);

    private async Task SaveCurrentRepoAsync(string url, string branch)
    {
        try
        {
            var repos    = await _db.GetSavedReposAsync();
            var existing = repos.FirstOrDefault(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { existing.Branch = branch; await _db.UpsertRepoAsync(existing); }
            else await _db.UpsertRepoAsync(new SavedRepo { Url = url, Branch = branch });
            await RefreshSavedReposAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save repo."); }
    }

    // ── Populate FetchedFiles from raw file list ───────────────────────────

    private void PopulateFetchedFiles(List<(string Path, string Content)> files)
    {
        FetchedFiles.Clear();
        foreach (var (path, _) in files)
        {
            var normalised = path.Replace('\\', '/');
            var lastSlash  = normalised.LastIndexOf('/');
            var folder     = lastSlash > 0 ? normalised[..lastSlash] : "Root";
            var fileName   = lastSlash > 0 ? normalised[(lastSlash + 1)..] : normalised;

            var entry = new FetchedFileEntry
            {
                Path     = normalised,
                FileName = fileName,
                Folder   = folder,
            };
            entry.IsExcluded = FilePatternFilters.Any(f =>
                f.IsEnabled &&
                fileName.Equals(f.Pattern, StringComparison.OrdinalIgnoreCase));

            entry.PropertyChanged += OnFetchedFileEntryChanged;
            FetchedFiles.Add(entry);
        }
    }

    private void OnFetchedFileEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FetchedFileEntry.IsExcluded)) return;
        if (sender is not FetchedFileEntry entry) return;

        if (entry.IsExcluded)
        {
            if (!FilePatternFilters.Any(f => f.Pattern.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                var pf = new FilePatternFilter { Pattern = entry.FileName, IsEnabled = true, IsAutoAdded = true };
                pf.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
                FilePatternFilters.Add(pf);
                OnPropertyChanged(nameof(FilePatternSummary));
            }
        }
        else
        {
            var autoPattern = FilePatternFilters.FirstOrDefault(f =>
                f.IsAutoAdded &&
                f.Pattern.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase));
            if (autoPattern is not null)
            {
                FilePatternFilters.Remove(autoPattern);
                OnPropertyChanged(nameof(FilePatternSummary));
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  COMMANDS
    // ══════════════════════════════════════════════════════════════════════

    // ── Navigation ────────────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateToPromptsPage() =>
        NavigateToPromptsPageRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void GoBack() =>
        GoBackRequested?.Invoke(this, EventArgs.Empty);

    // ── Fetch ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        if (IsBusy) return;
        HasError = false; ErrorText = string.Empty;
        IsBusy = true; IsResultsMode = true;
        AutoDetectStatusText = string.Empty;
        ChunkSearchText = string.Empty;

        UnsubscribeAllChunks(); Chunks.Clear(); FetchedFiles.Clear();
        ChunkCount = CopiedCount = SelectedCount = TotalFiles = TotalTokens = TotalProjects = 0;
        HasChunks = false;

        _cts?.Cancel(); _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            var extensions      = GetSelectedExtensions();
            var excludedFolders = GetExcludedFolders().ToList();
            var excludedPat     = GetExcludedFilePatterns().ToList();
            var progress        = new Progress<string>(msg => StatusText = msg);
            var inputPath       = RepoUrl.Trim().Trim('"');

            List<(string Path, string Content)> files;

            if (IsLocalPath(inputPath))
            {
                files = await _localFileService.ReadFilesAsync(
                    inputPath, extensions, excludedFolders, excludedPat, progress, _cts.Token);
            }
            else
            {
                files = await _gitHubService.FetchFilesAsync(
                    inputPath, extensions, AccessToken, Branch,
                    excludedFolders, excludedPat, progress, _cts.Token);

                if (!string.IsNullOrWhiteSpace(AccessToken))
                {
                    try { await SecureStorage.Default.SetAsync("github_token", AccessToken); TokenIsSaved = true; }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not save token."); }
                }
                await SaveCurrentRepoAsync(inputPath, Branch);
            }

            if (!string.IsNullOrWhiteSpace(KeywordFilter))
            {
                var kw = KeywordFilter.Trim();
                files = files.Where(f => f.Content.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
                if (files.Count == 0)
                    throw new InvalidOperationException(
                        $"No files contain the keyword \"{kw}\". Clear the keyword filter or try a different term.");
            }

            PopulateFetchedFiles(files);

            TotalFiles = files.Count;
            StatusText = $"Chunking {files.Count} files…";

            var token  = _cts.Token;
            var chunks = await Task.Run(
                () => _chunkingService.CreateChunks(files, (int)MaxTokensPerChunk, token), token);

            TotalTokens   = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();

            foreach (var chunk in chunks) { SubscribeChunk(chunk); Chunks.Add(chunk); }

            ChunkCount = Chunks.Count;
            HasChunks  = ChunkCount > 0;
            StatusText = $"✅  {files.Count} files · {TotalProjects} projects · {ChunkCount} chunks · ~{TotalTokens:N0} tokens";

            OnPropertyChanged(nameof(CompactSummaryText));
            OnPropertyChanged(nameof(SelectAllChunksLabel));
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
        !IsBusy && !string.IsNullOrWhiteSpace(RepoUrl) && FileTypeFilters.Any(f => f.IsEnabled);

    // ── Auto-detect (Android-safe local path scan) ─────────────────────────

    [RelayCommand]
    private async Task AutoDetectFileTypesAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || IsBusy || IsAutoDetecting) return;

        IsAutoDetecting = true;
        try
        {
            IReadOnlyDictionary<string, int> detected;
            var inputPath = RepoUrl.Trim().Trim('"');

            if (IsLocalPath(inputPath))
            {
                // BUG FIX: Directory.EnumerateFiles with SearchOption.AllDirectories
                // throws UnauthorizedAccessException on Android sandboxed directories.
                // Use a safe manual walk instead (same pattern as LocalFileService).
                var extCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(inputPath))
                {
                    var queue = new Queue<string>(new[] { inputPath });
                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();

                        IEnumerable<string> files;
                        try   { files = Directory.EnumerateFiles(current); }
                        catch { continue; }

                        foreach (var file in files)
                        {
                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            if (!string.IsNullOrEmpty(ext))
                                extCounts[ext] = extCounts.GetValueOrDefault(ext, 0) + 1;
                        }

                        IEnumerable<string> subdirs;
                        try   { subdirs = Directory.EnumerateDirectories(current); }
                        catch { continue; }

                        foreach (var sub in subdirs) queue.Enqueue(sub);
                    }
                }
                detected = extCounts;
            }
            else
            {
                detected = await _gitHubService.DetectFileTypesInRepoAsync(
                    inputPath, AccessToken, Branch);
            }

            if (detected.Count == 0)
            {
                AutoDetectStatusText = "⚠️ No file types detected.";
                return;
            }

            foreach (var f in FileTypeFilters)
                f.IsEnabled = f.Extensions.Any(e => detected.ContainsKey(e));

            var inferredLabels = new List<string>();
            foreach (var (detectedExt, _) in detected)
            {
                foreach (var (_, companionExts) in LanguageExtensionInference)
                {
                    foreach (var companionExt in companionExts)
                    {
                        var companionFilter = FileTypeFilters.FirstOrDefault(f =>
                            f.Extensions.Any(e => e.Equals(companionExt, StringComparison.OrdinalIgnoreCase)));
                        if (companionFilter is not null && !companionFilter.IsEnabled)
                        {
                            companionFilter.IsEnabled = true;
                            inferredLabels.Add(companionFilter.Label);
                        }
                    }
                }
            }

            if (detected.Any(kv => kv.Key.Equals(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var csproj = FileTypeFilters.FirstOrDefault(f => f.Label == ".csproj");
                if (csproj is not null && !csproj.IsEnabled)
                {
                    csproj.IsEnabled = true;
                    if (!inferredLabels.Contains(".csproj")) inferredLabels.Add(".csproj");
                }
            }

            var enabledCount = FileTypeFilters.Count(f => f.IsEnabled);
            var inferredPart = inferredLabels.Count > 0
                ? $"  ·  Inferred: {string.Join(", ", inferredLabels.Distinct())}"
                : string.Empty;

            AutoDetectStatusText = $"✅ Detected {detected.Count} type{(detected.Count == 1 ? "" : "s")}{inferredPart}  ·  {enabledCount} filters enabled";
        }
        catch (Exception ex)
        {
            AutoDetectStatusText = $"⚠️ Detection failed: {ex.Message}";
        }
        finally { IsAutoDetecting = false; }
    }

    // ── Branch picker ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ShowBranchPickerAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || !RepoUrl.Contains("github.com"))
        {
            StatusText = "⚠️ Enter a valid GitHub URL before loading branches."; return;
        }
        IsFetchingBranches = true;
        try
        {
            var branches = await _gitHubService.FetchBranchesAsync(RepoUrl.Trim(), AccessToken);
            if (branches.Count == 0) { StatusText = "⚠️ No branches found. A token may be required."; return; }
            BranchOptions.Clear();
            foreach (var b in branches) BranchOptions.Add(b);
            OnPropertyChanged(nameof(HasBranchOptions));
            BranchPickerRequested?.Invoke(this, branches);
        }
        catch (Exception ex) { StatusText = $"⚠️ Could not fetch branches: {ex.Message}"; _logger.LogWarning(ex, "Branch fetch failed."); }
        finally { IsFetchingBranches = false; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void BackToConfiguration() { ClearSelection(); IsResultsMode = false; }

    // ── Chip toggles ───────────────────────────────────────────────────────

    [RelayCommand] private static void ToggleFileType(FileTypeFilter f)       { if (f is not null) f.IsEnabled  = !f.IsEnabled;  }
    [RelayCommand] private static void ToggleFolder(FolderFilter f)           { if (f is not null) f.IsExcluded = !f.IsExcluded; }
    [RelayCommand] private static void ToggleFilePattern(FilePatternFilter p) { if (p is not null) p.IsEnabled  = !p.IsEnabled;  }

    [RelayCommand] private void ToggleAllFileTypes()     { bool a = FileTypeFilters.Any(f => f.IsEnabled);    foreach (var f in FileTypeFilters)    f.IsEnabled  = !a; }
    [RelayCommand] private void ToggleAllFolderFilters() { bool a = FolderFilters.Any(f => f.IsExcluded);     foreach (var f in FolderFilters)       f.IsExcluded = !a; }
    [RelayCommand] private void ToggleAllFilePatterns()  { bool a = FilePatternFilters.Any(f => f.IsEnabled); foreach (var f in FilePatternFilters)  f.IsEnabled  = !a; }

    [RelayCommand]
    private void AddCustomFolder()
    {
        var name = CustomFolderInput.Trim();
        if (string.IsNullOrWhiteSpace(name) || FolderFilters.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        var folder = new FolderFilter { Name = name, IsExcluded = true };
        folder.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FolderFilterSummary));
        FolderFilters.Add(folder);
        CustomFolderInput = string.Empty;
        OnPropertyChanged(nameof(FolderFilterSummary));
    }

    [RelayCommand]
    private void AddCustomPattern()
    {
        var raw = CustomPatternInput.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        var tokens = raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in tokens)
        {
            if (FilePatternFilters.Any(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))) continue;
            var p = new FilePatternFilter { Pattern = pattern, IsEnabled = true };
            p.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
            FilePatternFilters.Add(p);
        }
        CustomPatternInput = string.Empty;
        OnPropertyChanged(nameof(FilePatternSummary));
    }

    // ── File browser commands ──────────────────────────────────────────────

    [RelayCommand] private void ToggleFetchedFiles() => IsFetchedFilesExpanded = !IsFetchedFilesExpanded;

    [RelayCommand]
    private void ToggleFetchedFileExclusion(FetchedFileEntry entry)
    {
        if (entry is not null) entry.IsExcluded = !entry.IsExcluded;
    }

    // ── Chunk file include/exclude all ────────────────────────────────────

    /// <summary>Re-include all files in the specified chunk (clears all per-file exclusions).</summary>
    [RelayCommand]
    private void IncludeAllChunkFiles(CodeChunk chunk)
    {
        if (chunk is null) return;
        foreach (var f in chunk.FileEntries) f.IsExcluded = false;
    }

    /// <summary>Exclude all files in the specified chunk so they are skipped on copy.</summary>
    [RelayCommand]
    private void ExcludeAllChunkFiles(CodeChunk chunk)
    {
        if (chunk is null) return;
        foreach (var f in chunk.FileEntries) f.IsExcluded = true;
    }

    // ── Chunk copy / share ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task CopyChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            var payload = WithPrompt(BuildChunkContent(chunk));
            await _clipboard.SetTextAsync(payload);
            foreach (var c in Chunks) c.IsCopied = false;
            chunk.IsCopied = true;
            CopiedCount = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex) { StatusText = $"⚠️ Could not copy: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task CopyNextChunkAsync()
    {
        if (!HasChunks) return;
        var next = Chunks.OrderBy(c => c.Index).FirstOrDefault(c => !c.IsCopied)
                   ?? Chunks.OrderBy(c => c.Index).First();
        await CopyChunkAsync(next);
    }

    [RelayCommand]
    private async Task ShareChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try { await _shareService.ShareTextAsync(WithPrompt(chunk.Content), $"Chunk {chunk.Index + 1} · {chunk.ProjectName}"); }
        catch (Exception ex) { StatusText = $"⚠️ Could not open share sheet: {ex.Message}"; }
    }

    [RelayCommand] private void TogglePreview(CodeChunk chunk) { if (chunk is not null) chunk.IsPreviewExpanded = !chunk.IsPreviewExpanded; }
    [RelayCommand] private static void ToggleChunkSelection(CodeChunk chunk) { if (chunk is not null) chunk.IsSelected = !chunk.IsSelected; }
    [RelayCommand] private static void ToggleFileEntryExclusion(ChunkFile file) { if (file is not null) file.IsExcluded = !file.IsExcluded; }
    [RelayCommand] private static void ToggleFileCode(ChunkFile file) { if (file is not null) file.IsCodeExpanded = !file.IsCodeExpanded; }

    [RelayCommand]
    private void SelectAllChunks()
    {
        bool allSelected = Chunks.Count > 0 && Chunks.All(c => c.IsSelected);
        foreach (var c in Chunks) c.IsSelected = !allSelected;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ShareSelectedAsync()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var c in selected) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(BuildChunkContent(c)); }
        var title = selected.Count == 1
            ? $"Chunk {selected[0].Index + 1} · {selected[0].ProjectName}"
            : $"{selected.Count} chunks · " + string.Join(", ", selected.Select(c => c.ProjectName).Distinct());
        try { await _shareService.ShareTextAsync(WithPrompt(sb.ToString()), title); }
        catch (Exception ex) { StatusText = $"⚠️ Could not open share sheet: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopySelectedAsync()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var c in selected) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(BuildChunkContent(c)); }
        try
        {
            await _clipboard.SetTextAsync(WithPrompt(sb.ToString()));
            foreach (var c in Chunks) c.IsCopied = false;
            foreach (var c in selected) c.IsCopied = true;
            CopiedCount = Chunks.Count(c => c.IsCopied);
            StatusText = $"✅ {selected.Count} chunk(s) copied to clipboard.";
        }
        catch (Exception ex) { StatusText = $"⚠️ Could not copy: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelection() { foreach (var c in Chunks) c.IsSelected = false; }

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void MergeSelected()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count < 2) return;

        var sb = new StringBuilder();
        var mergedFiles = new List<ChunkFile>();
        foreach (var c in selected) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(c.Content); mergedFiles.AddRange(c.FileEntries); }

        var mergedContent = sb.ToString();
        var merged = new CodeChunk
        {
            Index           = selected[0].Index,
            ProjectName     = selected.Select(c => c.ProjectName).Distinct().Count() == 1 ? selected[0].ProjectName : "Merged",
            Content         = mergedContent,
            EstimatedTokens = _chunkingService.EstimateTokens(mergedContent),
            FileEntries     = mergedFiles,
        };

        int insertAt = Chunks.IndexOf(selected[0]);
        foreach (var c in selected.OrderByDescending(c => Chunks.IndexOf(c))) { UnsubscribeChunk(c); Chunks.Remove(c); }
        if (insertAt >= 0 && insertAt <= Chunks.Count) Chunks.Insert(insertAt, merged);
        else Chunks.Add(merged);
        SubscribeChunk(merged);

        for (int i = 0; i < Chunks.Count; i++)
        {
            Chunks[i].Index = i;
            Chunks[i].NotifyIndexChanged();
        }

        ChunkCount = Chunks.Count; TotalTokens = Chunks.Sum(c => c.EstimatedTokens);
        SelectedCount = 0;
        RefreshFilteredChunks(forceRebuild: true);
        OnPropertyChanged(nameof(CompactSummaryText));
        OnPropertyChanged(nameof(SelectAllChunksLabel));
    }

    // ── Prompts ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CopyPromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        try { await _clipboard.SetTextAsync(prompt.Content); foreach (var p in Prompts) p.IsCopied = false; prompt.IsCopied = true; }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not copy prompt."); }
    }

    [RelayCommand]
    private void SelectPromptForShare(PromptItem prompt)
    {
        if (prompt is null) return;
        var was = prompt.IsSelectedForShare;
        foreach (var p in Prompts) p.IsSelectedForShare = false;
        prompt.IsSelectedForShare = !was;
        OnPropertyChanged(nameof(SelectedPrompt));
        OnPropertyChanged(nameof(SelectedPromptLabel));
        OnPropertyChanged(nameof(HasSelectedPrompt));
    }

    [RelayCommand]
    private void TogglePromptPreview(PromptItem prompt)
    {
        if (prompt is null) return;
        bool willExpand = !prompt.IsPreviewExpanded;
        foreach (var p in Prompts) p.IsPreviewExpanded = false;
        if (willExpand) { prompt.IsEditing = false; prompt.IsPreviewExpanded = true; }
    }

    [RelayCommand]
    private void StartEditPrompt(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.IsPreviewExpanded = false;
        prompt.EditTitle = prompt.Title; prompt.EditContent = prompt.Content; prompt.IsEditing = true;
    }

    [RelayCommand]
    private async Task SavePromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        var title = prompt.EditTitle.Trim();
        if (string.IsNullOrEmpty(title)) title = "Untitled Prompt";
        prompt.Title = title; prompt.Content = prompt.EditContent.Trim(); prompt.IsEditing = false;
        try
        {
            var r = await _db.UpsertPromptAsync(prompt.ToRecord(Prompts.IndexOf(prompt)));
            prompt.Id = r.Id;
            OnPropertyChanged(nameof(SelectedPrompt));
            OnPropertyChanged(nameof(SelectedPromptLabel));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save prompt."); }
    }

    [RelayCommand]
    private void CancelEditPrompt(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.IsEditing = false;
        if (prompt.Id == 0) { UnsubscribePrompt(prompt); Prompts.Remove(prompt); }
    }

    [RelayCommand]
    private async Task DeletePromptAsync(PromptItem prompt)
    {
        if (prompt is null || prompt.IsBuiltIn) return;
        try { await _db.DeletePromptAsync(prompt.Id); UnsubscribePrompt(prompt); Prompts.Remove(prompt); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete prompt."); }
    }

    [RelayCommand]
    private void AddNewPrompt()
    {
        foreach (var p in Prompts) { p.IsPreviewExpanded = false; p.IsEditing = false; }
        var item = new PromptItem { IsEditing = true, EditTitle = "New Prompt", EditContent = "" };
        SubscribePrompt(item); Prompts.Add(item);
    }

    [RelayCommand]
    private async Task ResetPromptsAsync()
    {
        try { await _db.ResetPromptsToDefaultAsync(); await RefreshPromptsAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not reset prompts."); }
    }

    [RelayCommand]
    private async Task ResetSinglePromptAsync(PromptItem prompt)
    {
        if (prompt is null || !prompt.IsBuiltIn) return;
        if (!BuiltInPrompts.BySortOrder.TryGetValue(prompt.OriginalSortOrder, out var seed))
        { StatusText = "⚠️ Could not find factory default for this prompt."; return; }
        prompt.Title = seed.Title; prompt.Content = seed.Content; prompt.IsEditing = false;
        try { await _db.UpsertPromptAsync(prompt.ToRecord(Prompts.IndexOf(prompt))); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not reset single prompt."); }
    }

    // ── URL / repo management ──────────────────────────────────────────────

    [RelayCommand] private void ClearUrl() => RepoUrl = string.Empty;

    [RelayCommand]
    private async Task PasteUrlAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) RepoUrl = text.Trim().Trim('"');
    }

    [RelayCommand] private void SelectRepo(SavedRepo repo) => SelectedSavedRepo = repo;

    [RelayCommand]
    private async Task DeleteRepoAsync(SavedRepo repo)
    {
        if (repo is null) return;
        try
        {
            await _db.DeleteRepoAsync(repo.Id);
            try { SecureStorage.Default.Remove($"repo_token_{repo.Id}"); } catch { }
            if (SelectedSavedRepo?.Id == repo.Id) SelectedSavedRepo = null;
            await RefreshSavedReposAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete repo {Id}.", repo.Id); }
    }

    [RelayCommand] private void ClearSelectedRepo() => SelectedSavedRepo = null;

    [RelayCommand]
    private void RenameSelectedRepo()
    {
        if (SelectedSavedRepo is not null) RepoRenameRequested?.Invoke(this, SelectedSavedRepo);
    }

    public async Task SetRepoNameAsync(SavedRepo repo, string name)
    {
        repo.Name = name; await _db.UpsertRepoAsync(repo); await RefreshSavedReposAsync();
    }

    // ── Save workspace ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveWorkspaceAsync()
    {
        if (SelectedSavedRepo is null) return;
        try
        {
            var repo = SelectedSavedRepo;
            repo.SavedMaxTokens       = (int)MaxTokensPerChunk;
            repo.SavedEnabledExts     = JsonSerializer.Serialize(
                FileTypeFilters.Where(f => f.IsEnabled).Select(f => f.Label).ToList());
            repo.SavedExcludedFolders = JsonSerializer.Serialize(
                FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name).ToList());
            repo.SavedPatterns        = JsonSerializer.Serialize(
                FilePatternFilters.Where(f => f.IsEnabled && !f.IsAutoAdded).Select(f => f.Pattern).ToList());
            repo.SavedPromptSortOrder = SelectedPrompt?.OriginalSortOrder ?? -1;

            await _db.UpdateRepoWorkspaceAsync(repo);
            OnPropertyChanged(nameof(HasWorkspaceSaved));

            AutoDetectStatusText = "✓ Workspace saved";
            await Task.Delay(2000);
            if (AutoDetectStatusText == "✓ Workspace saved") AutoDetectStatusText = string.Empty;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save workspace."); }
    }

    // ── Misc commands ──────────────────────────────────────────────────────

    [RelayCommand] private void ShowTokenInfo() => TokenInfoRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ShowInfo(string message) => ShowInfoRequested?.Invoke(this, message ?? string.Empty);

    [RelayCommand]
    private void ClearToken()
    {
        try { SecureStorage.Default.Remove("github_token"); } catch (Exception ex) { _logger.LogWarning(ex, "Remove token failed."); }
        AccessToken = string.Empty; TokenIsSaved = false;
    }

    [RelayCommand]
    private void SetPresetTokens(string value) { if (int.TryParse(value, out var t)) MaxTokensPerChunk = t; }

    [RelayCommand] private void ToggleKeyword()      => IsKeywordExpanded      = !IsKeywordExpanded;
    [RelayCommand] private void ClearKeywordFilter() => KeywordFilter          = string.Empty;
    [RelayCommand] private void ToggleFileTypes()    => IsFileTypesExpanded    = !IsFileTypesExpanded;
    [RelayCommand] private void ToggleFolders()      => IsFoldersExpanded      = !IsFoldersExpanded;
    [RelayCommand] private void ToggleFilePatterns() => IsFilePatternsExpanded = !IsFilePatternsExpanded;
    [RelayCommand] private void ShowHistory()        => ShowHistoryRequested?.Invoke(this, SavedRepos.ToList());
    [RelayCommand] private void ClearChunkSearch()   => ChunkSearchText        = string.Empty;

    // ── Reset ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Reset()
    {
        ClearSelection();
        UnsubscribeAllChunks(); Chunks.Clear(); FetchedFiles.Clear();
        ChunkCount = CopiedCount = SelectedCount = TotalFiles = TotalTokens = TotalProjects = 0;
        HasChunks = HasError = false; ErrorText = string.Empty;
        IsResultsMode = false; AutoDetectStatusText = string.Empty; ChunkSearchText = string.Empty;
        StatusText = "Enter a GitHub URL or local path, then press Chunk.";
        OnPropertyChanged(nameof(FileTypeCardSummary));
        OnPropertyChanged(nameof(CompactSummaryText));
        OnPropertyChanged(nameof(CopyProgressLabel));
        OnPropertyChanged(nameof(HasCopyProgress));
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel(); _cts?.Dispose(); _cts = null;
        _autoDetectDebounce?.Cancel(); _autoDetectDebounce?.Dispose(); _autoDetectDebounce = null;
        foreach (var (f, h) in _fileTypeHandlers) f.PropertyChanged -= h;
        _fileTypeHandlers.Clear();
        UnsubscribeAllChunks();
        UnsubscribeAllPrompts();
    }
}
