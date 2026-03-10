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
    public int    Id        { get; set; }
    public bool   IsBuiltIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private string _content = string.Empty;

    // ── Edit-mode state ────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    private string _editContent = string.Empty;

    // ── Copy feedback ──────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isCopied;

    // ── Derived ───────────────────────────────────────────────────────────

    public string PreviewText =>
        Content.Length > 160 ? Content[..160].TrimEnd() + "…" : Content;

    // ── Factory ───────────────────────────────────────────────────────────

    public static PromptItem FromRecord(PromptRecord r) =>
        new()
        {
            Id        = r.Id,
            Title     = r.Title,
            Content   = r.Content,
            IsBuiltIn = r.IsBuiltIn,
        };

    public PromptRecord ToRecord(int sortOrder = 0) =>
        new()
        {
            Id        = Id,
            Title     = Title,
            Content   = Content,
            IsBuiltIn = IsBuiltIn,
            SortOrder = sortOrder,
        };
}
