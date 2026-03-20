using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace CopyCat.Models;

/// <summary>
/// Observable wrapper around <see cref="SavedRepo"/> for the recent-repos list.
///
/// Keeps <see cref="SavedRepo"/> clean (no INPC) while giving the XAML
/// list an <see cref="IsExpanded"/> toggle and a precomputed
/// <see cref="WorkspaceSummary"/> one-liner.
/// </summary>
public partial class SavedRepoDisplayItem : ObservableObject
{
    public SavedRepo Repo { get; }

    public SavedRepoDisplayItem(SavedRepo repo)
    {
        Repo = repo;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    private bool _isExpanded;

    public string ExpandIcon => IsExpanded ? "▲" : "▼";

    // ── Forwarded identity helpers used by XAML ──────────────────────────────

    public string DisplayName   => Repo.DisplayName;
    public bool   HasWorkspace  => Repo.HasWorkspace;
    public bool   HasToken      => Repo.HasToken;
    public string Branch        => Repo.Branch;
    public int    Id            => Repo.Id;

    // ── Workspace summary (derived from JSON workspace fields) ───────────────

    /// <summary>
    /// One-line summary of the saved workspace settings.
    /// Example: ".cs .xaml .json  ·  bin obj  ·  16K tokens  ·  Code Review"
    /// Returns empty string when no workspace has been saved.
    /// </summary>
    public string WorkspaceSummary
    {
        get
        {
            if (!Repo.HasWorkspace) return string.Empty;

            var parts = new List<string>();

            // Enabled file extensions
            if (!string.IsNullOrEmpty(Repo.SavedEnabledExts))
            {
                try
                {
                    var exts = JsonSerializer.Deserialize<List<string>>(Repo.SavedEnabledExts);
                    if (exts?.Count > 0)
                    {
                        var display = exts.Count <= 4
                            ? string.Join(" ", exts)
                            : string.Join(" ", exts.Take(4)) + $" +{exts.Count - 4}";
                        parts.Add(display);
                    }
                }
                catch { /* ignore malformed JSON */ }
            }

            // Excluded folders
            if (!string.IsNullOrEmpty(Repo.SavedExcludedFolders))
            {
                try
                {
                    var folders = JsonSerializer.Deserialize<List<string>>(Repo.SavedExcludedFolders);
                    if (folders?.Count > 0)
                    {
                        var display = folders.Count <= 3
                            ? string.Join(" ", folders)
                            : string.Join(" ", folders.Take(3)) + $" +{folders.Count - 3}";
                        parts.Add($"excl: {display}");
                    }
                }
                catch { /* ignore */ }
            }

            // Token limit
            if (Repo.SavedMaxTokens > 0)
                parts.Add($"{Repo.SavedMaxTokens / 1000}K tokens");

            return parts.Count > 0
                ? string.Join("  ·  ", parts)
                : "Workspace saved";
        }
    }
}
