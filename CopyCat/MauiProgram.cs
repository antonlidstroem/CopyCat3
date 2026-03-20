using CopyCat.Services;
using CopyCat.Services.Interfaces;
using CopyCat.ViewModels;
using Microsoft.Extensions.Logging;

namespace CopyCat;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf",  "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── HTTP client ───────────────────────────────────────────────────────
        builder.Services.AddHttpClient("github", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CopyCat/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect        = true,
            MaxAutomaticRedirections = 5,
        });

        // ── Infrastructure services ───────────────────────────────────────────
        builder.Services.AddSingleton<IGitHubService,    GitHubService>();
        builder.Services.AddSingleton<IChunkingService,  ChunkingService>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();
        builder.Services.AddSingleton<IShareService,     MauiShareService>();
        builder.Services.AddSingleton<ILocalFileService, LocalFileService>();

        // ── Database (ISP split: one concrete, two interface registrations) ───
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<IRepoRepository>  (sp => sp.GetRequiredService<DatabaseService>());
        builder.Services.AddSingleton<IPromptRepository>(sp => sp.GetRequiredService<DatabaseService>());

        // ── Child ViewModels (singletons — share lifetime with the app) ───────
        //
        // Registration order matters for constructor injection:
        // ChunkingViewModel depends on RepositoryViewModel + FilterViewModel,
        // so those must be registered first.
        builder.Services.AddSingleton<RepositoryViewModel>();
        builder.Services.AddSingleton<FilterViewModel>();
        builder.Services.AddSingleton<PromptsViewModel>();
        builder.Services.AddSingleton<ChunkListViewModel>();
        builder.Services.AddSingleton<ChunkingViewModel>();   // depends on Repo + Filter

        // ── Orchestrator ──────────────────────────────────────────────────────
        builder.Services.AddSingleton<MainViewModel>();

        // ── UI ────────────────────────────────────────────────────────────────
        // PromptsPage is instantiated on-demand in MainPage.xaml.cs via
        // Navigation.PushAsync(new PromptsPage(viewModel.Prompts)).
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
