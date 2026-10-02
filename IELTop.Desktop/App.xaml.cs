namespace IELTop.Desktop;

public partial class App : Application
{
    private readonly MainPage _mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        _mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_mainPage)
        {
            Title = "IELTop",
            Width = 1180,
            Height = 820,
            MinimumWidth = 980,
            MinimumHeight = 640,
        };
    }
}
