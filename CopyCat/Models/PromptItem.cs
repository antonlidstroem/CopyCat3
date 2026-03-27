using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace CopyCat.Models;

// ── Persisted record (SQLite) ──────────────────────────────────────────────



// ── Observable UI wrapper ──────────────────────────────────────────────────

public partial class PromptItem : ObservableObject
{
    public int  Id        { get; set; }
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// The SortOrder this prompt was seeded with (0–5 for built-ins).
    /// Used to look up the factory content in <see cref="Services.BuiltInPrompts"/>
    /// for reliable single-prompt reset — independent of what is saved in SQLite.
    /// </summary>
    public int OriginalSortOrder { get; init; } = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    [NotifyPropertyChangedFor(nameof(IsModifiedOrCustom))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    [NotifyPropertyChangedFor(nameof(IsModifiedOrCustom))]
    private string _content = string.Empty;

    // ── Edit-mode state ────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isEditing;
    [ObservableProperty] private string _editTitle   = string.Empty;
    [ObservableProperty] private string _editContent = string.Empty;

    // ── Phase 4: XML Prompt Builder mode ──────────────────────────────────

    /// <summary>
    /// When true, the prompt edit panel shows the XML tag button palette
    /// instead of (or alongside) the raw freehand editor.
    /// Freehand editing is always possible — the builder just appends tag
    /// snippets to EditContent via ViewModel.AppendXmlTagCommand.
    /// </summary>
    [ObservableProperty] private bool _isBuilderMode;

    // ── Full-text preview toggle ───────────────────────────────────────────

    /// <summary>
    /// Only one prompt preview should be open at a time.
    /// MainViewModel.TogglePromptPreviewCommand enforces mutual exclusion.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    // ── Copy feedback ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderThickness))]
    [NotifyPropertyChangedFor(nameof(ZoneABackground))]
    private bool _isCopied;

    // ── Single-select for share ────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(ZoneABackground))]
    private bool _isSelectedForShare;

    // ── Visual distinction: pristine built-in vs modified/custom ──────────

    /// <summary>
    /// True when this prompt has been edited from its factory default,
    /// or when it is user-created.  Controls card border color:
    ///   false (pristine built-in)  → quiet teal border
    ///   true  (modified or custom) → amber accent border
    /// </summary>
    public bool IsModifiedOrCustom
    {
        get
        {
            if (!IsBuiltIn) return true;   // user-created
            if (OriginalSortOrder < 0)     return false;  // unknown seed → treat as pristine
            if (!Services.BuiltInPrompts.BySortOrder.TryGetValue(OriginalSortOrder, out var seed))
                return false;
            return Title != seed.Title || Content != seed.Content;
        }
    }

    // ── C2: Three-zone card colours ───────────────────────────────────────

    /// <summary>Zone A (select area) background color.</summary>
    public Color ZoneABackground =>
        IsSelectedForShare ? Color.FromArgb("#1C1406") : Color.FromArgb("#0D2128");

    /// <summary>Full card border color — teal when copied, type-based otherwise.</summary>
    public Color CardBorderColor =>
        IsCopied ? Color.FromArgb("#00B4BC") :
        IsModifiedOrCustom ? Color.FromArgb("#F59E0B") :
                             Color.FromArgb("#1A3D4A");

    /// <summary>Card border thickness — heavier when copied to signal completion.</summary>
    public double CardBorderThickness => IsCopied ? 2.5 : 1.5;

    /// <summary>Legacy background property kept for compatibility.</summary>
    public Color CardBackgroundColor =>
        IsCopied           ? Color.FromArgb("#061A1B") :
        IsSelectedForShare ? Color.FromArgb("#1A1406") :
                             Color.FromArgb("#0D2128");

    // ── Derived ───────────────────────────────────────────────────────────

    public string PreviewText =>
        Content.Length > 160 ? Content[..160].TrimEnd() + "…" : Content;

    // ── Factory ───────────────────────────────────────────────────────────

    public static PromptItem FromRecord(PromptRecord r) => new()
    {
        Id               = r.Id,
        Title            = r.Title,
        Content          = r.Content,
        IsBuiltIn        = r.IsBuiltIn,
        OriginalSortOrder = r.IsBuiltIn ? r.SortOrder : -1,
    };

    public PromptRecord ToRecord(int sortOrder = 0) => new()
    {
        Id        = Id,
        Title     = Title,
        Content   = Content,
        IsBuiltIn = IsBuiltIn,
        SortOrder = sortOrder,
    };
}
