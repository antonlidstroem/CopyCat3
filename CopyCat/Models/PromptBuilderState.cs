using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyCat.Models.Catalog;
using System.Collections.ObjectModel;
using System.Text;

namespace CopyCat.Models;

/// <summary>
/// Non-persisted, session-scoped state for the guided prompt builder panel.
///
/// Lives on <c>PromptsViewModel</c> and is reset each time the user opens
/// a new edit session. Calling <see cref="AssemblePrompt"/> produces the
/// final string that is dropped into <c>PromptItem.EditContent</c>.
///
/// The round-trip is intentionally ONE-WAY: free-text edits in EditContent
/// do not sync back here. Once the user edits the assembled text, the builder
/// resets to indicate "custom mode".
/// </summary>
public partial class PromptBuilderState : ObservableObject
{
    // ── Role checkboxes ──────────────────────────────────────────────────────

    /// <summary>Each entry is one role chip — observable for two-way checkbox binding.</summary>
    public ObservableCollection<RoleSelectionItem> Roles { get; } =
        new(PromptRoleCatalog.All.Select(r => new RoleSelectionItem(r)));

    // ── Output format (single-select chips) ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(XmlSkeletonPreview))]
    private string _selectedOutputFormatId = "free";

    // ── Builder visibility ────────────────────────────────────────────────────

    /// <summary>Whether the guided builder panel is shown (vs. pure free-text mode).</summary>
    [ObservableProperty] private bool _isBuilderVisible;

    /// <summary>Whether at least one role is selected.</summary>
    public bool HasSelectedRoles => Roles.Any(r => r.IsSelected);

    // ── Live XML skeleton preview ─────────────────────────────────────────────

    /// <summary>
    /// Read-only preview of the XML structure that will be injected.
    /// Updates reactively as roles are toggled.
    /// </summary>
    public string XmlSkeletonPreview
    {
        get
        {
            var selected = Roles.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0)
                return "Select at least one role above to see the prompt skeleton.";

            var sb = new StringBuilder();
            foreach (var role in selected)
            {
                sb.AppendLine($"<{role.Definition.XmlTag}>");
                sb.AppendLine($"  {role.Definition.DefaultInstruction}");
                sb.AppendLine($"</{role.Definition.XmlTag}>");
                sb.AppendLine();
            }

            var fmt = PromptRoleCatalog.OutputFormats
                .FirstOrDefault(f => f.Id == SelectedOutputFormatId);
            if (!string.IsNullOrEmpty(fmt.Instruction))
            {
                sb.AppendLine($"<output_format>");
                sb.AppendLine($"  {fmt.Instruction}");
                sb.AppendLine($"</output_format>");
                sb.AppendLine();
            }

            sb.Append("[PASTE CHUNK]");
            return sb.ToString();
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectOutputFormat(string id)
    {
        SelectedOutputFormatId = id;
        OnPropertyChanged(nameof(XmlSkeletonPreview));
    }

    [RelayCommand]
    private void ToggleRole(RoleSelectionItem item)
    {
        if (item is null) return;
        item.IsSelected = !item.IsSelected;
        OnPropertyChanged(nameof(HasSelectedRoles));
        OnPropertyChanged(nameof(XmlSkeletonPreview));
    }

    [RelayCommand]
    private void ToggleBuilder() => IsBuilderVisible = !IsBuilderVisible;

    /// <summary>Resets all role selections and output format to defaults.</summary>
    [RelayCommand]
    private void ResetBuilder()
    {
        foreach (var r in Roles) r.IsSelected = false;
        SelectedOutputFormatId = "free";
        OnPropertyChanged(nameof(HasSelectedRoles));
        OnPropertyChanged(nameof(XmlSkeletonPreview));
    }

    // ── Assembly ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces the final prompt string from current role selections and format.
    /// Returns null if no roles are selected.
    /// </summary>
    public string? AssemblePrompt()
    {
        var selected = Roles.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return null;

        var sb = new StringBuilder();

        if (selected.Count == 1)
        {
            // Single role: no wrapper, just the instruction
            sb.AppendLine(selected[0].Definition.DefaultInstruction);
        }
        else
        {
            // Multiple roles: XML-delimited sections
            sb.AppendLine("Please analyse the following code from multiple expert perspectives.");
            sb.AppendLine();
            foreach (var role in selected)
            {
                sb.AppendLine($"<{role.Definition.XmlTag}>");
                sb.AppendLine(role.Definition.DefaultInstruction);
                sb.AppendLine($"</{role.Definition.XmlTag}>");
                sb.AppendLine();
            }
        }

        var fmt = PromptRoleCatalog.OutputFormats
            .FirstOrDefault(f => f.Id == SelectedOutputFormatId);
        if (!string.IsNullOrEmpty(fmt.Instruction))
        {
            sb.AppendLine();
            sb.AppendLine(fmt.Instruction);
        }

        sb.AppendLine();
        sb.Append("[PASTE CHUNK]");
        return sb.ToString();
    }
}

/// <summary>Observable wrapper around a <see cref="PromptRoleCatalog.RoleDefinition"/>
/// that adds an <see cref="IsSelected"/> toggle for XAML binding.</summary>
public partial class RoleSelectionItem : ObservableObject
{
    public PromptRoleCatalog.RoleDefinition Definition { get; }

    public RoleSelectionItem(PromptRoleCatalog.RoleDefinition definition)
    {
        Definition = definition;
    }

    [ObservableProperty] private bool _isSelected;

    public string Label       => Definition.Label;
    public string Icon        => Definition.Icon;
    public string Description => Definition.Description;
    public string ChipLabel   => $"{Definition.Icon}  {Definition.Label}";
}
