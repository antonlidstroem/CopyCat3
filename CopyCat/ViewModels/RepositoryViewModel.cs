using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CopyCat.Messages;
using CopyCat.Models;
using CopyCat.Services;
using CopyCat.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace CopyCat.ViewModels;

/// <summary>
/// Owns everything related to the source repository:
/// URL input, branch, GitHub token, recent repos list, workspace save/restore.
///
/// CHANGES IN THIS VERSION
/// ───────────────────────
/// • Token is always cleared / reloaded when a repo is selected (Item 1).
/// • PasteTokenCommand pastes clipboard into the token field (Item 1).
/// • IsLocalPath + LocalPathGuidanceText give the UI contextual hints (Item 3).
/// • Exposes SavedRepoDisplayItems (observable wrappers) for expandable rows (Item 2).
/// </summary>
public partial class RepositoryViewModel : ObservableObject
{
    private readonly IRepoRepository              _repoRepo;
    private readonly ILogger<RepositoryViewModel> _logger;

    public event EventHandler<List<string>>?    BranchPickerRequested;
    public event EventHandler?                  TokenInfoRequested;
    public event EventHandler<SavedRepo>?       RepoRenameRequested;
    public event EventHandler<List<SavedRepo>>? ShowHistoryRequested;
    public event EventHandler<string>?          ShowInfoRequested;

