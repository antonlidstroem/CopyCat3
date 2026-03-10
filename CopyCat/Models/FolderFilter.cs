using CommunityToolkit.Mvvm.ComponentModel;
namespace CopyCat.Models;
public partial class FolderFilter : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    [ObservableProperty]
    private bool _isExcluded;
}
