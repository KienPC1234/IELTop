using IELTop.Services.Storage;

namespace IELTop.Desktop.Web;

/// <summary>
/// Applies the saved window preference to the main window. The native window
/// does not exist yet when CreateWindow returns, so the apply retries on the UI
/// thread until the platform view is there, then stops.
/// </summary>
public static class MainWindowPreferences
{
    public static void ApplyFullscreenOnStart(Window window, ISettingsStore settings)
    {
        if (!settings.Current.FullscreenOnStart)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                    appWindow?.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                    return;
                }

                await Task.Delay(50);
            }
        });
    }
}