    public RepositoryViewModel(
        IRepoRepository              repoRepo,
        ILogger<RepositoryViewModel> logger)
    {
        _repoRepo = repoRepo;
        _logger   = logger;

        SavedRepoDisplayItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedRepos));
            OnPropertyChanged(nameof(SavedRepos));
        };
    }

    // ── Collections ────────────────────────────────────────────────────────

    /// <summary>Observable wrappers used by the XAML list (support expand/summary).</summary>
    public ObservableCollection<SavedRepoDisplayItem> SavedRepoDisplayItems { get; } = [];

    /// <summary>Raw repos — kept for backward compat with code that iterates them.</summary>
    public IEnumerable<SavedRepo> SavedRepos => SavedRepoDisplayItems.Select(i => i.Repo);

    public ObservableCollection<string> BranchOptions { get; } = [];

    // ── Observable properties ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRepo))]
    [NotifyPropertyChangedFor(nameof(HasWorkspaceSaved))]
    private SavedRepo? _selectedSavedRepo;

    /// <summary>
    /// Assigned by MainViewModel — reads cross-VM state for SaveWorkspace.
    /// </summary>
    public IAsyncRelayCommand? SaveWorkspaceCommand { get; set; }

    partial void OnSelectedSavedRepoChanged(SavedRepo? value)
    {
        if (value is null)
        {
            // ── ITEM 1: always clear token when deselecting ──────────────────
            AccessToken  = string.Empty;
            TokenIsSaved = false;
            return;
        }

        RepoUrl = value.Url;
        Branch  = value.Branch;

        // ── ITEM 1: clear stale token from previous repo BEFORE loading ──────
        AccessToken  = string.Empty;
        TokenIsSaved = false;

        if (value.HasToken)
            FireAndForget(LoadTokenForRepoAsync(value));

        WeakReferenceMessenger.Default.Send(new RepoSelectedMessage(value));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBranchOptions))]
    [NotifyPropertyChangedFor(nameof(BranchButtonLabel))]
    [NotifyPropertyChangedFor(nameof(IsLocalPath))]
    [NotifyPropertyChangedFor(nameof(LocalPathGuidanceText))]
    [NotifyPropertyChangedFor(nameof(ShowBranchSection))]
    [NotifyPropertyChangedFor(nameof(ShowTokenSection))]
    private string _repoUrl = string.Empty;

    partial void OnRepoUrlChanged(string _)
    {
        BranchOptions.Clear();
        OnPropertyChanged(nameof(HasBranchOptions));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BranchButtonLabel))]
    private string _branch = "main";

    [ObservableProperty] private string _accessToken  = string.Empty;
    [ObservableProperty] private bool   _tokenIsSaved;
    [ObservableProperty] private bool   _isFetchingBranches;

    // ── Computed ────────────────────────────────────────────────────────────

    public bool   HasSelectedRepo   => SelectedSavedRepo is not null;
    public bool   HasSavedRepos     => SavedRepoDisplayItems.Count > 0;
    public bool   HasWorkspaceSaved => SelectedSavedRepo?.HasWorkspace ?? false;
    public bool   HasBranchOptions  => BranchOptions.Count > 0;
    public string BranchButtonLabel => string.IsNullOrWhiteSpace(Branch) ? "Select branch…" : Branch;

    // ── ITEM 3: local path detection ──────────────────────────────────────────

    /// <summary>True when the URL field contains a local file-system path.</summary>
    public bool IsLocalPath =>
        !string.IsNullOrWhiteSpace(RepoUrl) &&
        !RepoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !RepoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        (RepoUrl.StartsWith('/') || RepoUrl.StartsWith('~') ||
         (RepoUrl.Length >= 2 && RepoUrl[1] == ':') || RepoUrl.StartsWith(@"\\"));

    /// <summary>Contextual guidance shown when a local path is detected.</summary>
    public string LocalPathGuidanceText =>
        IsLocalPath
            ? "📁 Local path detected — Branch picker and GitHub Token are not used. "
              + "CopyCat reads files directly from this folder. "
              + "Auto-detect works normally — tap 🔍 Auto in FILE TYPES."
            : string.Empty;

    /// <summary>Branch section visibility — hidden for local paths.</summary>
    public bool ShowBranchSection => !IsLocalPath;

    /// <summary>Token section visibility — hidden for local paths.</summary>
    public bool ShowTokenSection  => !IsLocalPath;

    // ── Initialisation ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try { await _repoRepo.InitializeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "DB init failed."); }

        try
        {
            var token = await SecureStorage.Default.GetAsync("github_token");
            if (!string.IsNullOrWhiteSpace(token)) { AccessToken = token; TokenIsSaved = true; }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not read token."); }

        await MigrateOldRecentUrlsAsync();
        await RefreshSavedReposAsync();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand] private void ClearUrl()           => RepoUrl = string.Empty;
    [RelayCommand] private void ClearSelectedRepo()  => SelectedSavedRepo = null;
    [RelayCommand] private void SelectRepo(SavedRepo repo) => SelectedSavedRepo = repo;

    [RelayCommand]
    private async Task PasteUrlAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) RepoUrl = text.Trim().Trim('"');
    }

    // ── ITEM 1: paste clipboard into token field ──────────────────────────────
    [RelayCommand]
    private async Task PasteTokenAsync()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) AccessToken = text.Trim();
    }

    [RelayCommand]
    private async Task DeleteRepoAsync(SavedRepo repo)
    {
        if (repo is null) return;
        try
        {
            await _repoRepo.DeleteRepoAsync(repo.Id);
            try { SecureStorage.Default.Remove($"repo_token_{repo.Id}"); } catch { }
            if (SelectedSavedRepo?.Id == repo.Id) SelectedSavedRepo = null;
            await RefreshSavedReposAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete repo {Id}.", repo.Id); }
    }

    [RelayCommand]
    private void RenameSelectedRepo()
    {
        if (SelectedSavedRepo is not null) RepoRenameRequested?.Invoke(this, SelectedSavedRepo);
    }

    [RelayCommand] private void ShowTokenInfo() => TokenInfoRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ShowHistory()   => ShowHistoryRequested?.Invoke(this, SavedRepos.ToList());
    [RelayCommand] private void ShowInfo(string message) =>
        ShowInfoRequested?.Invoke(this, message ?? string.Empty);

    [RelayCommand]
    private void ClearToken()
    {
        try { SecureStorage.Default.Remove("github_token"); }
        catch (Exception ex) { _logger.LogWarning(ex, "Remove token failed."); }
        AccessToken = string.Empty; TokenIsSaved = false;
    }

    [RelayCommand]
    private async Task ShowBranchPickerAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoUrl) || !RepoUrl.Contains("github.com")) return;
        IsFetchingBranches = true;
        try { BranchPickerRequested?.Invoke(this, BranchOptions.ToList()); }
        finally { IsFetchingBranches = false; }
    }

    // ── ITEM 2: expand/collapse a saved repo row ──────────────────────────────
    [RelayCommand]
    private void ToggleRepoExpandCommand(SavedRepoDisplayItem item)
    {
        if (item is null) return;
        // Collapse all others, expand only the tapped one
        foreach (var i in SavedRepoDisplayItems)
            if (i != item) i.IsExpanded = false;
        item.IsExpanded = !item.IsExpanded;
    }

    // ── Workspace persistence ────────────────────────────────────────────────

    public async Task PersistWorkspaceAsync(
        int    maxTokens,
        string enabledExts,
        string excludedFolders,
        string savedPatterns,
        int    promptSortOrder)
    {
        if (SelectedSavedRepo is null) return;
        var repo = SelectedSavedRepo;
        repo.SavedMaxTokens       = maxTokens;
        repo.SavedEnabledExts     = enabledExts;
        repo.SavedExcludedFolders = excludedFolders;
        repo.SavedPatterns        = savedPatterns;
        repo.SavedPromptSortOrder = promptSortOrder;
        await _repoRepo.UpdateRepoWorkspaceAsync(repo);
        OnPropertyChanged(nameof(HasWorkspaceSaved));
        // Refresh display items so summary updates immediately
        await RefreshSavedReposAsync();
    }

    // ── Public helpers ───────────────────────────────────────────────────────

    public async Task SaveCurrentRepoAsync(string url, string branch)
    {
        try
        {
            var repos    = await _repoRepo.GetSavedReposAsync();
            var existing = repos.FirstOrDefault(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { existing.Branch = branch; await _repoRepo.UpsertRepoAsync(existing); }
            else await _repoRepo.UpsertRepoAsync(new SavedRepo { Url = url, Branch = branch });
            await RefreshSavedReposAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save repo."); }
    }

    public async Task SetRepoNameAsync(SavedRepo repo, string name)
    {
        repo.Name = name;
        await _repoRepo.UpsertRepoAsync(repo);
        await RefreshSavedReposAsync();
    }

    public async Task RefreshSavedReposAsync()
    {
        try
        {
            var repos = await _repoRepo.GetSavedReposAsync();
            SavedRepoDisplayItems.Clear();
            foreach (var r in repos.Take(10))
                SavedRepoDisplayItems.Add(new SavedRepoDisplayItem(r));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load saved repos."); }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task MigrateOldRecentUrlsAsync()
    {
        try
        {
            var old = Preferences.Default.Get("recent_urls", string.Empty);
            if (string.IsNullOrWhiteSpace(old)) return;
            var existing = (await _repoRepo.GetSavedReposAsync())
                .Select(r => r.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var url in old.Split('|').Where(u => !string.IsNullOrWhiteSpace(u)).Reverse())
                if (!existing.Contains(url))
                    await _repoRepo.UpsertRepoAsync(new SavedRepo { Url = url, Branch = "main" });
            Preferences.Default.Remove("recent_urls");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "URL migration failed."); }
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

    private void FireAndForget(Task task) =>
        task.ContinueWith(
            t => _logger.LogWarning(t.Exception, "Fire-and-forget faulted."),
            TaskContinuationOptions.OnlyOnFaulted);
}
