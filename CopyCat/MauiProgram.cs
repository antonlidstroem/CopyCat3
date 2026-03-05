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
                fonts.AddFont("OpenSans-Regular.ttf",   "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf",  "OpenSansSemibold");
            });

        // Services
        builder.Services.AddSingleton<IGitHubService,    GitHubService>();
        builder.Services.AddSingleton<IChunkingService,  ChunkingService>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();

        // ViewModels & Pages
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddSingleton<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
