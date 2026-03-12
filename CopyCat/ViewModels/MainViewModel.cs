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
    private readonly IDatabaseService       _db;
    private readonly ILocalFileService      _localFileService;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _cts;
    private bool                     _initialized;
    private bool                     _disposed;

    // ── Event-handler lists for explicit unsubscription ────────────────────

    private readonly List<(FileTypeFilter Filter, PropertyChangedEventHandler Handler)> _fileTypeHandlers = [];
    private readonly List<(CodeChunk      Chunk,  PropertyChangedEventHandler Handler)> _chunkHandlers    = [];
    private readonly List<(PromptItem     Prompt, PropertyChangedEventHandler Handler)> _promptHandlers   = [];

    // ── Events for code-behind dialogs ─────────────────────────────────────

    public event EventHandler<List<string>>?           BranchPickerRequested;
    public event EventHandler?                         TokenInfoRequested;
    public event EventHandler<SavedRepo>?              RepoRenameRequested;
    public event EventHandler<List<Models.SavedRepo>>? ShowHistoryRequested;

    // ── Observable properties ──────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _repoUrl = string.Empty;

    partial void OnRepoUrlChanged(string _)
    {
        BranchOptions.Clear();
        OnPropertyChanged(nameof(HasBranchOptions));
        OnPropertyChanged(nameof(CanAutoDetect));
        // Clear stale auto-detect result — setting the text is sufficient;
        // HasAutoDetectStatus is a computed property and must NOT be assigned.
        AutoDetectStatusText = string.Empty;
    }

    [ObservableProperty] private string _accessToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchButtonLabel))]
    [NotifyPropertyChangedFor(nameof(CompactSummaryText))]
    private string _branch = "main";

    [ObservableProperty] private bool _tokenIsSaved;

    [ObservableProperty] private bool _isFileTypesExpanded;
    [ObservableProperty] private bool _isFoldersExpanded;
    [ObservableProperty] private bool _isFilePatternsExpanded;
    [ObservableProperty] private bool _isPromptsExpanded;
    [ObservableProperty] private bool _isKeywordExpanded;
    [ObservableProperty] private bool _isFetchingBranches;

    [ObservableProperty] private string _keywordFilter      = string.Empty;
    [ObservableProperty] private string _customFolderInput  = string.Empty;
    [ObservableProperty] private string _customPatternInput = string.Empty;

    // ── Auto-detect state ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isAutoDetecting;

    /// <summary>
    /// Human-readable result of the last auto-detect run, e.g.
    /// "✅ Detected: .cs, .xaml, .json  (12 types enabled)"
    /// Cleared when the URL changes or a new chunk run begins.
    ///
    /// NOTE: HasAutoDetectStatus is derived from this value via a computed
    /// property. Do NOT assign HasAutoDetectStatus directly — it is read-only.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoDetectStatus))]
    private string _autoDetectStatusText = string.Empty;

    /// <summary>
    /// True when AutoDetectStatusText is non-empty.
    /// This is a READ-ONLY computed property — never assign it directly.
    /// </summary>
    public bool HasAutoDetectStatus => !string.IsNullOrEmpty(AutoDetectStatusText);

    /// <summary>
    /// True only when the URL field contains a github.com address or a
    /// valid local path — i.e. when auto-detection makes sense.
    /// Controls visibility of the 🔍 Auto button.
    /// </summary>
    public bool CanAutoDetect =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(RepoUrl);

    // ── File-type card summary ─────────────────────────────────────────────

    /// <summary>
    /// Single-line summary shown in the FILE TYPES card header when collapsed.
    /// Examples:
    ///   ".cs  .xaml  .json  .csproj"
    ///   ".cs  .xaml  .json  +3 more"
    ///   ".cs  .xaml  .json  ·  47 files"
    /// </summary>
    public string FileTypeCardSummary
    {
        get
        {
            var enabled = FileTypeFilters.Where(f => f.IsEnabled).Select(f => f.Label).ToList();
            if (enabled.Count == 0) return "no types selected";

            const int maxVisible = 4;
            var labels = enabled.Count <= maxVisible
                ? string.Join("  ", enabled)
                : string.Join("  ", enabled.Take(maxVisible)) +
                  $"  +{enabled.Count - maxVisible} more";

            return TotalFiles > 0
                ? $"{labels}  ·  {TotalFiles:N0} files"
                : labels;
        }
    }

    // ── App state ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationMode))]
    private bool _isResultsMode;

    public bool IsConfigurationMode => !IsResultsMode;

    public string CompactSummaryText
    {
        get
        {
            if (!IsResultsMode) return string.Empty;
            var name = string.IsNullOrWhiteSpace(RepoUrl) ? "—" : ShortRepoName;
            var br   = string.IsNullOrWhiteSpace(Branch) ? "—" : Branch;
            return $"{name}  •  {br}  •  ~{TotalTokens:N0} tokens";
        }
    }

    private string ShortRepoName =>
        RepoUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? RepoUrl.Replace("https://github.com/", "").TrimEnd('/')
            : System.IO.Path.GetFileName(RepoUrl.TrimEnd('/', '\\'));

    // ── Branch button label ────────────────────────────────────────────────

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
        < 2000  => "⚠️ Very few tokens — many chunks will be created. Use multi-select to combine before sharing.",
        < 4097  => "ℹ️ Fits GPT-3.5 (4K context) and most basic AI chat interfaces.",
        < 8193  => "✅ Good balance — works with GPT-4, Claude and Gemini.",
        < 16001 => "ℹ️ Above GPT-3.5 limit. Compatible with GPT-4 Turbo, Claude 3+ and Gemini 1.5.",
        < 25001 => "⚠️ Large chunks — some AI chat interfaces may reject text this size. Prefer share sheet (↗).",
        _       => "🚨 Very large chunks — use the share sheet (↗) instead of clipboard for best results."
    };

    public Color SliderWarningColor => (int)MaxTokensPerChunk switch
    {
        < 2000             => Color.FromArgb("#FF7070"),
        >= 4097 and < 8193 => Color.FromArgb("#22C55E"),
        >= 25001           => Color.FromArgb("#FF7070"),
        _                  => Color.FromArgb("#A0A0C0"),
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

    partial void OnTotalFilesChanged(int _) => OnPropertyChanged(nameof(FileTypeCardSummary));

    // ── Multi-select ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShareSelectedLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedTokensLabel))]
    [NotifyPropertyChangedFor(nameof(SelectAllChunksLabel))]
    [NotifyCanExecuteChangedFor(nameof(ShareSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
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

    // ── Saved repos ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRepo))]
    private SavedRepo? _selectedSavedRepo;

    partial void OnSelectedSavedRepoChanged(SavedRepo? value)
    {
        if (value is null) return;
        RepoUrl = value.Url;
        Branch  = value.Branch;
        if (value.HasToken) FireAndForget(LoadTokenForRepoAsync(value));
    }

    public bool HasSavedRepos   => SavedRepos.Count > 0;
    public bool HasSelectedRepo => SelectedSavedRepo is not null;

    // ── Prompt selection for share ─────────────────────────────────────────

    public PromptItem? SelectedPrompt => Prompts.FirstOrDefault(p => p.IsSelectedForShare);

    // ── Collections ────────────────────────────────────────────────────────

    public ObservableCollection<CodeChunk>         Chunks             { get; } = [];
    public ObservableCollection<FileTypeFilter>    FileTypeFilters    { get; } = [];
    public ObservableCollection<FolderFilter>      FolderFilters      { get; } = [];
    public ObservableCollection<FilePatternFilter> FilePatternFilters { get; } = [];
    public ObservableCollection<string>            BranchOptions      { get; } = [];
    public ObservableCollection<SavedRepo>         SavedRepos         { get; } = [];
    public ObservableCollection<PromptItem>        Prompts            { get; } = [];

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
    }

    // ── Safe fire-and-forget ───────────────────────────────────────────────

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
            if (!string.IsNullOrWhiteSpace(token))
            {
                AccessToken  = token;
                TokenIsSaved = true;
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read token from SecureStorage."); }

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
                .Select(r => r.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var url in old.Split('|')
                         .Where(u => !string.IsNullOrWhiteSpace(u))
                         .Reverse())
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
            if (!string.IsNullOrWhiteSpace(token))
            {
                AccessToken  = token;
                TokenIsSaved = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load token for repo {Id}.", repo.Id);
        }
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
            FolderFilters.Add(new FolderFilter { Name = name, IsExcluded = true });
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
            new FilePatternFilter { Pattern = "*.spec.*",      IsEnabled = false },
            new FilePatternFilter { Pattern = "*Reference*",   IsEnabled = false },
            new FilePatternFilter { Pattern = "*Migration*",   IsEnabled = false },
        };
        foreach (var f in defaults) FilePatternFilters.Add(f);
    }

    // ── Chunk subscriptions ────────────────────────────────────────────────

    private void SubscribeChunk(CodeChunk chunk)
    {
        PropertyChangedEventHandler h = (_, e) =>
        {
            if (e.PropertyName != nameof(CodeChunk.IsSelected)) return;
            RefreshSelectedCount();
        };
        chunk.PropertyChanged += h;
        _chunkHandlers.Add((chunk, h));
    }

    private void UnsubscribeAllChunks()
    {
        foreach (var (chunk, h) in _chunkHandlers) chunk.PropertyChanged -= h;
        _chunkHandlers.Clear();
    }

    private void RefreshSelectedCount()
    {
        SelectedCount = Chunks.Count(c => c.IsSelected);
        OnPropertyChanged(nameof(SelectedTokensLabel));
        OnPropertyChanged(nameof(SelectAllChunksLabel));
    }

    // ── Prompt subscriptions ───────────────────────────────────────────────

    private void SubscribePrompt(PromptItem prompt)
    {
        PropertyChangedEventHandler h = (sender, e) =>
        {
            if (e.PropertyName != nameof(PromptItem.IsSelectedForShare)) return;
            if (sender is not PromptItem changed) return;
            if (changed.IsSelectedForShare)
            {
                foreach (var p in Prompts)
                    if (!ReferenceEquals(p, changed))
                        p.IsSelectedForShare = false;
            }
            OnPropertyChanged(nameof(SelectedPrompt));
        };
        prompt.PropertyChanged += h;
        _promptHandlers.Add((prompt, h));
    }

    private void UnsubscribeAllPrompts()
    {
        foreach (var (prompt, h) in _promptHandlers) prompt.PropertyChanged -= h;
        _promptHandlers.Clear();
    }

    private void UnsubscribePrompt(PromptItem prompt)
    {
        var entry = _promptHandlers.FirstOrDefault(x => ReferenceEquals(x.Prompt, prompt));
        if (entry.Prompt is null) return;
        entry.Prompt.PropertyChanged -= entry.Handler;
        _promptHandlers.Remove(entry);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool CanFetch() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(RepoUrl) &&
        FileTypeFilters.Any(f => f.IsEnabled);

    private List<string>        GetSelectedExtensions()   => FileTypeFilters.Where(f => f.IsEnabled).SelectMany(f => f.Extensions).ToList();
    private IEnumerable<string> GetExcludedFolders()      => FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name);
    private IEnumerable<string> GetExcludedFilePatterns() => FilePatternFilters.Where(f => f.IsEnabled).Select(f => f.Pattern);

    private static bool IsLocalPath(string p) =>
        (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':')
        || p.StartsWith('/')
        || p.StartsWith('~')
        || p.StartsWith("\\\\");

    private string WithPrompt(string text)
    {
        var prompt = SelectedPrompt;
        return prompt is null ? text : $"{prompt.Content}\n\n{text}";
    }

    private async Task SaveCurrentRepoAsync(string url, string branch)
    {
        try
        {
            var repos    = await _db.GetSavedReposAsync();
            var existing = repos.FirstOrDefault(
                r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Branch = branch;
                if (!string.IsNullOrWhiteSpace(AccessToken)) existing.HasToken = true;
                var saved = await _db.UpsertRepoAsync(existing);
                if (!string.IsNullOrWhiteSpace(AccessToken))
                    await SecureStorage.Default.SetAsync(
                        $"repo_token_{saved.Id}", AccessToken);
            }
            else
            {
                var repo = new SavedRepo
                {
                    Url      = url,
                    Branch   = branch,
                    HasToken = !string.IsNullOrWhiteSpace(AccessToken),
                };
                repo = await _db.UpsertRepoAsync(repo);
                if (!string.IsNullOrWhiteSpace(AccessToken))
                    await SecureStorage.Default.SetAsync(
                        $"repo_token_{repo.Id}", AccessToken);
            }
            await RefreshSavedReposAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save repo."); }
    }

    // ── Auto-detect file types ─────────────────────────────────────────────

    /// <summary>
    /// Downloads the repository ZIP and counts file extensions from the ZIP
    /// entry names (no file content is read, no GitHub API call is made).
    ///
    /// Algorithm:
    ///   1. Download the repo archive for the selected branch (same URL as
    ///      a normal chunk run — does NOT count against GitHub API rate limits).
    ///   2. Read all ZipArchive.Entries names — O(n) over entry names only.
    ///   3. Count extensions, ignore binary/asset types, filter noise (< 2 files).
    ///   4. Disable all FileTypeFilters, then enable those whose extensions
    ///      appear in the detected set.
    ///   5. Display a status string with the enabled type labels.
    ///
    /// Why ZIP instead of the GitHub Languages API?
    ///   The Languages API endpoint (api.github.com/repos/.../languages) counts
    ///   against the 60 req/hour unauthenticated rate limit.  The ZIP archive
    ///   endpoint (github.com/owner/repo/archive/...) has no such limit.
    /// </summary>
    [RelayCommand]
    private async Task AutoDetectFileTypesAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || IsAutoDetecting) return;

        IsAutoDetecting      = true;
        AutoDetectStatusText = string.Empty;

        try
        {
            // ── 1. Download zip and count extensions ───────────────────────
            IReadOnlyDictionary<string, int> detectedExts;

            var inputPath = RepoUrl.Trim().Trim('"');

            if (IsLocalPath(inputPath))
            {
                // Local path: enumerate files directly, no network needed
                detectedExts = DetectExtensionsLocally(inputPath);
            }
            else
            {
                detectedExts = await _gitHubService.DetectFileTypesInRepoAsync(
                    inputPath, AccessToken, Branch.Trim(), CancellationToken.None);
            }

            if (detectedExts.Count == 0)
            {
                AutoDetectStatusText =
                    "⚠️ Inga filtyper kunde detekteras. " +
                    "Kontrollera URL:en och lägg till en GitHub-token för privata repos.";
                return;
            }

            // ── 2. Disable all, then enable matched filters ────────────────
            foreach (var f in FileTypeFilters) f.IsEnabled = false;

            int enabledCount = 0;
            foreach (var filter in FileTypeFilters)
            {
                if (filter.Extensions.Any(e => detectedExts.ContainsKey(e)))
                {
                    filter.IsEnabled = true;
                    enabledCount++;
                }
            }

            // ── 3. Build status string from enabled filter labels ──────────
            var enabledLabels = FileTypeFilters
                .Where(f => f.IsEnabled)
                .Select(f => f.Label)
                .Take(5)
                .ToList();

            var labelStr = string.Join("  ", enabledLabels)
                         + (enabledCount > 5 ? $"  +{enabledCount - 5}" : "");

            AutoDetectStatusText = enabledCount > 0
                ? $"✅ Detekterat: {labelStr}"
                : "⚠️ Inga matchande filtyper. Aktivera manuellt.";

            OnPropertyChanged(nameof(FileTypeCardSummary));
        }
        catch (OperationCanceledException) { /* user cancelled */ }
        catch (Exception ex)
        {
            AutoDetectStatusText = $"⚠️ Detektering misslyckades: {ex.Message}";
            _logger.LogWarning(ex, "AutoDetect file types failed.");
        }
        finally
        {
            IsAutoDetecting = false;
        }
    }

    /// <summary>
    /// For local paths: enumerate all files, count extensions.
    /// Mirrors the logic in <see cref="GitHubService.DetectFileTypesInRepoAsync"/>
    /// but reads the local filesystem instead of a ZIP archive.
    /// </summary>
    private static IReadOnlyDictionary<string, int> DetectExtensionsLocally(string rootPath)
    {
        try
        {
            if (!Directory.Exists(rootPath)) return new Dictionary<string, int>();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) continue;
                counts[ext] = counts.GetValueOrDefault(ext, 0) + 1;
            }

            return counts
                .Where(kv => kv.Value >= 2)
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch { return new Dictionary<string, int>(); }
    }

    // ── Token preset buttons ───────────────────────────────────────────────

    [RelayCommand]
    private void SetPresetTokens(string preset)
    {
        if (double.TryParse(preset, out var val))
            MaxTokensPerChunk = Math.Clamp(val, 1000, 32000);
    }

    // ── Select-all chunks toggle ───────────────────────────────────────────

    [RelayCommand]
    private void SelectAllChunks()
    {
        bool allSelected = Chunks.Count > 0 && Chunks.All(c => c.IsSelected);
        foreach (var c in Chunks) c.IsSelected = !allSelected;
    }

    // ── Fetch ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        _cts?.Cancel(); _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsBusy = true; HasError = false; ErrorText = string.Empty;
        HasChunks = false; CopiedCount = 0; SelectedCount = 0;
        UnsubscribeAllChunks(); Chunks.Clear(); ChunkCount = 0;
        // Clear stale auto-detect status — assign text, not the computed bool.
        AutoDetectStatusText = string.Empty;

        IsResultsMode = true;

        try
        {
            var extensions       = GetSelectedExtensions();
            var excludedFolders  = GetExcludedFolders().ToList();
            var excludedPatterns = GetExcludedFilePatterns().ToList();
            var progress         = new Progress<string>(msg => StatusText = msg);
            var inputPath        = RepoUrl.Trim().Trim('"');

            List<(string Path, string Content)> files;

            if (IsLocalPath(inputPath))
            {
                files = await _localFileService.ReadFilesAsync(
                    inputPath, extensions, excludedFolders,
                    excludedPatterns, progress, _cts.Token);
            }
            else
            {
                files = await _gitHubService.FetchFilesAsync(
                    inputPath, extensions, AccessToken, Branch,
                    excludedFolders, excludedPatterns, progress, _cts.Token);

                if (!string.IsNullOrWhiteSpace(AccessToken))
                {
                    try
                    {
                        await SecureStorage.Default.SetAsync("github_token", AccessToken);
                        TokenIsSaved = true;
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not save token."); }
                }
                await SaveCurrentRepoAsync(inputPath, Branch);
            }

            // Keyword filter
            if (!string.IsNullOrWhiteSpace(KeywordFilter))
            {
                var kw = KeywordFilter.Trim();
                files = files
                    .Where(f => f.Content.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (files.Count == 0)
                    throw new InvalidOperationException(
                        $"No files contain the keyword \"{kw}\". " +
                        "Clear the keyword filter or try a different term.");
            }

            TotalFiles = files.Count;
            StatusText = $"Chunking {files.Count} files…";

            var token  = _cts.Token;
            var chunks = await Task.Run(
                () => _chunkingService.CreateChunks(
                    files, (int)MaxTokensPerChunk, token), token);

            TotalTokens   = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();

            foreach (var chunk in chunks) { SubscribeChunk(chunk); Chunks.Add(chunk); }

            ChunkCount = Chunks.Count;
            HasChunks  = ChunkCount > 0;
            StatusText =
                $"✅  {files.Count} files · {TotalProjects} projects · " +
                $"{ChunkCount} chunks · ~{TotalTokens:N0} tokens";

            OnPropertyChanged(nameof(CompactSummaryText));
            OnPropertyChanged(nameof(SelectAllChunksLabel));
        }
        catch (OperationCanceledException)
        {
            StatusText    = "Cancelled.";
            IsResultsMode = false;
        }
        catch (Exception ex)
        {
            HasError      = true;
            ErrorText     = ex.Message;
            StatusText    = "Error — see message below.";
            IsResultsMode = false;
            _logger.LogError(ex, "Fetch error.");
        }
        finally { IsBusy = false; }
    }

    // ── Branch picker ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ShowBranchPickerAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) ||
            !RepoUrl.Contains("github.com"))
        {
            StatusText = "⚠️ Enter a valid GitHub URL before loading branches.";
            return;
        }

        IsFetchingBranches = true;
        try
        {
            var branches = await _gitHubService.FetchBranchesAsync(
                RepoUrl.Trim(), AccessToken);
            if (branches.Count == 0)
            {
                StatusText =
                    "⚠️ No branches found. " +
                    "A token may be required for branch listing.";
                return;
            }
            BranchOptions.Clear();
            foreach (var b in branches) BranchOptions.Add(b);
            OnPropertyChanged(nameof(HasBranchOptions));
            BranchPickerRequested?.Invoke(this, branches);
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Could not fetch branches: {ex.Message}";
            _logger.LogWarning(ex, "Branch fetch failed.");
        }
        finally { IsFetchingBranches = false; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    // ── State management ───────────────────────────────────────────────────

    [RelayCommand]
    private void BackToConfiguration()
    {
        IsResultsMode = false;
        OnPropertyChanged(nameof(IsConfigurationMode));
    }

    // ── Chip toggle commands ───────────────────────────────────────────────

    [RelayCommand]
    private static void ToggleFileType(FileTypeFilter filter)
    {
        if (filter is not null) filter.IsEnabled = !filter.IsEnabled;
    }

    [RelayCommand]
    private static void ToggleFolder(FolderFilter folder)
    {
        if (folder is not null) folder.IsExcluded = !folder.IsExcluded;
    }

    [RelayCommand]
    private static void ToggleFilePattern(FilePatternFilter pattern)
    {
        if (pattern is not null) pattern.IsEnabled = !pattern.IsEnabled;
    }

    // ── History popup ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ShowHistory() =>
        ShowHistoryRequested?.Invoke(this, SavedRepos.ToList());

    // ── Chunk copy / share ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task CopyChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            await _clipboard.SetTextAsync(chunk.Content);
            foreach (var c in Chunks) c.IsCopied = false;
            chunk.IsCopied = true;
            CopiedCount    = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex) { StatusText = $"⚠️ Could not copy: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ShareChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            var payload = WithPrompt(chunk.Content);
            await _shareService.ShareTextAsync(
                payload, $"Chunk {chunk.Index + 1} · {chunk.ProjectName}");
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Could not open share sheet: {ex.Message}";
        }
    }

    [RelayCommand]
    private void TogglePreview(CodeChunk chunk)
    {
        if (chunk is not null) chunk.IsPreviewExpanded = !chunk.IsPreviewExpanded;
    }

    [RelayCommand]
    private static void ToggleChunkSelection(CodeChunk chunk)
    {
        if (chunk is not null) chunk.IsSelected = !chunk.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ShareSelectedAsync()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var c in selected) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(c.Content); }

        var payload = WithPrompt(sb.ToString());
        var title = selected.Count == 1
            ? $"Chunk {selected[0].Index + 1} · {selected[0].ProjectName}"
            : $"{selected.Count} chunks · " +
              string.Join(", ", selected.Select(c => c.ProjectName).Distinct());

        try { await _shareService.ShareTextAsync(payload, title); }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Could not open share sheet: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopySelectedAsync()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var c in selected) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(c.Content); }

        var payload = WithPrompt(sb.ToString());
        try
        {
            await _clipboard.SetTextAsync(payload);
            foreach (var c in Chunks) c.IsCopied = false;
            foreach (var c in selected) c.IsCopied = true;
            StatusText = $"✅ {selected.Count} chunk(s) copied to clipboard.";
        }
        catch (Exception ex) { StatusText = $"⚠️ Could not copy: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelection()
    {
        foreach (var c in Chunks) c.IsSelected = false;
    }

    // ── Prompts ────────────────────────────────────────────────────────────

    [RelayCommand] private void TogglePrompts() => IsPromptsExpanded = !IsPromptsExpanded;

    [RelayCommand]
    private async Task CopyPromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        try
        {
            await _clipboard.SetTextAsync(prompt.Content);
            foreach (var p in Prompts) p.IsCopied = false;
            prompt.IsCopied = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not copy prompt."); }
    }

    [RelayCommand]
    private void SelectPromptForShare(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.IsSelectedForShare = !prompt.IsSelectedForShare;
    }

    [RelayCommand]
    private static void TogglePromptPreview(PromptItem prompt)
    {
        if (prompt is not null) prompt.IsPreviewExpanded = !prompt.IsPreviewExpanded;
    }

    [RelayCommand]
    private void StartEditPrompt(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.EditTitle   = prompt.Title;
        prompt.EditContent = prompt.Content;
        prompt.IsEditing   = true;
    }

    [RelayCommand]
    private async Task SavePromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        var title = prompt.EditTitle.Trim();
        if (string.IsNullOrEmpty(title)) title = "Untitled Prompt";
        prompt.Title     = title;
        prompt.Content   = prompt.EditContent.Trim();
        prompt.IsEditing = false;
        try
        {
            var r = await _db.UpsertPromptAsync(
                prompt.ToRecord(Prompts.IndexOf(prompt)));
            prompt.Id = r.Id;
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
        try
        {
            await _db.DeletePromptAsync(prompt.Id);
            UnsubscribePrompt(prompt);
            Prompts.Remove(prompt);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete prompt."); }
    }

    [RelayCommand]
    private void AddNewPrompt()
    {
        var item = new PromptItem
        {
            IsEditing    = true,
            EditTitle    = "New Prompt",
            EditContent  = "",
        };
        SubscribePrompt(item);
        Prompts.Add(item);
    }

    [RelayCommand]
    private async Task ResetPromptsAsync()
    {
        try
        {
            await _db.ResetPromptsToDefaultAsync();
            await RefreshPromptsAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not reset prompts."); }
    }

    // ── URL / repo management ──────────────────────────────────────────────

    [RelayCommand] private void ClearUrl() => RepoUrl = string.Empty;

    [RelayCommand]
    private async Task PasteUrlAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
            RepoUrl = text.Trim().Trim('"');
    }

    [RelayCommand] private void SelectRepo(SavedRepo repo) => SelectedSavedRepo = repo;

    [RelayCommand]
    private async Task DeleteRepoAsync(SavedRepo repo)
    {
        if (repo is null) return;
        try
        {
            await _db.DeleteRepoAsync(repo.Id);
            try { SecureStorage.Default.Remove($"repo_token_{repo.Id}"); }
            catch { /* platform may not support */ }
            if (SelectedSavedRepo?.Id == repo.Id) SelectedSavedRepo = null;
            await RefreshSavedReposAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete repo {Id}.", repo.Id);
        }
    }

    [RelayCommand] private void ClearSelectedRepo() => SelectedSavedRepo = null;

    [RelayCommand]
    private void RenameSelectedRepo()
    {
        if (SelectedSavedRepo is not null)
            RepoRenameRequested?.Invoke(this, SelectedSavedRepo);
    }

    public async Task SetRepoNameAsync(SavedRepo repo, string name)
    {
        repo.Name = name;
        await _db.UpsertRepoAsync(repo);
        await RefreshSavedReposAsync();
    }

    // ── Token ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ShowTokenInfo() =>
        TokenInfoRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearToken()
    {
        try { SecureStorage.Default.Remove("github_token"); }
        catch (Exception ex) { _logger.LogWarning(ex, "Remove token failed."); }
        AccessToken  = string.Empty;
        TokenIsSaved = false;
    }

    // ── Keyword filter ─────────────────────────────────────────────────────

    [RelayCommand] private void ToggleKeyword()      => IsKeywordExpanded      = !IsKeywordExpanded;
    [RelayCommand] private void ClearKeywordFilter() => KeywordFilter          = string.Empty;

    // ── Section expand/collapse ────────────────────────────────────────────

    [RelayCommand] private void ToggleFileTypes()    => IsFileTypesExpanded    = !IsFileTypesExpanded;
    [RelayCommand] private void ToggleFolders()      => IsFoldersExpanded      = !IsFoldersExpanded;
    [RelayCommand] private void ToggleFilePatterns() => IsFilePatternsExpanded = !IsFilePatternsExpanded;

    // ── Toggle-all helpers ─────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleAllFileTypes()
    {
        bool any = FileTypeFilters.Any(f => f.IsEnabled);
        foreach (var f in FileTypeFilters) f.IsEnabled = !any;
    }

    [RelayCommand]
    private void ToggleAllFolderFilters()
    {
        bool any = FolderFilters.Any(f => f.IsExcluded);
        foreach (var f in FolderFilters) f.IsExcluded = !any;
    }

    [RelayCommand]
    private void ToggleAllFilePatterns()
    {
        bool any = FilePatternFilters.Any(f => f.IsEnabled);
        foreach (var f in FilePatternFilters) f.IsEnabled = !any;
    }

    // ── Custom filter additions ────────────────────────────────────────────

    [RelayCommand]
    private void AddCustomFolder()
    {
        var name = CustomFolderInput.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            FolderFilters.Any(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        FolderFilters.Add(new FolderFilter { Name = name, IsExcluded = true });
        CustomFolderInput = string.Empty;
    }

    [RelayCommand]
    private void AddCustomPattern()
    {
        var pattern = CustomPatternInput.Trim();
        if (string.IsNullOrWhiteSpace(pattern) ||
            FilePatternFilters.Any(f =>
                f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))) return;
        FilePatternFilters.Add(new FilePatternFilter
        {
            Pattern   = pattern,
            IsEnabled = true,
        });
        CustomPatternInput = string.Empty;
    }

    // ── Reset ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Reset()
    {
        UnsubscribeAllChunks(); Chunks.Clear();
        ChunkCount = CopiedCount = SelectedCount =
            TotalFiles = TotalTokens = TotalProjects = 0;
        HasChunks = HasError = false;
        ErrorText            = string.Empty;
        IsResultsMode        = false;
        // Clear auto-detect status via text, not the computed bool.
        AutoDetectStatusText = string.Empty;
        StatusText           = "Enter a GitHub URL or local path, then press Chunk.";
        OnPropertyChanged(nameof(FileTypeCardSummary));
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel(); _cts?.Dispose(); _cts = null;
        foreach (var (f, h) in _fileTypeHandlers) f.PropertyChanged -= h;
        _fileTypeHandlers.Clear();
        UnsubscribeAllChunks();
        UnsubscribeAllPrompts();
    }
}
