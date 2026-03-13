using CopyCat.Services;
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

        builder.Services.AddHttpClient("github", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CopyCat/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect        = true,
            MaxAutomaticRedirections = 5
        });

        // Services
        builder.Services.AddSingleton<IGitHubService,    GitHubService>();
        builder.Services.AddSingleton<IChunkingService,  ChunkingService>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();
        builder.Services.AddSingleton<IShareService,     MauiShareService>();
        builder.Services.AddSingleton<IDatabaseService,  DatabaseService>();
        builder.Services.AddSingleton<ILocalFileService, LocalFileService>();

        // BUG 5 FIX: FileTypeDetectorService (Trees API) was registered here
        // but never injected into any consumer — MainViewModel uses
        // IGitHubService.DetectFileTypesInRepoAsync (ZIP-based) instead.
        // Removed to eliminate the dead singleton allocation and the
        // confusion it causes when reading the DI setup.
        //
        // If you later want to switch auto-detect to the lighter Trees API
        // approach (one JSON request vs a full ZIP download), inject
        // IFileTypeDetectorService into MainViewModel and call
        // DetectExtensionsAsync there instead of DetectFileTypesInRepoAsync.

        // UI
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
