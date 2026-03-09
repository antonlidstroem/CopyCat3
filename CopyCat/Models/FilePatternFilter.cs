using CommunityToolkit.Mvvm.ComponentModel;

namespace CopyCat.Models;

public partial class FilePatternFilter : ObservableObject
{
    public string Pattern { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;
}
