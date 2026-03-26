using CopyCat.ViewModels;

namespace CopyCat.Views;

public partial class PromptsPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public PromptsPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        _viewModel.GoBackRequested += OnGoBackRequested;

        // Reset button wired in code-behind (requires confirmation dialog)
        ResetPromptsButton.Clicked += async (_, _) =>
        {
            bool ok = await DisplayAlert(
                "Reset all prompts",
                "Delete all custom prompts and restore the 6 built-in defaults?",
                "Reset", "Cancel");
            if (ok) await _viewModel.ResetPromptsCommand.ExecuteAsync(null);
        };
    }

    private async void OnGoBackRequested(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        foreach (var p in _viewModel.Prompts)
        {
            p.IsEditing         = false;
            p.IsPreviewExpanded = false;
            p.IsBuilderMode     = false;
        }
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _viewModel.GoBackRequested -= OnGoBackRequested;
    }
}
