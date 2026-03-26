using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>
/// A node in the hierarchical file/folder tree browser (Phase 5).
///
/// Visual rules:
///   IsFolder = true  → shows 📁/📂 icon; tapping expands/collapses
///   IsFolder = false → shows ○/✕ icon; tapping toggles file exclusion
///
/// Exclusion propagation:
///   When a folder is excluded all descendant nodes are also marked excluded.
///   When a child is individually toggled, the parent folder shows ⊖ (IsPartiallyExcluded).
///
/// FlatFileTree in the ViewModel is a filtered collection of only visible nodes
/// (ancestors all expanded). It is rebuilt on every expand/collapse event.
/// </summary>
public partial class FileTreeNode : ObservableObject
{
    // ── Identity ──────────────────────────────────────────────────────────

    /// <summary>Display name: folder or file name without path prefix.</summary>
    public string Name     { get; set; } = string.Empty;

    /// <summary>Full relative path from the repo root, e.g. "src/App/MainPage.cs".</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>True for folder nodes; false for file (leaf) nodes.</summary>
    public bool IsFolder   { get; set; }

    /// <summary>Zero-based nesting depth. Used to compute left-indent in the list.</summary>
    public int  Depth      { get; set; }

    /// <summary>File content — populated for file nodes, empty for folders.</summary>
    public string Content  { get; set; } = string.Empty;

    // ── Tree structure ─────────────────────────────────────────────────────

    public List<FileTreeNode> Children { get; set; } = [];
    public FileTreeNode?      Parent   { get; set; }

    // ── Observable state ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NodeIcon))]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private bool _isExcluded;

    /// <summary>
    /// True when some but not all children are excluded (folder nodes only).
    /// Shows ⊖ to indicate a partial-exclusion state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludeIcon))]
    [NotifyPropertyChangedFor(nameof(ExcludeIconColor))]
    private bool _isPartiallyExcluded;

    // ── Derived visual properties ──────────────────────────────────────────

    /// <summary>Left indentation in pixels per nesting level.</summary>
    public double IndentWidth => Depth * 16.0;

    /// <summary>Folder icon changes with expansion state; files have no icon (use ExcludeIcon).</summary>
    public string NodeIcon =>
        !IsFolder ? string.Empty :
        IsExpanded ? "📂" : "📁";

    /// <summary>
    /// ○  — included (default)
    /// ✕  — excluded (IsExcluded=true)
    /// ⊖  — partially excluded (folder with mixed children)
    /// </summary>
    public string ExcludeIcon =>
        IsExcluded          ? "✕" :
        IsPartiallyExcluded ? "⊖" : "○";

    /// <summary>Red when excluded, amber when partial, dim teal when included.</summary>
    public Color ExcludeIconColor =>
        IsExcluded          ? Color.FromArgb("#EF4444") :
        IsPartiallyExcluded ? Color.FromArgb("#F59E0B") :
                              Color.FromArgb("#1A3D4A");

    /// <summary>Row background — red tint when excluded.</summary>
    public Color RowBackground =>
        IsExcluded ? Color.FromArgb("#2A0D0D") : Colors.Transparent;

    /// <summary>Child count hint shown beside folder names.</summary>
    public string ChildCountLabel =>
        IsFolder && Children.Count > 0 ? $"({Children.Count})" : string.Empty;

    /// <summary>Convenience accessor for file name without directory.</summary>
    public string FileName =>
        IsFolder ? string.Empty :
        System.IO.Path.GetFileName(FullPath.Replace('\\', '/'));
}
