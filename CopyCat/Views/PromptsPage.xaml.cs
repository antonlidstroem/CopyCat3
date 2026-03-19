using CopyCat.ViewModels;

namespace CopyCat.Views;

public partial class PromptsPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public PromptsPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Wire GoBack event so the "← Done" button can navigate back
        _viewModel.GoBackRequested += OnGoBackRequested;

        // Reset button wired here (not in MainPage) to keep concerns separated
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

        // Collapse any open editors / previews when leaving the page
        foreach (var p in _viewModel.Prompts)
        {
            p.IsEditing         = false;
            p.IsPreviewExpanded = false;
        }
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // Unsubscribe to avoid memory leak if page is GC'd while event is live
        _viewModel.GoBackRequested -= OnGoBackRequested;
    }
}
