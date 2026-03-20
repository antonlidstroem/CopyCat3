using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CopyCat.Models;

/// <summary>
/// Observable UI model for an AI prompt card in the Prompts library.
///
/// Responsibilities
/// ────────────────
/// • Holds all mutable UI state (editing, selection, preview expand).
/// • Exposes computed display helpers that XAML can bind to directly.
/// • Provides <see cref="FromRecord"/> and <see cref="ToRecord"/> to
///   translate to/from the <see cref="PromptRecord"/> DB entity.
///
/// Why not inherit from PromptRecord?
/// ───────────────────────────────────
/// SQLite-net maps every public property it finds.  Mixing in
/// ObservableProperty backing fields and UI-only computed properties
/// would pollute the DB schema with phantom columns or require [Ignore]
/// on every UI property — error-prone and noisy.  Keeping the two
/// classes separate is cleaner and explicit.
/// </summary>
public partial class PromptItem : ObservableObject
{
    // ── DB identity (round-trips through PromptRecord) ────────────────────────

    public int Id                { get; set; }
    public int OriginalSortOrder { get; set; }
    public bool IsBuiltIn        { get; set; }

    // ── Observable data fields ────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModifiedOrCustom))]
    [NotifyPropertyChangedFor(nameof(CardBorderThickness))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    [NotifyPropertyChangedFor(nameof(IsModifiedOrCustom))]
    private string _content = string.Empty;

    // ── UI state ─────────────────────────────────────────────────────────────

    /// <summary>Whether this prompt is the active selection prepended to every copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoneABackground))]
    private bool _isSelectedForShare;

    /// <summary>Whether the inline edit panel is open.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Whether the full-text preview strip is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    /// <summary>Whether the clipboard-copy flash animation is showing.</summary>
    [ObservableProperty]
    private bool _isCopied;

    /// <summary>Editable title scratch pad (discarded if user hits Cancel).</summary>
    [ObservableProperty]
    private string _editTitle = string.Empty;

    /// <summary>Editable content scratch pad (discarded if user hits Cancel).</summary>
    [ObservableProperty]
    private string _editContent = string.Empty;

    // ── Computed display helpers ──────────────────────────────────────────────

    /// <summary>
    /// True when the prompt has been modified from its built-in default,
    /// or is a user-created custom prompt.
    /// Drives the amber border on modified cards.
    /// </summary>
    public bool IsModifiedOrCustom => !IsBuiltIn;

    /// <summary>First ~80 characters of content for the card preview line.</summary>
    public string PreviewText =>
        Content.Length <= 80 ? Content : Content[..80].TrimEnd() + "…";

    /// <summary>Icon on the Zone C preview expand strip.</summary>
    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    /// <summary>Zone A background tint when the prompt is selected.</summary>
    public Color ZoneABackground =>
        IsSelectedForShare
            ? Color.FromArgb("#0D2A2B")   // teal tint
            : Colors.Transparent;

    /// <summary>Border thickness — thicker on modified/custom prompts.</summary>
    public double CardBorderThickness => IsModifiedOrCustom ? 1.5 : 0.8;

    // ── Mapping ───────────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="PromptItem"/> from a DB record.</summary>
    public static PromptItem FromRecord(PromptRecord r) => new()
    {
        Id                = r.Id,
        OriginalSortOrder = r.SortOrder,
        IsBuiltIn         = r.IsBuiltIn,
        Title             = r.Title,
        Content           = r.Content,
    };

    /// <summary>Converts this item back to a DB record for persistence.</summary>
    public PromptRecord ToRecord(int currentIndex) => new()
    {
        Id         = Id,
        Title      = Title.Trim(),
        Content    = Content.Trim(),
        SortOrder  = OriginalSortOrder > 0 ? OriginalSortOrder : currentIndex + 100,
        IsBuiltIn  = IsBuiltIn,
        IsModified = IsBuiltIn && (Title != Content), // conservative flag
    };
}
