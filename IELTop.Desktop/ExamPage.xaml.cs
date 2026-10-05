using System;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Desktop.Bridge;
using IELTop.Desktop.Web;

namespace IELTop.Desktop;

public partial class ExamPage : ContentPage
{
    private readonly BridgeRouter _router;
    private readonly MauiExamSession _examSession;
    private readonly DesktopBrowser _browser;
    private readonly BrowserFailurePresenter _failure;

    public ExamPage(
        StaticFileServer server,
        BridgeRouter router,
        MauiExamSession examSession)
    {
        InitializeComponent();

        _router = router;
        _examSession = examSession;

        // Developer tools stay off even under a debugger: this is the window a
        // test runs in, and the page must not be inspectable while it runs.
        _browser = new DesktopBrowser(new BrowserOptions { AllowDeveloperTools = false });

        _browser.MessageReceived += OnMessageFromPage;
        _browser.Attach(ExamWebView);
        _browser.Load($"{server.BaseUrl}/exam.html?k={server.AccessKey}");

        // A broken test window must not trap the student above their other
        // windows while it sits there showing an error.
        _failure = new BrowserFailurePresenter(
            _browser, ExamWebView, FallbackOverlay, FallbackHost,
            () => _examSession.SetAlwaysOnTop(false));
    }

    private async Task<string?> OnMessageFromPage(string raw, CancellationToken cancellationToken)
        => await _router.HandleAsync(raw, cancellationToken);

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler is not null) return;

        _browser.MessageReceived -= OnMessageFromPage;
        _failure.Dispose();
        _browser.Dispose();
    }
}