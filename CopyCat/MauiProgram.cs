using CopyCat.Services;
using CopyCat.Services.Interfaces;
using CopyCat.ViewModels;
using CopyCat.Views.Results;
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
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddHttpClient("github", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CopyCat/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        });

        // ── Services ──────────────────────────────────────────────────────

        builder.Services.AddSingleton<IGitHubService, GitHubService>();
        builder.Services.AddSingleton<IChunkingService, ChunkingService>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();
        builder.Services.AddSingleton<IShareService, MauiShareService>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
        builder.Services.AddSingleton<ILocalFileService, LocalFileService>();
        builder.Services.AddSingleton<IFileTypeDetectorService, FileTypeDetectorService>();

        // ── ViewModels ─────────────────────────────────────────────────────
        // Order matters: leaf VMs first, then VMs that depend on them.

        builder.Services.AddSingleton<RepositoryViewModel>();
        builder.Services.AddSingleton<FilterViewModel>();
        builder.Services.AddSingleton<ChunkingViewModel>();
        builder.Services.AddSingleton<ChunkListViewModel>();
        builder.Services.AddSingleton<PromptsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();   // keep last if it depends on others

        // ── UI ─────────────────────────────────────────────────────────────

        builder.Services.AddSingleton<ChunkListView>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}