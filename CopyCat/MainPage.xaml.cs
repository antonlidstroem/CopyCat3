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

        // Branch picker: ViewModel signals us to open a native action sheet
        _viewModel.BranchPickerRequested += async (_, branches) =>
        {
            var result = await DisplayActionSheet(
                "Select branch", "Cancel", null, [.. branches]);
            if (result is not null and not "Cancel")
                _viewModel.Branch = result;
        };

        // GitHub token info dialog
        _viewModel.TokenInfoRequested += async (_, _) =>
        {
            await DisplayAlert(
                "GitHub Personal Access Token",
                "A token lets CopyCat access private repositories and avoids rate limits.\n\n" +
                "How to create one:\n" +
                "1. Go to github.com → Profile → Settings\n" +
                "2. Developer settings → Personal access tokens → Fine-grained tokens\n" +
                "3. Click \"Generate new token\"\n" +
                "4. Under Repository permissions, set Contents → Read-only\n" +
                "5. Copy the generated token (starts with github_pat_…) and paste it here.\n\n" +
                "The token is stored securely on your device and is never sent to any server other than GitHub.",
                "Got it");
        };

        // Repo rename dialog
        _viewModel.RepoRenameRequested += async (_, repo) =>
        {
            var name = await DisplayPromptAsync(
                "Name this repository",
                "Enter a friendly label for quick identification:",
                initialValue: repo.Name,
                placeholder:  "e.g. My API Project",
                maxLength:    60);

            if (name is not null)
                await _viewModel.SetRepoNameAsync(repo, name);
        };

        // History popup — shows the saved-repos list as an action sheet
        // so the clock icon in the URL field provides a quick-pick overlay.
        _viewModel.ShowHistoryRequested += async (_, repos) =>
        {
            if (repos.Count == 0)
            {
                await DisplayAlert("History", "No recent repositories saved yet.", "OK");
                return;
            }

            var labels = repos.Select(r => r.DisplayName).ToArray();
            var picked = await DisplayActionSheet("Recent repositories", "Cancel", null, labels);

            if (picked is null or "Cancel") return;

            var chosen = repos.FirstOrDefault(r => r.DisplayName == picked);
            if (chosen is not null) _viewModel.SelectRepoCommand.Execute(chosen);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.InitializeAsync();

            if (!string.IsNullOrWhiteSpace(SharedUrlService.PendingUrl))
            {
                _viewModel.RepoUrl          = SharedUrlService.PendingUrl;
                SharedUrlService.PendingUrl = null;
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"⚠️ Startup error: {ex.Message}";
        }
    }
}
