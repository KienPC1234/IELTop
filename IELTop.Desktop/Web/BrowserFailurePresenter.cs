using System;
using Microsoft.Maui.Controls;

namespace IELTop.Desktop.Web;

/// <summary>
/// Shows the failure screen over a browser view and puts it back on retry.
///
/// This lives here because the main window and the exam window need identical
/// behaviour, and the browser view is not replaced: WebView2 draws on its own
/// surface above ordinary XAML content, so a panel laid over it stays
/// invisible. Hiding the view keeps it alive for the retry and leaves the room
/// to the message.
/// </summary>
internal sealed class BrowserFailurePresenter : IDisposable
{
    private readonly DesktopBrowser _browser;
    private readonly WebView _webView;
    private readonly View _overlay;
    private readonly Layout _host;
    private readonly Action? _onShown;
    private BrowserProblem? _problem;

    public BrowserFailurePresenter(
        DesktopBrowser browser,
        WebView webView,
        View overlay,
        Layout host,
        Action? onShown = null)
    {
        _browser = browser;
        _webView = webView;
        _overlay = overlay;
        _host = host;
        _onShown = onShown;

        _browser.LoadFailed += OnLoadFailed;
    }

    /// <summary>True once the browser has failed and the message is on screen.</summary>
    public bool IsShowing => _problem is not null;

    private void OnLoadFailed(BrowserProblem problem)
        => MainThread.BeginInvokeOnMainThread(() => Show(problem));

    private void Show(BrowserProblem problem)
    {
        if (_problem is not null) return;

        _problem = problem;
        _onShown?.Invoke();

        _webView.IsVisible = false;
        _overlay.IsVisible = true;
        _host.Children.Clear();
        _host.Children.Add(WebFallbackView.Create(problem, Retry, OpenDownloadPage));
    }

    private void Retry()
    {
        _problem = null;
        _overlay.IsVisible = false;
        _webView.IsVisible = true;
        _browser.Retry();
    }

    /// <summary>
    /// Sends the student to the download page in their own browser, because the
    /// WebView is exactly what is not working. The screen then says what to do
    /// next, so the window never sits there with a button that looks dead.
    /// </summary>
    private void OpenDownloadPage(string url)
        => Show(BrowserProblem.DownloadPageOpened(url, ExternalLink.Open(url)));

    public void Dispose()
    {
        _browser.LoadFailed -= OnLoadFailed;
    }
}