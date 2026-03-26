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

        // ── Services ──────────────────────────────────────────────────────

        builder.Services.AddSingleton<IGitHubService,            GitHubService>();
        builder.Services.AddSingleton<IChunkingService,          ChunkingService>();
        builder.Services.AddSingleton<IClipboardService,         MauiClipboardService>();
        builder.Services.AddSingleton<IShareService,             MauiShareService>();
        builder.Services.AddSingleton<IDatabaseService,          DatabaseService>();
        builder.Services.AddSingleton<ILocalFileService,         LocalFileService>();

        // FIX C-1: FileTypeDetectorService was implemented and had the interface
        // but was never registered in DI. It is now injected into MainViewModel
        // to replace the duplicated local-directory walk in AutoDetectFileTypesAsync.
        builder.Services.AddSingleton<IFileTypeDetectorService,  FileTypeDetectorService>();

        // ── UI ─────────────────────────────────────────────────────────────

        // PromptsPage is NOT registered here: it is instantiated on-demand
        // in MainPage.xaml.cs via Navigation.PushAsync(new PromptsPage(viewModel)).
        // This avoids DI singleton lifecycle conflicts with the Navigation stack.
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
