using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace CopyCat.Models;

/// <summary>
/// Represents a reusable XML tag button in the AI Prompt Builder.
///
/// Built-in buttons are seeded by BuiltInXmlTags and marked IsBuiltIn=true.
/// Custom buttons are created by the user and persisted in SQLite.
///
/// Tapping a button in the prompt editor appends the open/close tag pair
/// with a placeholder into the prompt's EditContent field.
///
/// Special case: the [PASTE CHUNK] button has empty XmlOpen/XmlClose and
/// inserts the literal "[PASTE CHUNK]" placeholder directly.
/// </summary>
[Table("XmlTagButtons")]
public partial class XmlTagButton : ObservableObject
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Short display label shown on the chip button, e.g. "📌 Role".</summary>
    [ObservableProperty] private string _label = string.Empty;

    /// <summary>Opening XML tag, e.g. "&lt;role&gt;". Empty for [PASTE CHUNK] button.</summary>
    [ObservableProperty] private string _xmlOpen = string.Empty;

    /// <summary>Closing XML tag, e.g. "&lt;/role&gt;". Empty for [PASTE CHUNK] button.</summary>
    [ObservableProperty] private string _xmlClose = string.Empty;

    /// <summary>Default placeholder text inserted between the tags.</summary>
    [ObservableProperty] private string _placeholder = string.Empty;

    /// <summary>True for the 7 seeds from BuiltInXmlTags; false for user-created buttons.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Display order in the palette. Built-ins use 0–6.</summary>
    public int SortOrder { get; set; }

    // ── Derived ───────────────────────────────────────────────────────────

    /// <summary>True for user-created custom tags that can be deleted.</summary>
    [Ignore]
    public bool IsCustom => !IsBuiltIn;

    /// <summary>
    /// Text fragment appended to the prompt editor when this button is tapped.
    /// Handles the special [PASTE CHUNK] case where no XML wrapping is needed.
    /// </summary>
    [Ignore]
    public string Insertion
    {
        get
        {
            if (string.IsNullOrEmpty(XmlOpen))
                return $"\n{Placeholder}";
            return $"\n{XmlOpen}\n{Placeholder}\n{XmlClose}\n";
        }
    }
}
