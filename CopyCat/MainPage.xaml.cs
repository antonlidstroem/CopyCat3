using CopyCat.Services;
using CopyCat.ViewModels;

namespace CopyCat;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.InitializeAsync();

        if (!string.IsNullOrWhiteSpace(SharedUrlService.PendingUrl))
        {
            _viewModel.RepoUrl          = SharedUrlService.PendingUrl;
            SharedUrlService.PendingUrl = null;
        }
    }
}
