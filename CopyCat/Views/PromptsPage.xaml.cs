using CopyCat.ViewModels;

namespace CopyCat.Views;

/// <summary>
/// PromptsPage code-behind.
///
/// FIX: Constructor now accepts <see cref="MainViewModel"/> because the XAML
/// has x:DataType="vm:MainViewModel" and all bindings go through Prompts.Xxx.
/// If BindingContext were PromptsViewModel, the compiled bindings would fail
/// silently — the cast would succeed at runtime only if the type matched.
///
/// Internal operations (GoBackRequested, ResetPromptsCommand, Prompts list)
/// are accessed via _vm which holds the PromptsViewModel reference.
/// </summary>
public partial class PromptsPage : ContentPage
{
    private readonly PromptsViewModel _vm;

    public PromptsPage(MainViewModel viewModel)
    {
        InitializeComponent();

        // BindingContext must be MainViewModel — XAML binds through Prompts.Xxx
        BindingContext = viewModel;

        // Internal operations use PromptsViewModel directly
        _vm = viewModel.Prompts;

        // Wire Reset confirmation dialog
        ResetPromptsButton.Clicked += async (_, _) =>
        {
            bool ok = await DisplayAlert(
                "Reset all prompts",
                "Delete all custom prompts and restore the 8 built-in defaults?",
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
            p.IsEditing         = false;
            p.IsPreviewExpanded = false;
        }
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _vm.GoBackRequested -= OnGoBackRequested;
    }
}
