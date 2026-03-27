using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

public partial class FileTypeFilter : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private bool _isEnabled;

    // Guard against null — always initialize to empty list
    private List<string> _extensions = [];
    public List<string> Extensions
    {
        get => _extensions;
        set => _extensions = value ?? [];
    }
}