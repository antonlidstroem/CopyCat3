namespace CopyCat;

public partial class App : Application
{
    // IMPORTANT: Do NOT inject MainPage directly here.
    //
    // The DI container resolves constructor parameters before calling
    // InitializeComponent(). That means MainPage.InitializeComponent()
    // would run before App.InitializeComponent(), so Application.Resources
    // would still be empty when MainPage.xaml tries to look up StaticResources
    // like "Card", causing XamlParseException at runtime.
    //
    // Fix: take IServiceProvider and resolve MainPage AFTER InitializeComponent()
    // has finished loading App.xaml (and its merged ResourceDictionary).

    public App(IServiceProvider services)
    {
        InitializeComponent();          // ← App.xaml loaded, Resources populated

        var mainPage = services.GetRequiredService<MainPage>();  // ← safe now

        MainPage = new NavigationPage(mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#0F0F1A"),
            BarTextColor       = Colors.White
        };
    }
}
