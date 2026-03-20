using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CopyCat.Messages;
using CopyCat.Models;
using CopyCat.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace CopyCat.ViewModels;

/// <summary>
/// Owns the result chunk list: filtering/search, per-chunk UI state,
/// multi-select, merge, copy, and share.
///
/// CHANGES IN THIS VERSION
/// ───────────────────────
/// ITEM 8 — Active prompt card treated as "Chunk 0":
///   • IsPromptCardSelected, CopyActivePromptCommand, IsActivePromptCopied.
///   • WithPrompt() now only prepends when IsPromptCardSelected AND copying
///     a non-prompt chunk. Prompt is NO LONGER auto-prepended to every copy.
///   • SelectAllChunks includes the prompt card in its sweep.
///
/// ITEM 9 — Quick Prompt ephemeral card:
///   • QuickPromptText (not persisted, cleared on Reset).
///   • IsQuickPromptSelected, IsQuickPromptCopied.
///   • CopyQuickPromptCommand, ToggleQuickPromptSelectionCommand.
///   • AssembleCopyPayload() handles ordered concatenation:
///     selected prompt → quick prompt → selected chunks.
/// </summary>
public partial class ChunkListViewModel : ObservableObject,
    IRecipient<FetchCompletedMessage>,
    IRecipient<FetchResetMessage>,
    IRecipient<PromptSelectionChangedMessage>
{
    private readonly IClipboardService          _clipboard;
    private readonly IShareService              _shareService;
    private readonly IChunkingService           _chunkingService;
    private readonly ILogger<ChunkListViewModel> _logger;

    private readonly List<(CodeChunk Chunk, PropertyChangedEventHandler Handler)> _chunkHandlers = [];
    private readonly ObservableCollection<CodeChunk> _filteredChunks = [];
    private PromptItem? _activePrompt;

    public ChunkListViewModel(
        IClipboardService           clipboard,
        IShareService               shareService,
        IChunkingService            chunkingService,
        ILogger<ChunkListViewModel> logger)
    {
        _clipboard       = clipboard;
        _shareService    = shareService;
        _chunkingService = chunkingService;
        _logger          = logger;

        Chunks.CollectionChanged += (_, _) =>
        {
            RefreshFilteredChunks(forceRebuild: true);
            OnPropertyChanged(nameof(CopyProgressLabel));
            OnPropertyChanged(nameof(HasCopyProgress));
        };

        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // ── Collections ────────────────────────────────────────────────────────

    public ObservableCollection<CodeChunk> Chunks         { get; } = [];
    public ObservableCollection<CodeChunk> FilteredChunks => _filteredChunks;

    // ── Chunk search & selection ────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChunkSearchSummary))]
    [NotifyPropertyChangedFor(nameof(HasFilteredChunks))]
    private string _chunkSearchText = string.Empty;
    partial void OnChunkSearchTextChanged(string _) => RefreshFilteredChunks();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShareSelectedLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedTokensLabel))]
    [NotifyPropertyChangedFor(nameof(SelectAllChunksLabel))]
    [NotifyPropertyChangedFor(nameof(SelectionWarningLabel))]
    [NotifyPropertyChangedFor(nameof(SelectionWarningColor))]
    [NotifyPropertyChangedFor(nameof(HasSelectionWarning))]
    [NotifyCanExecuteChangedFor(nameof(ShareSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeSelectedCommand))]
    private int _selectedCount;

    [ObservableProperty] private int _copiedCount;
    partial void OnCopiedCountChanged(int _)
    {
        OnPropertyChanged(nameof(CopyProgressLabel));
        OnPropertyChanged(nameof(HasCopyProgress));
    }

    // ── ITEM 8: Active prompt card state ────────────────────────────────────

    /// <summary>Whether the active prompt card is selected for inclusion in multi-copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyCardSelected))]
    private bool _isPromptCardSelected;

    /// <summary>Whether the active prompt was just copied (drives the Copied badge).</summary>
    [ObservableProperty] private bool _isActivePromptCopied;

    /// <summary>Whether an active prompt exists to show in the card.</summary>
    public bool HasActivePrompt => _activePrompt is not null;

    /// <summary>Title of the active prompt for display in the card header.</summary>
    public string ActivePromptTitle => _activePrompt?.Title ?? string.Empty;

    /// <summary>Preview text (first 120 chars) of the active prompt content.</summary>
    public string ActivePromptPreview =>
        _activePrompt is null ? string.Empty :
        _activePrompt.Content.Length <= 120
            ? _activePrompt.Content
            : _activePrompt.Content[..120].TrimEnd() + "…";

    /// <summary>Whether the active prompt full text is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePromptExpandIcon))]
    private bool _isActivePromptExpanded;

    public string ActivePromptExpandIcon => IsActivePromptExpanded ? "▲" : "▼  full prompt";

    public Microsoft.Maui.Graphics.Color ActivePromptBorderColor =>
        IsPromptCardSelected
            ? Microsoft.Maui.Graphics.Color.FromArgb("#00B4BC")
            : Microsoft.Maui.Graphics.Color.FromArgb("#F59E0B");

    public double ActivePromptBorderThickness => IsPromptCardSelected ? 1.5 : 0.8;

    public Microsoft.Maui.Graphics.Color ActivePromptZoneA =>
        IsPromptCardSelected
            ? Microsoft.Maui.Graphics.Color.FromArgb("#0D2A2B")
            : Microsoft.Maui.Graphics.Colors.Transparent;

    // ── ITEM 9: Quick Prompt ephemeral card ─────────────────────────────────

    /// <summary>The quick prompt text. Never persisted. Cleared on Reset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuickPromptText))]
    private string _quickPromptText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyCardSelected))]
    private bool _isQuickPromptSelected;

    [ObservableProperty] private bool _isQuickPromptCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickPromptExpandIcon))]
    private bool _isQuickPromptExpanded;

    public bool   HasQuickPromptText  => !string.IsNullOrWhiteSpace(QuickPromptText);
    public string QuickPromptExpandIcon => IsQuickPromptExpanded ? "▲" : "▼  expand";

    public Microsoft.Maui.Graphics.Color QuickPromptBorderColor =>
        IsQuickPromptSelected
            ? Microsoft.Maui.Graphics.Color.FromArgb("#00B4BC")
            : Microsoft.Maui.Graphics.Color.FromArgb("#2D2D3A");

    public Microsoft.Maui.Graphics.Color QuickPromptZoneA =>
        IsQuickPromptSelected
            ? Microsoft.Maui.Graphics.Color.FromArgb("#0D2A2B")
            : Microsoft.Maui.Graphics.Colors.Transparent;

    // ── Computed ────────────────────────────────────────────────────────────

    public bool   HasChunks         => Chunks.Count > 0;
    public bool   HasFilteredChunks => _filteredChunks.Count > 0;
    public bool   HasSelection      => SelectedCount > 0 || IsPromptCardSelected || IsQuickPromptSelected;
    public bool   HasAnyCardSelected => IsPromptCardSelected || IsQuickPromptSelected || SelectedCount > 0;
    public bool   CanMerge          => SelectedCount >= 2;

    public string SelectAllChunksLabel =>
        Chunks.Count > 0 && Chunks.All(c => c.IsSelected) ? "Deselect all" : "Select all";

    public string ShareSelectedLabel => HasSelection
        ? $"Share selected ({SelectedCount}{(IsPromptCardSelected ? "+prompt" : "")}{(IsQuickPromptSelected ? "+quick" : "")})"
        : "Share selected";

    public string ChunkSearchSummary =>
        string.IsNullOrWhiteSpace(ChunkSearchText) ? string.Empty :
        _filteredChunks.Count == 0 ? "No chunks match" :
        $"{_filteredChunks.Count} of {Chunks.Count} chunk{(Chunks.Count == 1 ? "" : "s")} match";

    public string CopyProgressLabel =>
        CopiedCount == 0 || Chunks.Count == 0 ? string.Empty :
        CopiedCount == Chunks.Count ? $"All {Chunks.Count} chunks copied ✓" :
        $"Chunk {CopiedCount} of {Chunks.Count} copied";

    public bool HasCopyProgress => !string.IsNullOrEmpty(CopyProgressLabel);

    private int SelectedTokens => Chunks.Where(c => c.IsSelected).Sum(c => c.EstimatedTokens);

    public string SelectedTokensLabel =>
        SelectedTokens == 0 ? string.Empty : $"~{SelectedTokens:N0} tokens selected";

    public string SelectionWarningLabel => SelectedTokens switch
    {
        > 200_000 => "⛔ Exceeds Gemini 1.5 Pro share limit (~200K tokens)",
        > 128_000 => "⚠️ May exceed Claude.ai context window (128K tokens)",
        >  32_000 => "⚠️ Too large for GPT-3.5 — use GPT-4 Turbo, Claude, or Gemini",
        >  16_000 => "ℹ️ Above GPT-3.5 limit — works with GPT-4 Turbo, Claude 3+, Gemini 1.5",
        >       0 => "✅ Fits all major AI interfaces",
        _         => string.Empty,
    };

    public Microsoft.Maui.Graphics.Color SelectionWarningColor => SelectedTokens switch
    {
        > 128_000 => Microsoft.Maui.Graphics.Color.FromArgb("#EF4444"),
        >  32_000 => Microsoft.Maui.Graphics.Color.FromArgb("#F59E0B"),
        _         => Microsoft.Maui.Graphics.Color.FromArgb("#00B4BC"),
    };

    public bool HasSelectionWarning => !string.IsNullOrEmpty(SelectionWarningLabel);

    // ── Messenger ───────────────────────────────────────────────────────────

    public void Receive(FetchCompletedMessage msg)
    {
        UnsubscribeAllChunks();
        Chunks.Clear();
        foreach (var chunk in msg.Chunks) { SubscribeChunk(chunk); Chunks.Add(chunk); }
        ChunkSearchText        = string.Empty;
        SelectedCount          = 0;
        CopiedCount            = 0;
        IsPromptCardSelected   = false;
        IsActivePromptCopied   = false;
        IsActivePromptExpanded = false;
        IsQuickPromptSelected  = false;
        IsQuickPromptCopied    = false;
        OnPropertyChanged(nameof(HasChunks));
        OnPropertyChanged(nameof(SelectAllChunksLabel));
    }

    public void Receive(FetchResetMessage _)
    {
        UnsubscribeAllChunks();
        Chunks.Clear();
        ChunkSearchText        = string.Empty;
        SelectedCount          = CopiedCount = 0;
        QuickPromptText        = string.Empty;
        IsPromptCardSelected   = false;
        IsActivePromptCopied   = false;
        IsQuickPromptSelected  = false;
        IsQuickPromptCopied    = false;
        OnPropertyChanged(nameof(HasChunks));
        OnPropertyChanged(nameof(CopyProgressLabel));
        OnPropertyChanged(nameof(HasCopyProgress));
    }

    public void Receive(PromptSelectionChangedMessage msg)
    {
        _activePrompt          = msg.SelectedPrompt;
        IsActivePromptCopied   = false;
        IsPromptCardSelected   = false;
        OnPropertyChanged(nameof(HasActivePrompt));
        OnPropertyChanged(nameof(ActivePromptTitle));
        OnPropertyChanged(nameof(ActivePromptPreview));
        OnPropertyChanged(nameof(ActivePromptBorderColor));
        OnPropertyChanged(nameof(ActivePromptBorderThickness));
    }

    // ── Commands ────────────────────────────────────────────────────────────

    // ITEM 8 — active prompt card actions

    [RelayCommand]
    private void TogglePromptCardSelection()
    {
        IsPromptCardSelected = !IsPromptCardSelected;
        OnPropertyChanged(nameof(ActivePromptBorderColor));
        OnPropertyChanged(nameof(ActivePromptBorderThickness));
        OnPropertyChanged(nameof(ActivePromptZoneA));
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private async Task CopyActivePromptAsync()
    {
        if (_activePrompt is null) return;
        try
        {
            await _clipboard.SetTextAsync(_activePrompt.Content);
            IsActivePromptCopied = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Copy active prompt failed."); }
    }

    [RelayCommand]
    private void ToggleActivePromptExpand() =>
        IsActivePromptExpanded = !IsActivePromptExpanded;

    // ITEM 9 — quick prompt card actions

    [RelayCommand]
    private void ToggleQuickPromptSelection()
    {
        IsQuickPromptSelected = !IsQuickPromptSelected;
        OnPropertyChanged(nameof(QuickPromptBorderColor));
        OnPropertyChanged(nameof(QuickPromptZoneA));
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private async Task CopyQuickPromptAsync()
    {
        if (!HasQuickPromptText) return;
        try
        {
            await _clipboard.SetTextAsync(QuickPromptText);
            IsQuickPromptCopied = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Copy quick prompt failed."); }
    }

    [RelayCommand]
    private void ToggleQuickPromptExpand() =>
        IsQuickPromptExpanded = !IsQuickPromptExpanded;

    [RelayCommand]
    private void ClearQuickPrompt()
    {
        QuickPromptText       = string.Empty;
        IsQuickPromptSelected = false;
        IsQuickPromptCopied   = false;
    }

    // Chunk actions

    [RelayCommand]
    private async Task CopyChunkAsync(CodeChunk chunk)
    {
        if (chunk is null) return;
        try
        {
            // ITEM 8: prompt is NOT auto-prepended here anymore.
            // User copies the prompt card first (separately), then chunks one-by-one.
            await _clipboard.SetTextAsync(BuildChunkContent(chunk));
            foreach (var c in Chunks) c.IsCopied = false;
            chunk.IsCopied = true;
            CopiedCount    = Chunks.Count(c => c.IsCopied);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Copy failed."); }
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
        try { await _shareService.ShareTextAsync(BuildChunkContent(chunk), $"Chunk {chunk.Index + 1} · {chunk.ProjectName}"); }
        catch (Exception ex) { _logger.LogWarning(ex, "Share failed."); }
    }

    [RelayCommand] private void TogglePreview(CodeChunk chunk) { if (chunk is not null) chunk.IsPreviewExpanded = !chunk.IsPreviewExpanded; }
    [RelayCommand] private static void ToggleChunkSelection(CodeChunk chunk) { if (chunk is not null) chunk.IsSelected = !chunk.IsSelected; }
    [RelayCommand] private static void ToggleFileEntryExclusion(ChunkFile file) { if (file is not null) file.IsExcluded = !file.IsExcluded; }
    [RelayCommand] private static void ToggleFileCode(ChunkFile file) { if (file is not null) file.IsCodeExpanded = !file.IsCodeExpanded; }
    [RelayCommand] private void ClearChunkSearch() => ChunkSearchText = string.Empty;

    [RelayCommand]
    private void IncludeAllChunkFiles(CodeChunk chunk)
    { if (chunk is not null) foreach (var f in chunk.FileEntries) f.IsExcluded = false; }

    [RelayCommand]
    private void ExcludeAllChunkFiles(CodeChunk chunk)
    { if (chunk is not null) foreach (var f in chunk.FileEntries) f.IsExcluded = true; }

    [RelayCommand]
    private void SelectAllChunks()
    {
        bool allSelected = Chunks.Count > 0 && Chunks.All(c => c.IsSelected);
        foreach (var c in Chunks) c.IsSelected = !allSelected;
        // Also sweep the prompt and quick prompt cards
        IsPromptCardSelected  = !allSelected && HasActivePrompt;
        IsQuickPromptSelected = !allSelected && HasQuickPromptText;
        OnPropertyChanged(nameof(ActivePromptZoneA));
        OnPropertyChanged(nameof(QuickPromptZoneA));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ShareSelectedAsync()
    {
        var payload = AssembleCopyPayload();
        if (string.IsNullOrEmpty(payload)) return;
        var selectedChunks = Chunks.Where(c => c.IsSelected).ToList();
        var title = selectedChunks.Count == 0 ? "AI Prompt"
            : selectedChunks.Count == 1
                ? $"Chunk {selectedChunks[0].Index + 1} · {selectedChunks[0].ProjectName}"
                : $"{selectedChunks.Count} chunks";
        try { await _shareService.ShareTextAsync(payload, title); }
        catch (Exception ex) { _logger.LogWarning(ex, "Share selected failed."); }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopySelectedAsync()
    {
        var payload = AssembleCopyPayload();
        if (string.IsNullOrEmpty(payload)) return;
        try
        {
            await _clipboard.SetTextAsync(payload);
            var selectedChunks = Chunks.Where(c => c.IsSelected).ToList();
            foreach (var c in Chunks) c.IsCopied = false;
            foreach (var c in selectedChunks) c.IsCopied = true;
            CopiedCount          = Chunks.Count(c => c.IsCopied);
            IsActivePromptCopied = IsPromptCardSelected;
            IsQuickPromptCopied  = IsQuickPromptSelected;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Copy selected failed."); }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelection()
    {
        foreach (var c in Chunks) c.IsSelected = false;
        IsPromptCardSelected  = false;
        IsQuickPromptSelected = false;
        OnPropertyChanged(nameof(ActivePromptZoneA));
        OnPropertyChanged(nameof(QuickPromptZoneA));
    }

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

        for (int i = 0; i < Chunks.Count; i++) { Chunks[i].Index = i; Chunks[i].NotifyIndexChanged(); }
        SelectedCount = 0;
        RefreshFilteredChunks(forceRebuild: true);
        OnPropertyChanged(nameof(HasChunks));
        OnPropertyChanged(nameof(SelectAllChunksLabel));
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// ITEM 8 + 9: Assembles multi-select payload in order:
    ///   1. Active prompt (if IsPromptCardSelected)
    ///   2. Quick prompt text (if IsQuickPromptSelected + has text)
    ///   3. Selected chunk contents
    /// </summary>
    private string AssembleCopyPayload()
    {
        var sb = new StringBuilder();

        if (IsPromptCardSelected && _activePrompt is not null)
            sb.AppendLine(_activePrompt.Content);

        if (IsQuickPromptSelected && HasQuickPromptText)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine(QuickPromptText);
        }

        var selectedChunks = Chunks.Where(c => c.IsSelected).OrderBy(c => c.Index).ToList();
        foreach (var c in selectedChunks)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(BuildChunkContent(c));
        }

        return sb.ToString().TrimEnd();
    }

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

    private void RefreshFilteredChunks(bool forceRebuild = false)
    {
        var src = string.IsNullOrWhiteSpace(ChunkSearchText)
            ? Chunks
            : (IEnumerable<CodeChunk>)Chunks.Where(c =>
                c.DisplayLabel.Contains(ChunkSearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains(ChunkSearchText, StringComparison.OrdinalIgnoreCase));

        var newList = src.ToList();
        if (!forceRebuild && _filteredChunks.Count == newList.Count && _filteredChunks.SequenceEqual(newList))
            return;

        _filteredChunks.Clear();
        foreach (var c in newList) _filteredChunks.Add(c);

        OnPropertyChanged(nameof(HasFilteredChunks));
        OnPropertyChanged(nameof(ChunkSearchSummary));
    }

    private void SubscribeChunk(CodeChunk chunk)
    {
        PropertyChangedEventHandler h = (_, e) =>
        {
            if (e.PropertyName != nameof(CodeChunk.IsSelected)) return;
            SelectedCount = Chunks.Count(c => c.IsSelected);
            OnPropertyChanged(nameof(SelectAllChunksLabel));
            OnPropertyChanged(nameof(SelectedTokensLabel));
            OnPropertyChanged(nameof(CanMerge));
            OnPropertyChanged(nameof(HasSelection));
        };
        chunk.PropertyChanged += h;
        _chunkHandlers.Add((chunk, h));
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

    private void UnsubscribeAllChunks()
    {
        foreach (var (c, h) in _chunkHandlers) c.PropertyChanged -= h;
        _chunkHandlers.Clear();
    }

    public void Dispose()
    {
        UnsubscribeAllChunks();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
