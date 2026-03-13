using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace CopyCat.Models;

// ── Persisted record (SQLite) ──────────────────────────────────────────────

[Table("Prompts")]
public class PromptRecord
{
    [PrimaryKey, AutoIncrement]
    public int    Id        { get; set; }
    public string Title     { get; set; } = string.Empty;
    public string Content   { get; set; } = string.Empty;
    public bool   IsBuiltIn { get; set; }
    public int    SortOrder { get; set; }
}

// ── Observable UI wrapper ──────────────────────────────────────────────────

public partial class PromptItem : ObservableObject
{
    public int  Id        { get; set; }
    public bool IsBuiltIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private string _content = string.Empty;

    // ── Edit-mode state ────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isEditing;
    [ObservableProperty] private string _editTitle   = string.Empty;
    [ObservableProperty] private string _editContent = string.Empty;

    // ── Full-text preview toggle ───────────────────────────────────────────

    /// <summary>
    /// When true the full prompt text is shown below the card title row.
    /// Toggled by the ▼/▲ chevron button — identical pattern to chunk file preview.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    // ── Copy feedback — card turns green ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    private bool _isCopied;

    // ── Single-select for share — card turns blue ─────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    private bool _isSelectedForShare;

    // ── Card colours: copied (green) > selected (blue) > default ──────────

    public Color CardBackgroundColor =>
        IsCopied           ? Color.FromArgb("#00A3A9") :
        IsSelectedForShare ? Color.FromArgb("#F59E0B") :
                             Color.FromArgb("#008C8B");

    public Color CardBorderColor =>
        IsCopied           ? Color.FromArgb("#F59E0B") :
        IsSelectedForShare ? Color.FromArgb("#006770") :
                             Color.FromArgb("#003B46");

    // ── Derived ───────────────────────────────────────────────────────────

    /// <summary>Two-line truncated preview shown in collapsed card view.</summary>
    public string PreviewText =>
        Content.Length > 160 ? Content[..160].TrimEnd() + "…" : Content;

    // ── Factory ───────────────────────────────────────────────────────────

    public static PromptItem FromRecord(PromptRecord r) => new()
    {
        Id = r.Id, Title = r.Title, Content = r.Content, IsBuiltIn = r.IsBuiltIn,
    };

    public PromptRecord ToRecord(int sortOrder = 0) => new()
    {
        Id = Id, Title = Title, Content = Content, IsBuiltIn = IsBuiltIn, SortOrder = sortOrder,
    };
}
