using IELTop.Services.Diagnostics;
using Microsoft.UI.Xaml;

namespace IELTop.Desktop.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();

        // WinUI raises UI-thread faults here before the window goes away. Without
        // this hook such a crash leaves a blank window and nothing in the log.
        // The exception is logged and left unhandled, so the behaviour is the
        // same as before, only now it is written down.
        UnhandledException += (_, e) =>
        {
            HealthMonitor.CountUnhandled();
            AppLog.Error("app", "Unhandled UI exception.", e.Exception);
            AppLog.Flush();
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
