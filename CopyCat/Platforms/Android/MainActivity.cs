using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CopyCat.Services;

namespace CopyCat;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]

[IntentFilter(new[] { Android.Content.Intent.ActionSend },
    Categories = new[] { Android.Content.Intent.CategoryDefault },
    DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleShareIntent(Intent);
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleShareIntent(intent);
    }

    private void HandleShareIntent(Android.Content.Intent? intent)
    {
        if (intent?.Action == Android.Content.Intent.ActionSend && intent.Type == "text/plain")
        {
            var text = intent.GetStringExtra(Android.Content.Intent.ExtraText);
            if (!string.IsNullOrWhiteSpace(text))
            {
                SharedUrlService.PendingUrl = text.Trim();
            }
        }
    }
}