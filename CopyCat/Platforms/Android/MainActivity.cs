using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CopyCat.Services;

namespace CopyCat;

[Activity(
    Theme              = "@style/Maui.SplashTheme",
    MainLauncher       = true,
    LaunchMode         = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize  | ConfigChanges.Orientation   |
        ConfigChanges.UiMode      | ConfigChanges.ScreenLayout  |
        ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionSend },
    Categories   = new[] { Intent.CategoryDefault },
    DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleShareIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleShareIntent(intent);
    }

    /// <summary>
    /// Extracts a GitHub URL from a plain-text share intent and stores it
    /// in SharedUrlService so MainPage.OnAppearing can pick it up.
    /// </summary>
    private static void HandleShareIntent(Intent? intent)
    {
        if (intent?.Action == Intent.ActionSend &&
            intent.Type    == "text/plain")
        {
            var text = intent.GetStringExtra(Intent.ExtraText);
            if (!string.IsNullOrWhiteSpace(text))
                SharedUrlService.PendingUrl = text.Trim();
        }
    }
}
