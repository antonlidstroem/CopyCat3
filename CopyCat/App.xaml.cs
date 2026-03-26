namespace CopyCat;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        var mainPage = services.GetRequiredService<MainPage>();
        MainPage = new NavigationPage(mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#0F0F1A"),
            BarTextColor       = Colors.White
        };
    }
}
