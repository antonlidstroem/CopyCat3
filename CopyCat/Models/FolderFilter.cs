using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>Representerar en mapp som kan exkluderas vid hämtning.</summary>
public partial class FolderFilter : ObservableObject
{
    /// <summary>Mappnamnet, t.ex. "bin" eller "node_modules".</summary>
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isExcluded;
}
