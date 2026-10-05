using IELTop.Desktop.Web;
using IELTop.Services.Storage;

namespace IELTop.Desktop;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly ISettingsStore _settings;

    public App(MainPage mainPage, ISettingsStore settings)
    {
        InitializeComponent();
        _mainPage = mainPage;
        _settings = settings;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_mainPage)
        {
            Title = "IELTop",
            Width = 1180,
            Height = 820,
            MinimumWidth = 980,
            MinimumHeight = 640,
        };

        // The saved preference is applied here, not in the page: the window only
        // exists at this point, and the page is rebuilt when a window closes.
        MainWindowPreferences.ApplyFullscreenOnStart(window, _settings);

        return window;
    }
}
