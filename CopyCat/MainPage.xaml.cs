using CopyCat.Services;
using CopyCat.ViewModels;
using CopyCat.Views;

namespace CopyCat;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly PromptsViewModel _promptsViewModel;

    public MainPage(MainViewModel viewModel, PromptsViewModel promptsViewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        var repo = viewModel.Repo;
        var prompts = viewModel.Prompts;

        // ── Branch picker ──────────────────────────────────────────────────
        repo.BranchPickerRequested += async (_, branches) =>
        {
            var result = await DisplayActionSheet("Select branch", "Cancel", null, [.. branches]);
            if (result is not null and not "Cancel")
                repo.Branch = result;
        };

        // ── Token info dialog ──────────────────────────────────────────────
        repo.TokenInfoRequested += async (_, _) =>
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

        // ── Generic info dialog (tooltip fallback for mobile) ─────────────
        repo.ShowInfoRequested += async (_, message) =>
        {
            if (!string.IsNullOrWhiteSpace(message))
                await DisplayAlert("Info", message, "OK");
        };

        // ── Repo rename ────────────────────────────────────────────────────
        repo.RepoRenameRequested += async (_, repoItem) =>
        {
            var name = await DisplayPromptAsync(
                "Name this repository",
                "Enter a friendly label:",
                initialValue: repoItem.Name,
                placeholder: "e.g. My API Project",
                maxLength: 60);
            if (name is not null)
                await repo.SetRepoNameAsync(repoItem, name);
        };

        // ── History popup ──────────────────────────────────────────────────
        repo.ShowHistoryRequested += async (_, repos) =>
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
            if (chosen is not null) repo.SelectRepoCommand.Execute(chosen);
        };

        // ── Navigate to PromptsPage ────────────────────────────────────────
        prompts.NavigateToPromptsPageRequested += async (_, _) =>
        {
            var page = new PromptsPage(promptsViewModel);
            await Navigation.PushAsync(page);
        };

        prompts.GoBackRequested += async (_, _) =>
        {
            await Navigation.PopAsync();
        };

        // ── Phase 4: Custom XML tag dialog ────────────────────────────────
        promptsViewModel.CustomXmlTagRequested += async (_, _) =>
        {
            var label = await DisplayPromptAsync(
                "New XML Tag Button",
                "Button label (e.g. 🔑 Auth):",
                placeholder: "🔑 Auth",
                maxLength: 40);
            if (string.IsNullOrWhiteSpace(label)) return;

            var xmlOpen = await DisplayPromptAsync(
                "Opening tag",
                "XML opening tag (e.g. <auth>). Leave empty for plain text insertion:",
                placeholder: "<auth>",
                maxLength: 80);

            var xmlClose = string.Empty;
            if (!string.IsNullOrWhiteSpace(xmlOpen))
            {
                xmlClose = await DisplayPromptAsync(
                    "Closing tag",
                    "XML closing tag (e.g. </auth>):",
                    placeholder: "</auth>",
                    maxLength: 80) ?? string.Empty;
            }

            var placeholder = await DisplayPromptAsync(
                "Default placeholder text",
                "Text inserted between the tags:",
                placeholder: "Describe the authentication mechanism…",
                maxLength: 200) ?? string.Empty;

            await _viewModel.AddCustomXmlTagAsync(label, xmlOpen ?? string.Empty, xmlClose, placeholder);
        };
        _promptsViewModel = promptsViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.InitializeAsync();

            if (!string.IsNullOrWhiteSpace(SharedUrlService.PendingUrl))
            {
                _viewModel.Repo.RepoUrl = SharedUrlService.PendingUrl;
                SharedUrlService.PendingUrl = null;
            }
        }
        catch (Exception ex)
        {
            _viewModel.Chunking.StatusText = $"⚠️ Startup error: {ex.Message}";
        }
    }
}