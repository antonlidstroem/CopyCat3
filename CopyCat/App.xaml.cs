namespace CopyCat;

public partial class App : Application
{
    public App(MainPage mainPage)
    {
        InitializeComponent();
        MainPage = new NavigationPage(mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#0F0F1A"),
            BarTextColor       = Colors.White
        };
    }
}
