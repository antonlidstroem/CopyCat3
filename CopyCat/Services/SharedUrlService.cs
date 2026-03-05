namespace CopyCat.Services;

/// <summary>
/// Simple static bridge between the Android Share Target intent handler
/// (MainActivity) and the MAUI page (MainPage.OnAppearing).
/// </summary>
public static class SharedUrlService
{
    public static string? PendingUrl { get; set; }
}
