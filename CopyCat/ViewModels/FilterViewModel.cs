using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CopyCat.Messages;
using CopyCat.Models;
using CopyCat.Models.Catalog;
using CopyCat.Services;
using CopyCat.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

namespace CopyCat.ViewModels;

/// <summary>
/// Owns all file-filtering configuration.
///
/// CHANGES IN THIS VERSION
/// ───────────────────────
/// • FetchedFolders collection — distinct top-level folders from the last fetch (Item 4).
/// • PopulateFetchedFolders — builds FetchedFolders after every fetch.
/// • ToggleFetchedFolderCommand — syncs a folder toggle back into FolderFilters chips.
/// • HasFetchedFolders / FetchedFoldersCountLabel computed properties.
/// </summary>
public partial class FilterViewModel : ObservableObject,
    IRecipient<RepoSelectedMessage>
{
    private readonly IGitHubService           _gitHubService;
    private readonly ILocalFileService        _localFileService;
    private readonly ILogger<FilterViewModel> _logger;

    private readonly List<(FileTypeFilter Filter, PropertyChangedEventHandler Handler)> _fileTypeHandlers = [];
    private CancellationTokenSource? _autoDetectDebounce;

    public FilterViewModel(
        IGitHubService           gitHubService,
        ILocalFileService        localFileService,
        ILogger<FilterViewModel> logger)
    {
        _gitHubService    = gitHubService;
        _localFileService = localFileService;
        _logger           = logger;

        InitFileTypeFilters();
        InitFolderFilters();
        InitFilePatternFilters();

        FetchedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFetchedFiles));
            OnPropertyChanged(nameof(FetchedFilesCountLabel));
        };

        FetchedFolders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFetchedFolders));
            OnPropertyChanged(nameof(FetchedFoldersCountLabel));
        };

        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // ── Collections ────────────────────────────────────────────────────────

    public ObservableCollection<FileTypeFilter>    FileTypeFilters    { get; } = [];
    public ObservableCollection<FolderFilter>      FolderFilters      { get; } = [];
    public ObservableCollection<FilePatternFilter> FilePatternFilters { get; } = [];
    public ObservableCollection<FetchedFileEntry>  FetchedFiles       { get; } = [];

    /// <summary>Distinct folders discovered in the last fetch. Powers the "From this repo" picker.</summary>
    public ObservableCollection<FetchedFolderEntry> FetchedFolders { get; } = [];

    // ── Observable properties ───────────────────────────────────────────────

    [ObservableProperty] private bool   _isFileTypesExpanded;
    [ObservableProperty] private bool   _isFoldersExpanded;
    [ObservableProperty] private bool   _isFilePatternsExpanded;
    [ObservableProperty] private bool   _isKeywordExpanded;
    [ObservableProperty] private bool   _isAutoDetecting;
    [ObservableProperty] private bool   _isFetchedFilesExpanded;
    [ObservableProperty] private bool   _isFetchedFoldersExpanded;
    [ObservableProperty] private string _keywordFilter      = string.Empty;
    [ObservableProperty] private string _customFolderInput  = string.Empty;
    [ObservableProperty] private string _customPatternInput = string.Empty;
    private string _currentRepoUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoDetectStatus))]
    private string _autoDetectStatusText = string.Empty;

    // ── Computed ────────────────────────────────────────────────────────────

    public bool   HasAutoDetectStatus      => !string.IsNullOrEmpty(AutoDetectStatusText);
    public bool   HasFetchedFiles          => FetchedFiles.Count > 0;
    public bool   HasFetchedFolders        => FetchedFolders.Count > 0;
    public string FetchedFilesCountLabel   => $"FETCHED FILES ({FetchedFiles.Count})";
    public string FetchedFoldersCountLabel => $"FROM THIS REPO ({FetchedFolders.Count} folders)";

    public string FileTypeCardSummary
    {
        get
        {
            var enabled = FileTypeFilters.Where(f => f.IsEnabled).Select(f => f.Label).ToList();
            if (enabled.Count == 0) return "no types selected";
            const int maxVisible = 4;
            return enabled.Count <= maxVisible
                ? string.Join("  ", enabled)
                : string.Join("  ", enabled.Take(maxVisible)) + $"  +{enabled.Count - maxVisible} more";
        }
    }

    public string FolderFilterSummary
    {
        get
        {
            var excluded = FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name).ToList();
            return excluded.Count == 0 ? "none excluded" : $"Excluded: {string.Join(", ", excluded)}";
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

    // ── Public helpers ────────────────────────────────────────────────────────

    public IEnumerable<string> GetSelectedExtensions() =>
        FileTypeFilters.Where(f => f.IsEnabled).SelectMany(f => f.Extensions);

    public IEnumerable<string> GetExcludedFolders() =>
        FolderFilters.Where(f => f.IsExcluded).Select(f => f.Name);

    public IEnumerable<string> GetExcludedFilePatterns() =>
        FilePatternFilters.Where(f => f.IsEnabled).Select(f => f.Pattern);

    public void PopulateFetchedFiles(List<(string Path, string Content)> files)
    {
        FetchedFiles.Clear();
        foreach (var (path, _) in files)
        {
            var normalised = path.Replace('\\', '/');
            var lastSlash  = normalised.LastIndexOf('/');
            var folder     = lastSlash > 0 ? normalised[..lastSlash] : "Root";
            var fileName   = lastSlash > 0 ? normalised[(lastSlash + 1)..] : normalised;

            var entry = new FetchedFileEntry { Path = normalised, FileName = fileName, Folder = folder };
            entry.IsExcluded = FilePatternFilters.Any(f =>
                f.IsEnabled && fileName.Equals(f.Pattern, StringComparison.OrdinalIgnoreCase));
            entry.PropertyChanged += OnFetchedFileEntryChanged;
            FetchedFiles.Add(entry);
        }

        PopulateFetchedFolders(files);
    }

    /// <summary>
    /// ITEM 4 — Builds the "From this repo" folder picker from the fetched file list.
    /// Extracts top-level folder segments, deduplicates, counts files per folder,
    /// and cross-checks against existing FolderFilters to pre-tick already-excluded names.
    /// </summary>
    private void PopulateFetchedFolders(List<(string Path, string Content)> files)
    {
        FetchedFolders.Clear();

        var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, _) in files)
        {
            var normalised = path.Replace('\\', '/');
            // Use top-level segment only (first path component)
            var firstSlash = normalised.IndexOf('/');
            var topFolder  = firstSlash > 0 ? normalised[..firstSlash] : "Root";
            folderCounts[topFolder] = folderCounts.GetValueOrDefault(topFolder, 0) + 1;
        }

        var excludedNames = FolderFilters
            .Where(f => f.IsExcluded)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, count) in folderCounts.OrderBy(kv => kv.Key))
        {
            var entry = new FetchedFolderEntry
            {
                Name      = name,
                FileCount = count,
                IsExcluded = excludedNames.Contains(name),
            };
            entry.PropertyChanged += OnFetchedFolderEntryChanged;
            FetchedFolders.Add(entry);
        }

        WeakReferenceMessenger.Default.Send(new FetchedFoldersAvailableMessage(FetchedFolders.Count));
    }

    public bool CanAutoDetect => !IsAutoDetecting && !string.IsNullOrWhiteSpace(_currentRepoUrl);

    public void UpdateRepoUrl(string url) => _currentRepoUrl = url;

    // ── Messenger ───────────────────────────────────────────────────────────

    public void Receive(RepoSelectedMessage message)
    {
        RestoreFromWorkspace(
            message.Repo.SavedEnabledExts,
            message.Repo.SavedExcludedFolders,
            message.Repo.SavedPatterns);
    }

    public void RestoreFromWorkspace(
        string savedEnabledExts,
        string savedExcludedFolders,
        string savedPatterns)
    {
        try
        {
            if (!string.IsNullOrEmpty(savedEnabledExts))
            {
                var labels = JsonSerializer.Deserialize(savedEnabledExts, CopyCatJsonContext.Default.ListString) ?? [];
                if (labels.Count > 0)
                    foreach (var f in FileTypeFilters)
                        f.IsEnabled = labels.Contains(f.Label, StringComparer.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(savedExcludedFolders))
            {
                var names = JsonSerializer.Deserialize(savedExcludedFolders, CopyCatJsonContext.Default.ListString) ?? [];
                if (names.Count > 0)
                    foreach (var f in FolderFilters)
                        f.IsExcluded = names.Contains(f.Name, StringComparer.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(savedPatterns))
            {
                var patterns = JsonSerializer.Deserialize(savedPatterns, CopyCatJsonContext.Default.ListString) ?? [];
                foreach (var pattern in patterns)
                {
                    if (!FilePatternFilters.Any(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                    {
                        var pf = new FilePatternFilter { Pattern = pattern, IsEnabled = true };
                        pf.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
                        FilePatternFilters.Add(pf);
                    }
                    else
                        FilePatternFilters.First(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                            .IsEnabled = true;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not restore filter workspace."); }
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand] private static void ToggleFileType(FileTypeFilter f)       { if (f is not null) f.IsEnabled  = !f.IsEnabled;  }
    [RelayCommand] private static void ToggleFolder(FolderFilter f)           { if (f is not null) f.IsExcluded = !f.IsExcluded; }
    [RelayCommand] private static void ToggleFilePattern(FilePatternFilter p) { if (p is not null) p.IsEnabled  = !p.IsEnabled;  }

    [RelayCommand] private void ToggleAllFileTypes()     { bool a = FileTypeFilters.Any(f => f.IsEnabled);    foreach (var f in FileTypeFilters)    f.IsEnabled  = !a; }
    [RelayCommand] private void ToggleAllFolderFilters() { bool a = FolderFilters.Any(f => f.IsExcluded);     foreach (var f in FolderFilters)       f.IsExcluded = !a; }
    [RelayCommand] private void ToggleAllFilePatterns()  { bool a = FilePatternFilters.Any(f => f.IsEnabled); foreach (var f in FilePatternFilters)  f.IsEnabled  = !a; }

    [RelayCommand] private void ToggleFileTypes()       => IsFileTypesExpanded       = !IsFileTypesExpanded;
    [RelayCommand] private void ToggleFolders()         => IsFoldersExpanded         = !IsFoldersExpanded;
    [RelayCommand] private void ToggleFilePatterns()    => IsFilePatternsExpanded    = !IsFilePatternsExpanded;
    [RelayCommand] private void ToggleKeyword()         => IsKeywordExpanded         = !IsKeywordExpanded;
    [RelayCommand] private void ClearKeywordFilter()    => KeywordFilter             = string.Empty;
    [RelayCommand] private void ToggleFetchedFiles()    => IsFetchedFilesExpanded    = !IsFetchedFilesExpanded;
    [RelayCommand] private void ToggleFetchedFolders()  => IsFetchedFoldersExpanded  = !IsFetchedFoldersExpanded;

    [RelayCommand]
    private void ToggleFetchedFileExclusion(FetchedFileEntry entry)
    {
        if (entry is not null) entry.IsExcluded = !entry.IsExcluded;
    }

    /// <summary>ITEM 4 — Syncs a FetchedFolderEntry toggle back into FolderFilters chips.</summary>
    [RelayCommand]
    private void ToggleFetchedFolder(FetchedFolderEntry entry)
    {
        if (entry is null) return;
        entry.IsExcluded = !entry.IsExcluded;

        if (entry.IsExcluded)
        {
            // Add to FolderFilters if not already present
            if (!FolderFilters.Any(f => f.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var chip = new FolderFilter { Name = entry.Name, IsExcluded = true };
                chip.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FolderFilterSummary));
                FolderFilters.Add(chip);
                OnPropertyChanged(nameof(FolderFilterSummary));
            }
            else
            {
                var existing = FolderFilters.First(f => f.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                existing.IsExcluded = true;
                OnPropertyChanged(nameof(FolderFilterSummary));
            }
        }
        else
        {
            // Remove from FolderFilters
            var chip = FolderFilters.FirstOrDefault(f => f.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            if (chip is not null)
            {
                FolderFilters.Remove(chip);
                OnPropertyChanged(nameof(FolderFilterSummary));
            }
        }
    }

    [RelayCommand]
    private void AddCustomFolder()
    {
        var name = CustomFolderInput.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            FolderFilters.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
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
        foreach (var pattern in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t))
                                   .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (FilePatternFilters.Any(f => f.Pattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))) continue;
            var p = new FilePatternFilter { Pattern = pattern, IsEnabled = true };
            p.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
            FilePatternFilters.Add(p);
        }
        CustomPatternInput = string.Empty;
        OnPropertyChanged(nameof(FilePatternSummary));
    }

    [RelayCommand]
    private async Task AutoDetectFileTypesAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentRepoUrl) || IsAutoDetecting) return;
        IsAutoDetecting = true;
        try
        {
            IReadOnlyDictionary<string, int> detected;
            var inputPath = _currentRepoUrl.Trim().Trim('"');

            if (IsLocalPathHelper(inputPath))
            {
                var extCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(inputPath))
                    foreach (var file in EnumerateFilesSafe(inputPath))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (!string.IsNullOrEmpty(ext))
                            extCounts[ext] = extCounts.GetValueOrDefault(ext, 0) + 1;
                    }
                detected = extCounts;
            }
            else
            {
                detected = await _gitHubService.DetectFileTypesInRepoAsync(inputPath, string.Empty, string.Empty);
            }

            if (detected.Count == 0) { AutoDetectStatusText = "⚠️ No file types detected."; return; }

            foreach (var f in FileTypeFilters)
                f.IsEnabled = f.Extensions.Any(e => detected.ContainsKey(e));

            var inferredLabels = new List<string>();
            foreach (var (detectedExt, _) in detected)
                foreach (var (_, companionExts) in FileTypeCatalog.CompanionInference)
                {
                    var primary = FileTypeFilters.FirstOrDefault(f =>
                        f.Extensions.Any(e => e.Equals(detectedExt, StringComparison.OrdinalIgnoreCase)));
                    if (primary is null) continue;
                    foreach (var companionExt in companionExts)
                    {
                        var companion = FileTypeFilters.FirstOrDefault(f =>
                            f.Extensions.Any(e => e.Equals(companionExt, StringComparison.OrdinalIgnoreCase)));
                        if (companion is not null && !companion.IsEnabled)
                        { companion.IsEnabled = true; inferredLabels.Add(companion.Label); }
                    }
                }

            if (detected.Any(kv => kv.Key.Equals(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var csproj = FileTypeFilters.FirstOrDefault(f => f.Label == ".csproj");
                if (csproj is not null && !csproj.IsEnabled)
                { csproj.IsEnabled = true; if (!inferredLabels.Contains(".csproj")) inferredLabels.Add(".csproj"); }
            }

            var enabledCount = FileTypeFilters.Count(f => f.IsEnabled);
            var inferredPart = inferredLabels.Count > 0
                ? $"  ·  Inferred: {string.Join(", ", inferredLabels.Distinct())}"
                : string.Empty;
            AutoDetectStatusText = $"✅ Detected {detected.Count} type{(detected.Count == 1 ? "" : "s")}{inferredPart}  ·  {enabledCount} filters enabled";
        }
        catch (Exception ex) { AutoDetectStatusText = $"⚠️ Detection failed: {ex.Message}"; }
        finally { IsAutoDetecting = false; }
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private void InitFileTypeFilters()
    {
        foreach (var f in FileTypeCatalog.CreateDefaults())
        {
            PropertyChangedEventHandler h = (_, _) => OnPropertyChanged(nameof(FileTypeCardSummary));
            f.PropertyChanged += h;
            _fileTypeHandlers.Add((f, h));
            FileTypeFilters.Add(f);
        }
    }

    private void InitFolderFilters()
    {
        foreach (var f in FolderCatalog.CreateDefaults())
        {
            f.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FolderFilterSummary));
            FolderFilters.Add(f);
        }
    }

    private void InitFilePatternFilters()
    {
        foreach (var p in PatternCatalog.CreateDefaults())
        {
            p.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FilePatternSummary));
            FilePatternFilters.Add(p);
        }
    }

    private void OnFetchedFileEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FetchedFileEntry.IsExcluded) || sender is not FetchedFileEntry entry) return;
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
            var auto = FilePatternFilters.FirstOrDefault(f =>
                f.IsAutoAdded && f.Pattern.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase));
            if (auto is not null) { FilePatternFilters.Remove(auto); OnPropertyChanged(nameof(FilePatternSummary)); }
        }
    }

    private void OnFetchedFolderEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Sync is handled by ToggleFetchedFolderCommand.
        // This handler is intentionally empty — the entry's IsExcluded is
        // the source of truth; FolderFilters chips are the downstream effect.
    }

    private static bool IsLocalPathHelper(string path) =>
        !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        (path.StartsWith('/') || path.StartsWith('~') ||
         (path.Length >= 2 && path[1] == ':') || path.StartsWith(@"\\"));

    private static IEnumerable<string> EnumerateFilesSafe(string baseDir)
    {
        var queue = new Queue<string>([baseDir]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            IEnumerable<string> files;
            try   { files = Directory.EnumerateFiles(current); } catch { continue; }
            foreach (var f in files) yield return f;
            IEnumerable<string> dirs;
            try   { dirs = Directory.EnumerateDirectories(current); } catch { continue; }
            foreach (var d in dirs) queue.Enqueue(d);
        }
    }

    public void Dispose()
    {
        foreach (var (f, h) in _fileTypeHandlers) f.PropertyChanged -= h;
        _fileTypeHandlers.Clear();
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _autoDetectDebounce?.Cancel();
        _autoDetectDebounce?.Dispose();
    }
}
