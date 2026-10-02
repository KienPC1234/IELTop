using IELTop.Desktop.Bridge;
using IELTop.Desktop.Web;

namespace IELTop.Desktop;

public partial class MainPage : ContentPage
{
    private readonly StaticFileServer _server;
    private readonly BridgeRouter _router;
    private bool _initialized;

    public MainPage(StaticFileServer server, BridgeRouter router)
    {
        InitializeComponent();
        _server = server;
        _router = router;

        AppWebView.Source = new UrlWebViewSource { Url = $"{_server.BaseUrl}/index.html" };
        AppWebView.HandlerChanged += (_, _) => TryInitPlatformView();
        Loaded += (_, _) => TryInitPlatformView();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        TryInitPlatformView();
    }

    private void TryInitPlatformView()
    {
#if WINDOWS
        if (!_initialized && AppWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 native)
        {
            _initialized = true;
            _ = InitWebView2Async(native);
        }
#endif
    }

#if WINDOWS
    private async Task InitWebView2Async(Microsoft.UI.Xaml.Controls.WebView2 native)
    {
        try
        {
            await native.EnsureCoreWebView2Async();
            native.CoreWebView2.Settings.IsWebMessageEnabled = true;
            native.CoreWebView2.PermissionRequested += (s, args) =>
            {
                if (args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone)
                {
                    args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
                }
            };

            native.CoreWebView2.WebMessageReceived += async (s, args) =>
            {
                string raw;
                try
                {
                    raw = args.TryGetWebMessageAsString();
                }
                catch
                {
                    return;
                }

                if (string.IsNullOrEmpty(raw)) return;

                var reply = await _router.HandleAsync(raw, CancellationToken.None).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(reply))
                {
                    native.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            native.CoreWebView2?.PostWebMessageAsString(reply);
                        }
                        catch
                        {
                            // WebView was navigated or disposed
                        }
                    });
                }
            };

            native.DispatcherQueue.TryEnqueue(() =>
            {
                native.CoreWebView2.Navigate($"{_server.BaseUrl}/index.html");
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainPage] WebView2 initialization failed: {ex}");
        }
    }
#endif
}
