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

    private readonly List<(FileTypeFilter Filter, PropertyChangedEventHandler Handler)> _fileTypeHandlers = [];
    private readonly List<(CodeChunk Chunk, PropertyChangedEventHandler Handler)>       _chunkHandlers    = [];

    // ── Events for code-behind dialogs ─────────────────────────────────────
    public event EventHandler<List<string>>? BranchPickerRequested;
    public event EventHandler?               TokenInfoRequested;
    public event EventHandler<SavedRepo>?    RepoRenameRequested;

    // ── Observable properties ──────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _repoUrl = string.Empty;

    partial void OnRepoUrlChanged(string _)
    {
        BranchOptions.Clear();
        OnPropertyChanged(nameof(HasBranchOptions));
    }

    [ObservableProperty] private string _accessToken  = string.Empty;
    [ObservableProperty] private string _branch       = "developer";
    [ObservableProperty] private bool   _tokenIsSaved;

    [ObservableProperty] private bool _isFileTypesExpanded    = false;
    [ObservableProperty] private bool _isFoldersExpanded;
    [ObservableProperty] private bool _isFilePatternsExpanded;
    [ObservableProperty] private bool _isPromptsExpanded;
    [ObservableProperty] private bool _isFetchingBranches;

    [ObservableProperty] private string _keywordFilter      = string.Empty;
    [ObservableProperty] private string _customFolderInput  = string.Empty;
    [ObservableProperty] private string _customPatternInput = string.Empty;

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
        < 2000                  => Color.FromArgb("#FF7070"),
        >= 4097 and < 8193      => Color.FromArgb("#22C55E"),
        >= 25001                => Color.FromArgb("#FF7070"),
        _                       => Color.FromArgb("#A0A0C0"),
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText  = "Enter a GitHub URL or local path, then press Chunk.";
    [ObservableProperty] private string _errorText   = string.Empty;
    [ObservableProperty] private bool   _hasError;
    [ObservableProperty] private bool   _hasChunks;
    [ObservableProperty] private int    _totalFiles;
    [ObservableProperty] private int    _totalTokens;
    [ObservableProperty] private int    _totalProjects;
    [ObservableProperty] private int    _chunkCount;
    [ObservableProperty] private int    _copiedCount;

    // ── Multi-select ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShareSelectedLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedTokensLabel))]
    [NotifyCanExecuteChangedFor(nameof(ShareSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private int _selectedCount;

    public bool   HasSelection       => SelectedCount > 0;
    public string ShareSelectedLabel => $"Share selected ({SelectedCount})";

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
    private SavedRepo? _selectedSavedRepo;

    partial void OnSelectedSavedRepoChanged(SavedRepo? value)
    {
        if (value is null) return;
        RepoUrl = value.Url;
        Branch  = value.Branch;
        if (value.HasToken)
            _ = LoadTokenForRepoAsync(value);
    }

    public bool HasSavedRepos => SavedRepos.Count > 0;

    // ── Collections ────────────────────────────────────────────────────────

    public ObservableCollection<CodeChunk>        Chunks             { get; } = [];
    public ObservableCollection<FileTypeFilter>   FileTypeFilters    { get; } = [];
    public ObservableCollection<FolderFilter>     FolderFilters      { get; } = [];
    public ObservableCollection<FilePatternFilter> FilePatternFilters { get; } = [];
    public ObservableCollection<string>           BranchOptions      { get; } = [];
    public ObservableCollection<SavedRepo>        SavedRepos         { get; } = [];
    public ObservableCollection<PromptItem>       Prompts            { get; } = [];

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

    // ── Init ───────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try { await _db.InitializeAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "DB init failed."); }

        try
        {
            var token = await SecureStorage.Default.GetAsync("github_token");
            if (!string.IsNullOrWhiteSpace(token)) { AccessToken = token; TokenIsSaved = true; }
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
            Prompts.Clear();
            foreach (var r in await _db.GetPromptsAsync())
                Prompts.Add(PromptItem.FromRecord(r));
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

    // ── Filter init ────────────────────────────────────────────────────────

    private void InitFileTypeFilters()
    {
        var filters = new[]
        {
            new FileTypeFilter { Label = ".cs",       Extensions = [".cs"],               IsEnabled = true  },
            new FileTypeFilter { Label = ".xaml",     Extensions = [".xaml"],             IsEnabled = true  },
            new FileTypeFilter { Label = ".json",     Extensions = [".json"],             IsEnabled = true  },
            new FileTypeFilter { Label = ".csproj",   Extensions = [".csproj"],           IsEnabled = true  },
            new FileTypeFilter { Label = ".css",      Extensions = [".css"],              IsEnabled = false },
            new FileTypeFilter { Label = ".scss",     Extensions = [".scss", ".sass"],    IsEnabled = false },
            new FileTypeFilter { Label = ".html",     Extensions = [".html", ".htm"],     IsEnabled = false },
            new FileTypeFilter { Label = ".razor",    Extensions = [".razor", ".cshtml"], IsEnabled = false },
            new FileTypeFilter { Label = ".js",       Extensions = [".js", ".mjs"],       IsEnabled = false },
            new FileTypeFilter { Label = ".ts",       Extensions = [".ts", ".tsx"],       IsEnabled = false },
            new FileTypeFilter { Label = ".jsx",      Extensions = [".jsx"],              IsEnabled = false },
            new FileTypeFilter { Label = ".vue",      Extensions = [".vue"],              IsEnabled = false },
            new FileTypeFilter { Label = ".py",       Extensions = [".py"],               IsEnabled = false },
            new FileTypeFilter { Label = ".java",     Extensions = [".java"],             IsEnabled = false },
            new FileTypeFilter { Label = ".kt",       Extensions = [".kt"],               IsEnabled = false },
            new FileTypeFilter { Label = ".swift",    Extensions = [".swift"],            IsEnabled = false },
            new FileTypeFilter { Label = ".c/.h",     Extensions = [".c", ".h"],          IsEnabled = false },
            new FileTypeFilter { Label = ".cpp",      Extensions = [".cpp", ".hpp"],      IsEnabled = false },
            new FileTypeFilter { Label = ".go",       Extensions = [".go"],               IsEnabled = false },
            new FileTypeFilter { Label = ".rs",       Extensions = [".rs"],               IsEnabled = false },
            new FileTypeFilter { Label = ".rb",       Extensions = [".rb"],               IsEnabled = false },
            new FileTypeFilter { Label = ".php",      Extensions = [".php"],              IsEnabled = false },
            new FileTypeFilter { Label = ".xml",      Extensions = [".xml"],              IsEnabled = false },
            new FileTypeFilter { Label = ".yaml",     Extensions = [".yaml", ".yml"],     IsEnabled = false },
            new FileTypeFilter { Label = ".md",       Extensions = [".md"],               IsEnabled = false },
            new FileTypeFilter { Label = ".sql",      Extensions = [".sql"],              IsEnabled = false },
            new FileTypeFilter { Label = ".proto",    Extensions = [".proto"],            IsEnabled = false },
            new FileTypeFilter { Label = ".tf",       Extensions = [".tf"],               IsEnabled = false },
            new FileTypeFilter { Label = ".sh/.ps1",  Extensions = [".sh", ".ps1"],       IsEnabled = false },
        };

        foreach (var f in filters)
        {
            PropertyChangedEventHandler h = (_, _) => FetchCommand.NotifyCanExecuteChanged();
            f.PropertyChanged += h;
            _fileTypeHandlers.Add((f, h));
            FileTypeFilters.Add(f);
        }
    }

    private void InitFolderFilters()
    {
        foreach (var name in new[] { "bin", "obj", ".git", ".vs", "node_modules",
            "packages", "dist", "build", ".idea", "__pycache__", ".gradle", "out", ".next" })
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
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool CanFetch() =>
        !IsBusy && !string.IsNullOrWhiteSpace(RepoUrl) && FileTypeFilters.Any(f => f.IsEnabled);

    private List<string>      GetSelectedExtensions()  => FileTypeFilters.Where(f => f.IsEnabled).SelectMany(f => f.Extensions).ToList();
    private IEnumerable<string> GetExcludedFolders()   => FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name);
    private IEnumerable<string> GetExcludedFilePatterns() => FilePatternFilters.Where(f => f.IsEnabled).Select(f => f.Pattern);

    private static bool IsLocalPath(string p) =>
        (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':')
        || p.StartsWith('/') || p.StartsWith('~') || p.StartsWith("\\\\");

    private async Task SaveCurrentRepoAsync(string url, string branch)
    {
        try
        {
            var repos    = await _db.GetSavedReposAsync();
            var existing = repos.FirstOrDefault(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Branch = branch;
                if (!string.IsNullOrWhiteSpace(AccessToken)) existing.HasToken = true;
                var saved = await _db.UpsertRepoAsync(existing);
                if (!string.IsNullOrWhiteSpace(AccessToken))
                    await SecureStorage.Default.SetAsync($"repo_token_{saved.Id}", AccessToken);
            }
            else
            {
                var repo = new SavedRepo { Url = url, Branch = branch, HasToken = !string.IsNullOrWhiteSpace(AccessToken) };
                repo = await _db.UpsertRepoAsync(repo);
                if (!string.IsNullOrWhiteSpace(AccessToken))
                    await SecureStorage.Default.SetAsync($"repo_token_{repo.Id}", AccessToken);
            }

            await RefreshSavedReposAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save repo."); }
    }

    // ── Commands ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        _cts?.Cancel(); _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsBusy = true; HasError = false; ErrorText = string.Empty;
        HasChunks = false; CopiedCount = 0; SelectedCount = 0;
        UnsubscribeAllChunks(); Chunks.Clear(); ChunkCount = 0;

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
                    inputPath, extensions, excludedFolders, excludedPatterns, progress, _cts.Token);
            }
            else
            {
                files = await _gitHubService.FetchFilesAsync(
                    inputPath, extensions, AccessToken, Branch,
                    excludedFolders, excludedPatterns, progress, _cts.Token);

                if (!string.IsNullOrWhiteSpace(AccessToken))
                {
                    try { await SecureStorage.Default.SetAsync("github_token", AccessToken); TokenIsSaved = true; }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not save token."); }
                }
                await SaveCurrentRepoAsync(inputPath, Branch);
            }

            // Keyword filter
            if (!string.IsNullOrWhiteSpace(KeywordFilter))
            {
                var kw = KeywordFilter.Trim();
                files = files.Where(f => f.Content.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
                if (files.Count == 0)
                    throw new InvalidOperationException(
                        $"No files contain the keyword \"{kw}\". Clear the keyword filter or try a different term.");
            }

            TotalFiles = files.Count;
            StatusText = $"Chunking {files.Count} files…";

            var token  = _cts.Token;
            var chunks = await Task.Run(() => _chunkingService.CreateChunks(files, (int)MaxTokensPerChunk, token), token);

            TotalTokens   = chunks.Sum(c => c.EstimatedTokens);
            TotalProjects = chunks.Select(c => c.ProjectName).Distinct().Count();

            foreach (var chunk in chunks) { SubscribeChunk(chunk); Chunks.Add(chunk); }

            ChunkCount = Chunks.Count;
            HasChunks  = ChunkCount > 0;
            StatusText = $"✅  {files.Count} files · {TotalProjects} projects · {ChunkCount} chunks · ~{TotalTokens:N0} tokens";
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        catch (Exception ex)
        {
            HasError = true; ErrorText = ex.Message;
            StatusText = "Error — see message below.";
            _logger.LogError(ex, "Fetch error.");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ShowBranchPickerAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || !RepoUrl.Contains("github.com"))
        { StatusText = "⚠️ Enter a valid GitHub URL before loading branches."; return; }

        IsFetchingBranches = true;
        try
        {
            var branches = await _gitHubService.FetchBranchesAsync(RepoUrl.Trim(), AccessToken);
            if (branches.Count == 0)
            { StatusText = "⚠️ No branches found. A token may be required for branch listing on public repos."; return; }

            BranchOptions.Clear();
            foreach (var b in branches) BranchOptions.Add(b);
            OnPropertyChanged(nameof(HasBranchOptions));
            BranchPickerRequested?.Invoke(this, branches);
        }
        catch (Exception ex) { StatusText = $"⚠️ Could not fetch branches: {ex.Message}"; }
        finally { IsFetchingBranches = false; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

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
        try { await _shareService.ShareTextAsync(chunk.Content, $"Chunk {chunk.Index + 1} · {chunk.ProjectName}"); }
        catch (Exception ex) { StatusText = $"⚠️ Could not open share sheet: {ex.Message}"; }
    }

    [RelayCommand] private void TogglePreview(CodeChunk chunk) { if (chunk is not null) chunk.IsPreviewExpanded = !chunk.IsPreviewExpanded; }
    [RelayCommand] private void ToggleChunkSelection(CodeChunk chunk) { if (chunk is not null) chunk.IsSelected = !chunk.IsSelected; }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ShareSelectedAsync()
    {
        var selected = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        if (selected.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var c in selected) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(c.Content); }
        var title = selected.Count == 1
            ? $"Chunk {selected[0].Index + 1} · {selected[0].ProjectName}"
            : $"{selected.Count} chunks · {string.Join(", ", selected.Select(c => c.ProjectName).Distinct())}";
        try { await _shareService.ShareTextAsync(sb.ToString(), title); }
        catch (Exception ex) { StatusText = $"⚠️ Could not open share sheet: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelection() { foreach (var c in Chunks) c.IsSelected = false; }

    // ── Prompts ────────────────────────────────────────────────────────────

    [RelayCommand] private void TogglePrompts() => IsPromptsExpanded = !IsPromptsExpanded;

    [RelayCommand]
    private async Task CopyPromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        try { await _clipboard.SetTextAsync(prompt.Content); foreach (var p in Prompts) p.IsCopied = false; prompt.IsCopied = true; }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not copy prompt."); }
    }

    [RelayCommand]
    private void StartEditPrompt(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.EditTitle = prompt.Title; prompt.EditContent = prompt.Content; prompt.IsEditing = true;
    }

    [RelayCommand]
    private async Task SavePromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.Title = prompt.EditTitle.Trim(); prompt.Content = prompt.EditContent.Trim(); prompt.IsEditing = false;
        try { var r = await _db.UpsertPromptAsync(prompt.ToRecord(Prompts.IndexOf(prompt))); prompt.Id = r.Id; }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save prompt."); }
    }

    [RelayCommand] private void CancelEditPrompt(PromptItem prompt) { if (prompt is not null) prompt.IsEditing = false; }

    [RelayCommand]
    private async Task DeletePromptAsync(PromptItem prompt)
    {
        if (prompt is null || prompt.IsBuiltIn) return;
        try { await _db.DeletePromptAsync(prompt.Id); Prompts.Remove(prompt); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete prompt."); }
    }

    [RelayCommand]
    private void AddNewPrompt()
    {
        Prompts.Add(new PromptItem { IsEditing = true, EditTitle = "New Prompt", EditContent = "" });
    }

    // ── URL / repo ─────────────────────────────────────────────────────────

    [RelayCommand] private void ClearUrl() => RepoUrl = string.Empty;

    [RelayCommand]
    private async Task PasteUrlAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) RepoUrl = text.Trim().Trim('"');
    }

    [RelayCommand]
    private async Task ClearSavedReposAsync()
    {
        try { await _db.ClearAllReposAsync(); SavedRepos.Clear(); SelectedSavedRepo = null; }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not clear saved repos."); }
    }

    [RelayCommand] private void RenameSelectedRepo() { if (SelectedSavedRepo is not null) RepoRenameRequested?.Invoke(this, SelectedSavedRepo); }

    public async Task SetRepoNameAsync(SavedRepo repo, string name)
    {
        repo.Name = name;
        await _db.UpsertRepoAsync(repo);
        await RefreshSavedReposAsync();
    }

    [RelayCommand] private void ShowTokenInfo() => TokenInfoRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ClearToken()
    {
        try { SecureStorage.Default.Remove("github_token"); } catch (Exception ex) { _logger.LogWarning(ex, "Remove token failed."); }
        AccessToken = string.Empty; TokenIsSaved = false;
    }

    [RelayCommand] private void ClearKeywordFilter() => KeywordFilter = string.Empty;

    // ── Filter toggles ─────────────────────────────────────────────────────

    [RelayCommand] private void ToggleFileTypes()    => IsFileTypesExpanded    = !IsFileTypesExpanded;
    [RelayCommand] private void ToggleFolders()      => IsFoldersExpanded      = !IsFoldersExpanded;
    [RelayCommand] private void ToggleFilePatterns() => IsFilePatternsExpanded = !IsFilePatternsExpanded;

    [RelayCommand] private void ToggleAllFileTypes()    { bool any = FileTypeFilters.Any(f => f.IsEnabled);    foreach (var f in FileTypeFilters)    f.IsEnabled  = !any; }
    [RelayCommand] private void ToggleAllFolderFilters(){ bool any = FolderFilters.Any(f => f.IsExcluded);     foreach (var f in FolderFilters)      f.IsExcluded = !any; }
    [RelayCommand] private void ToggleAllFilePatterns() { bool any = FilePatternFilters.Any(f => f.IsEnabled); foreach (var f in FilePatternFilters) f.IsEnabled  = !any; }

    [RelayCommand]
    private void AddCustomFolder()
    {
        var name = CustomFolderInput.Trim();
        if (string.IsNullOrWhiteSpace(name) || FolderFilters.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        FolderFilters.Add(new FolderFilter { Name = name, IsExcluded = true });
        CustomFolderInput = string.Empty;
    }

    [RelayCommand]
    private void AddCustomPattern()
    {
        var pattern = CustomPatternInput.Trim();
        if (string.IsNullOrWhiteSpace(pattern) || FilePatternFilters.Any(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))) return;
        FilePatternFilters.Add(new FilePatternFilter { Pattern = pattern, IsEnabled = true });
        CustomPatternInput = string.Empty;
    }

    [RelayCommand]
    private void Reset()
    {
        UnsubscribeAllChunks(); Chunks.Clear();
        ChunkCount = CopiedCount = SelectedCount = TotalFiles = TotalTokens = TotalProjects = 0;
        HasChunks = HasError = false; ErrorText = string.Empty;
        StatusText = "Enter a GitHub URL or local path, then press Chunk.";
    }

    // ── Dispose ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel(); _cts?.Dispose(); _cts = null;
        foreach (var (f, h) in _fileTypeHandlers) f.PropertyChanged -= h;
        _fileTypeHandlers.Clear();
        UnsubscribeAllChunks();
    }
}
