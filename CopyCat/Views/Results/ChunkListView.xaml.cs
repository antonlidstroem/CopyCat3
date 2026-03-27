using CopyCat.ViewModels;

namespace CopyCat.Views.Results;

public partial class ChunkListView : ContentView
{
    public ChunkListView(ChunkListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}