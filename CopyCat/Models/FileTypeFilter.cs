using CommunityToolkit.Mvvm.ComponentModel;
namespace CopyCat.Models;
public partial class FileTypeFilter : ObservableObject
{
    public string Label { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = [];
    [ObservableProperty]
    private bool _isEnabled;
}
