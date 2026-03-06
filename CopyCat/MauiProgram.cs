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

        // Named HttpClient — single shared instance with centralised config.
        // GitHubService resolves this via IHttpClientFactory.
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

        // UI — Singleton avoids captive-dependency issues in a single-page app.
        // App itself is managed by UseMauiApp<App>() — do NOT add it here again.
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
