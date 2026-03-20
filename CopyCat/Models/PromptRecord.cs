using SQLite;

namespace CopyCat.Models;

/// <summary>
/// SQLite database record for an AI prompt.
///
/// Kept intentionally separate from <see cref="PromptItem"/> (the
/// observable UI model) to honour the Single Responsibility Principle:
///   • <see cref="PromptRecord"/> — knows about the DB schema.
///   • <see cref="PromptItem"/>   — knows about XAML binding and UI state.
///
/// The mapping between them lives in <see cref="PromptItem.FromRecord"/>
/// and <see cref="PromptItem.ToRecord"/>, keeping both classes clean.
/// </summary>
[Table("Prompts")]
public class PromptRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Display title shown on the prompt card.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full prompt body; may contain the [PASTE CHUNK] placeholder.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Stable sort key that survives edits.
    /// Built-in prompts keep the same OriginalSortOrder forever so the
    /// workspace-restore feature can re-select the right prompt after a reset.
    /// Custom prompts use sort orders ≥ 100.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// True for the six factory-default prompts.
    /// Built-in prompts cannot be deleted, only reset to their default text.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>True when the user has modified a built-in prompt's text.</summary>
    public bool IsModified { get; set; }
}
