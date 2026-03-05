using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

/// <summary>Representerar en filtyp som kan inkluderas vid hämtning.</summary>
public partial class FileTypeFilter : ObservableObject
{
    /// <summary>Visningstext på chip:en, t.ex. ".cs" eller ".js".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>En eller flera filändelser, t.ex. [".ts", ".tsx"].</summary>
    public List<string> Extensions { get; set; } = [];

    [ObservableProperty]
    private bool _isEnabled;
}
