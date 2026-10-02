using IELTop.Desktop.Bridge;
using IELTop.Desktop.Web;
using IELTop.Services.Exam;

namespace IELTop.Desktop;

public partial class ExamPage : ContentPage
{
    private readonly StaticFileServer _server;
    private readonly BridgeRouter _router;
    private readonly ExamEngine _engine;
    private readonly MauiExamSession _examSession;
    private bool _initialized;

    public ExamPage(StaticFileServer server, BridgeRouter router, ExamEngine engine, MauiExamSession examSession)
    {
        InitializeComponent();
        _server = server;
        _router = router;
        _engine = engine;
        _examSession = examSession;

        ExamWebView.Source = new UrlWebViewSource { Url = $"{_server.BaseUrl}/exam.html" };
        ExamWebView.HandlerChanged += (_, _) => TryInitPlatformView();
        Loaded += (_, _) =>
        {
            TryInitPlatformView();
            if (Window is not null)
            {
                _examSession.Attach(Window);
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        TryInitPlatformView();
    }

    private void TryInitPlatformView()
    {
#if WINDOWS
        if (!_initialized && ExamWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 native)
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
            native.CoreWebView2.Settings.AreDevToolsEnabled = true;
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
                            // WebView navigated or disposed
                        }
                    });
                }
            };

            _examSession.RegisterPushSink((message) =>
            {
                native.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        native.CoreWebView2?.PostWebMessageAsString(message);
                    }
                    catch
                    {
                        // WebView navigated or disposed
                    }
                });
            });

            native.DispatcherQueue.TryEnqueue(() =>
            {
                native.CoreWebView2.Navigate($"{_server.BaseUrl}/exam.html");
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ExamPage] WebView2 initialization failed: {ex}");
        }
    }
#endif
}
