using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CopyCat.Messages;
using CopyCat.Models;
using CopyCat.Models.Catalog;
using CopyCat.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CopyCat.ViewModels;

/// <summary>
/// Owns the AI prompt library: selection, CRUD, and navigation to PromptsPage.
///
/// CHANGES IN THIS VERSION
/// ───────────────────────
/// ITEM 7 — Guided prompt builder:
///   • Builder property — per-edit-session PromptBuilderState.
///   • ApplyBuilderCommand — assembles the builder output into EditContent.
///   • StartEditPrompt creates a fresh PromptBuilderState each time.
/// </summary>
public partial class PromptsViewModel : ObservableObject,
    IRecipient<RepoSelectedMessage>
{
    private readonly IPromptRepository           _promptRepo;
    private readonly ILogger<PromptsViewModel>   _logger;

    private readonly List<(PromptItem Prompt, PropertyChangedEventHandler Handler)> _promptHandlers = [];

    public event EventHandler? NavigateToPromptsPageRequested;
    public event EventHandler? GoBackRequested;

    public PromptsViewModel(
        IPromptRepository         promptRepo,
        ILogger<PromptsViewModel> logger)
    {
        _promptRepo = promptRepo;
        _logger     = logger;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    // ── Collections ────────────────────────────────────────────────────────

    public ObservableCollection<PromptItem> Prompts { get; } = [];

    // ── Observable / Computed ───────────────────────────────────────────────

    [ObservableProperty] private bool _isPromptsExpanded = true;

    // ITEM 7 — per-session builder state (recreated in StartEditPrompt)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuilder))]
    private PromptBuilderState? _builder;

    public bool HasBuilder => Builder is not null;

    public PromptItem? SelectedPrompt    => Prompts.FirstOrDefault(p => p.IsSelectedForShare);
    public bool        HasSelectedPrompt => SelectedPrompt is not null;
    public string      SelectedPromptLabel =>
        SelectedPrompt is null
            ? "No prompt selected — tap ⚙ Prompts to choose one"
            : $"✓  {SelectedPrompt.Title}";

    // ── Initialisation ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try { await _promptRepo.InitializeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Prompt DB init failed."); }
        await RefreshPromptsAsync();
    }

    // ── Messenger ───────────────────────────────────────────────────────────

    public void Receive(RepoSelectedMessage message)
    {
        var sortOrder = message.Repo.SavedPromptSortOrder;
        if (sortOrder < 0) return;
        var prompt = Prompts.FirstOrDefault(p => p.OriginalSortOrder == sortOrder);
        if (prompt is null) return;
        foreach (var p in Prompts) p.IsSelectedForShare = false;
        prompt.IsSelectedForShare = true;
        NotifyPromptChanged();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectPromptForShare(PromptItem prompt)
    {
        if (prompt is null) return;
        var was = prompt.IsSelectedForShare;
        foreach (var p in Prompts) p.IsSelectedForShare = false;
        prompt.IsSelectedForShare = !was;
        NotifyPromptChanged();
    }

    [RelayCommand]
    private void ClearSelectedPrompt()
    {
        foreach (var p in Prompts) p.IsSelectedForShare = false;
        NotifyPromptChanged();
    }

    [RelayCommand]
    private async Task CopyPromptAsync(PromptItem prompt)
    {
        if (prompt is null) return;
        try
        {
            await Clipboard.Default.SetTextAsync(prompt.Content);
            foreach (var p in Prompts) p.IsCopied = false;
            prompt.IsCopied = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not copy prompt."); }
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
        prompt.EditTitle         = prompt.Title;
        prompt.EditContent       = prompt.Content;
        prompt.IsEditing         = true;

        // ITEM 7: Create a fresh builder state for this edit session
        Builder = new PromptBuilderState();
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
        Builder          = null; // discard builder state
        try
        {
            var r = await _promptRepo.UpsertPromptAsync(prompt.ToRecord(Prompts.IndexOf(prompt)));
            prompt.Id = r.Id;
            NotifyPromptChanged();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not save prompt."); }
    }

    [RelayCommand]
    private void CancelEditPrompt(PromptItem prompt)
    {
        if (prompt is null) return;
        prompt.IsEditing = false;
        Builder          = null;
        if (prompt.Id == 0) { UnsubscribePrompt(prompt); Prompts.Remove(prompt); }
    }

    [RelayCommand]
    private async Task DeletePromptAsync(PromptItem prompt)
    {
        if (prompt is null || prompt.IsBuiltIn) return;
        try
        {
            await _promptRepo.DeletePromptAsync(prompt.Id);
            UnsubscribePrompt(prompt);
            Prompts.Remove(prompt);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete prompt."); }
    }

    [RelayCommand]
    private void AddNewPrompt()
    {
        foreach (var p in Prompts) { p.IsPreviewExpanded = false; p.IsEditing = false; }
        var item = new PromptItem { IsEditing = true, EditTitle = "New Prompt", EditContent = "" };
        SubscribePrompt(item);
        Prompts.Add(item);
        // ITEM 7: fresh builder for new prompts too
        Builder = new PromptBuilderState();
    }

    [RelayCommand]
    private async Task ResetPromptsAsync()
    {
        try { await _promptRepo.ResetPromptsToDefaultAsync(); await RefreshPromptsAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not reset prompts."); }
    }

    [RelayCommand]
    private async Task ResetSinglePromptAsync(PromptItem prompt)
    {
        if (prompt is null || !prompt.IsBuiltIn) return;
        if (!BuiltInPrompts.BySortOrder.TryGetValue(prompt.OriginalSortOrder, out var seed)) return;
        prompt.Title     = seed.Title;
        prompt.Content   = seed.Content;
        prompt.IsEditing = false;
        try { await _promptRepo.UpsertPromptAsync(prompt.ToRecord(Prompts.IndexOf(prompt))); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not reset prompt."); }
    }

    /// <summary>
    /// ITEM 7 — Takes the assembled prompt from the builder and inserts it
    /// into the currently editing prompt's EditContent field.
    /// The builder state is preserved so the user can keep adjusting.
    /// </summary>
    [RelayCommand]
    private void ApplyBuilderToEditContentCommand(PromptItem prompt)
    {
        if (prompt is null || Builder is null) return;
        var assembled = Builder.AssemblePrompt();
        if (assembled is not null)
            prompt.EditContent = assembled;
    }

    [RelayCommand] private void TogglePrompts() => IsPromptsExpanded = !IsPromptsExpanded;

    [RelayCommand]
    private void NavigateToPromptsPage() =>
        NavigateToPromptsPageRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void GoBack() => GoBackRequested?.Invoke(this, EventArgs.Empty);

    // ── Helpers ─────────────────────────────────────────────────────────────

    public async Task RefreshPromptsAsync()
    {
        try
        {
            UnsubscribeAllPrompts();
            Prompts.Clear();
            foreach (var r in await _promptRepo.GetPromptsAsync())
            {
                var item = PromptItem.FromRecord(r);
                SubscribePrompt(item);
                Prompts.Add(item);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not load prompts."); }
    }

    private void NotifyPromptChanged()
    {
        OnPropertyChanged(nameof(SelectedPrompt));
        OnPropertyChanged(nameof(SelectedPromptLabel));
        OnPropertyChanged(nameof(HasSelectedPrompt));
        WeakReferenceMessenger.Default.Send(new PromptSelectionChangedMessage(SelectedPrompt));
    }

    private void SubscribePrompt(PromptItem prompt)
    {
        PropertyChangedEventHandler h = (_, e) =>
        {
            if (e.PropertyName == nameof(PromptItem.IsSelectedForShare))
                NotifyPromptChanged();
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

    public void Dispose()
    {
        UnsubscribeAllPrompts();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
