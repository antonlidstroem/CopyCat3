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

        // ── HTTP client (GitHub API) ──────────────────────────────────────────
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

        // ── Services ──────────────────────────────────────────────────────────
        builder.Services.AddSingleton<IGitHubService,    GitHubService>();
        builder.Services.AddSingleton<IChunkingService,  ChunkingService>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();
        builder.Services.AddSingleton<IShareService,     MauiShareService>();
        builder.Services.AddSingleton<ILocalFileService, LocalFileService>();

        // ── Database (ISP split) ──────────────────────────────────────────────
        // DatabaseService implements both IRepoRepository and IPromptRepository
        // from a single shared SQLite connection.  Registering it as a singleton
        // first, then aliasing both interfaces to the same instance, ensures:
        //   • One DB connection for the lifetime of the app.
        //   • RepositoryViewModel depends on IRepoRepository only.
        //   • PromptsViewModel    depends on IPromptRepository only.
        //   • Neither can accidentally call the other domain's data layer.
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<IRepoRepository>  (sp => sp.GetRequiredService<DatabaseService>());
        builder.Services.AddSingleton<IPromptRepository>(sp => sp.GetRequiredService<DatabaseService>());

        // ── UI ────────────────────────────────────────────────────────────────
        // PromptsPage is NOT registered here: it is instantiated on-demand in
        // MainPage.xaml.cs via Navigation.PushAsync(new PromptsPage(viewModel)).
        // This avoids DI singleton lifecycle conflicts with the Navigation stack.
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
