using CopyCat.Services;
using CopyCat.ViewModels;
using CopyCat.Views;

namespace CopyCat;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;

        // ── Branch picker (RepositoryViewModel) ──────────────────────────────
        _vm.Repo.BranchPickerRequested += async (_, branches) =>
        {
            var result = await DisplayActionSheet("Select branch", "Cancel", null,
                [.. _vm.Repo.BranchOptions]);
            if (result is not null and not "Cancel")
                _vm.Repo.Branch = result;
        };

        // ── Token info dialog (RepositoryViewModel) ───────────────────────────
        _vm.Repo.TokenInfoRequested += async (_, _) =>
        {
            await DisplayAlert(
                "GitHub Personal Access Token",
                "A token lets CopyCat access private repositories and avoids rate limits.\n\n" +
                "How to create one:\n" +
                "1. Go to github.com → Profile → Settings\n" +
                "2. Developer settings → Personal access tokens → Fine-grained tokens\n" +
                "3. Click \"Generate new token\"\n" +
                "4. Under Repository permissions, set Contents → Read-only\n" +
                "5. Copy the token (starts with github_pat_…) and paste it here.\n\n" +
                "The token is stored securely on your device.",
                "Got it");
        };

        // ── Generic info dialog ───────────────────────────────────────────────
        _vm.Repo.ShowInfoRequested += async (_, message) =>
        {
            if (!string.IsNullOrWhiteSpace(message))
                await DisplayAlert("Info", message, "OK");
        };

        // ── Repo rename (RepositoryViewModel) ────────────────────────────────
        _vm.Repo.RepoRenameRequested += async (_, repo) =>
        {
            var name = await DisplayPromptAsync(
                "Name this repository",
                "Enter a friendly label:",
                initialValue: repo.Name,
                placeholder: "e.g. My API Project",
                maxLength: 60);
            if (name is not null)
                await _vm.Repo.SetRepoNameAsync(repo, name);
        };

        // ── History popup (RepositoryViewModel) ──────────────────────────────
        _vm.Repo.ShowHistoryRequested += async (_, repos) =>
        {
            if (repos.Count == 0)
            {
                await DisplayAlert("History", "No recent repositories saved yet.", "OK");
                return;
            }
            var picked = await DisplayActionSheet(
                "Recent repositories", "Cancel", null,
                repos.Select(r => r.DisplayName).ToArray());
            if (picked is null or "Cancel") return;
            var chosen = repos.FirstOrDefault(r => r.DisplayName == picked);
            if (chosen is not null) _vm.Repo.SelectRepoCommand.Execute(chosen);
        };

        // ── Navigate to PromptsPage (PromptsViewModel) ────────────────────────
        _vm.Prompts.NavigateToPromptsPageRequested += async (_, _) =>
        {
            // FIX: pass _vm (MainViewModel), NOT _vm.Prompts.
            // PromptsPage.xaml has x:DataType="vm:MainViewModel" and binds
            // through Prompts.Xxx — BindingContext must be MainViewModel.
            var page = new PromptsPage(_vm);
            await Navigation.PushAsync(page);
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.InitializeAsync();

            if (!string.IsNullOrWhiteSpace(SharedUrlService.PendingUrl))
            {
                _vm.Repo.RepoUrl = SharedUrlService.PendingUrl;
                SharedUrlService.PendingUrl = null;
            }
        }
        catch (Exception ex)
        {
            _vm.Chunking.StatusText = $"⚠️ Startup error: {ex.Message}";
        }
    }
}
