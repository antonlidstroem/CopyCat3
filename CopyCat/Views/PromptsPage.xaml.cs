using CopyCat.ViewModels;

namespace CopyCat.Views;

public partial class PromptsPage : ContentPage
{
    private readonly PromptsViewModel _vm;

    public PromptsPage(PromptsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;

        // Wire Reset confirmation dialog
        ResetPromptsButton.Clicked += async (_, _) =>
        {
            bool ok = await DisplayAlert(
                "Reset all prompts",
                "Delete all custom prompts and restore the 6 built-in defaults?",
                "Reset", "Cancel");
            if (ok) await _vm.ResetPromptsCommand.ExecuteAsync(null);
        };

        // GoBackCommand raises this event; page responds by popping itself
        _vm.GoBackRequested += OnGoBackRequested;
    }

    private async void OnGoBackRequested(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        foreach (var p in _vm.Prompts)
        {
            p.IsEditing        = false;
            p.IsPreviewExpanded = false;
        }
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _vm.GoBackRequested -= OnGoBackRequested;
    }
}
