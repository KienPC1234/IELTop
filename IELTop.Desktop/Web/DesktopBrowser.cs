using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace IELTop.Desktop.Web;

/// <summary>What the browser is allowed to do.</summary>
public sealed class BrowserOptions
{
    /// <summary>Lets the page use the microphone. Speaking practice needs it.</summary>
    public bool AllowMicrophone { get; init; } = true;

    /// <summary>
    /// Opens the developer tools on F12. Off by default so a page under test
    /// cannot be inspected or edited while the test is running.
    /// </summary>
    public bool AllowDeveloperTools { get; init; }
}

/// <summary>
/// The WebView2 view the app window shows, with its native message channel
/// wired to the bridge. One class, no interface and no factory: the app targets
/// Windows only, so there is nothing to switch between.
///
/// Two ways in, and they are used on purpose. Messages from the page arrive
/// through <see cref="MessageReceived"/> and the answer goes back through
/// <see cref="PostMessage"/>. The HTTP loopback server serves the page and the
/// same calls, which is the path that works even when no browser can start.
/// </summary>
public sealed class DesktopBrowser : IDisposable
{
    private readonly BrowserOptions _options;
    private readonly CancellationTokenSource _shutdown = new();
    private WebView? _view;
    private Microsoft.UI.Xaml.Controls.WebView2? _native;
    private string? _pendingUrl;
    private bool _loadFailed;
    private bool _engineMissing;
    private bool _disposed;

    /// <summary>
    /// Handles one message posted by the page and returns the answer to send
    /// back, or null when the page asked nothing.
    /// </summary>
    public event Func<string, CancellationToken, Task<string?>>? MessageReceived;

    /// <summary>
    /// Raised when the page cannot be shown at all, for example the browser
    /// engine is not installed. Nothing else will load the window, so the host
    /// has to say it natively. The problem carries plain English text and, when
    /// there is one, the address of the fix.
    /// </summary>
    public event Action<BrowserProblem>? LoadFailed;

    public DesktopBrowser(BrowserOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Hands over the view to show on screen. Call once, at any point in the
    /// page lifecycle: the browser waits for the native view to appear.
    /// </summary>
    public void Attach(WebView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.HandlerChanged += OnViewHandlerChanged;
        TryBindNativeView(view);
    }

    /// <summary>
    /// Loads a URL. Calling it before the native view exists is fine, the URL
    /// is kept and opened once the view is ready.
    /// </summary>
    public void Load(string url)
    {
        _pendingUrl = url;
        _loadFailed = false;

        if (_native?.CoreWebView2 is { } core)
        {
            Navigate(core, url);
        }
    }

    /// <summary>Loads the last URL again, for a retry after a failure.</summary>
    public void Retry()
    {
        // An engine that is missing will still be missing, and navigating again
        // would only repeat the same failure for the student to read twice.
        if (_engineMissing) return;
        Load(_pendingUrl ?? string.Empty);
    }

    /// <summary>Sends a message to the page. Does nothing once the view is gone.</summary>
    public void PostMessage(string json)
    {
        var native = _native;
        if (native is null || string.IsNullOrEmpty(json)) return;

        native.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                native.CoreWebView2?.PostWebMessageAsString(json);
            }
            catch (Exception)
            {
                // The view was navigated or closed while the message waited.
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();

        if (_view is { } view)
        {
            view.HandlerChanged -= OnViewHandlerChanged;
        }

        if (_native?.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.ProcessFailed -= OnProcessFailed;
            core.PermissionRequested -= OnPermissionRequested;
        }

        MessageReceived = null;
        LoadFailed = null;
        _view = null;
        _native = null;
        _shutdown.Dispose();
    }

    private void OnViewHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is WebView view) TryBindNativeView(view);
    }

    private void TryBindNativeView(WebView view)
    {
        if (_disposed) return;
        if (_view is not null && !ReferenceEquals(_view, view)) return;

        _view = view;
        if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native) return;
        if (ReferenceEquals(native, _native)) return;

        _native = native;
        _ = InitializeAsync(native);
    }

    private async Task InitializeAsync(Microsoft.UI.Xaml.Controls.WebView2 native)
    {
        try
        {
            await native.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            // The engine could not start. Whatever the error text turns out to
            // be (a missing runtime, a runtime too old, or a null reference
            // from inside the loader), the fix is the same and a retry would
            // fail the same way, so it is named as an engine problem.
            _engineMissing = true;
            ReportFailure(
                BrowserProblem.EngineUnavailable($"{ex.GetType().Name}: {ex.Message}"),
                $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (_disposed || !ReferenceEquals(native, _native)) return;

        // When the engine cannot start, EnsureCoreWebView2Async can return
        // without throwing and leave CoreWebView2 null. Touching it then gives
        // a NullReferenceException, which reads as a generic failure and offers
        // a retry that can never work. It is a missing engine, so it is named.
        var core = native.CoreWebView2;
        if (core is null)
        {
            _engineMissing = true;
            ReportFailure(
                BrowserProblem.MissingEngine(),
                "EnsureCoreWebView2Async returned but left CoreWebView2 null, so no engine is present.");
            return;
        }

        try
        {
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreDevToolsEnabled = _options.AllowDeveloperTools;
            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += OnProcessFailed;

            if (_options.AllowMicrophone)
            {
                core.PermissionRequested += OnPermissionRequested;
            }
        }
        catch (Exception ex)
        {
            ReportFailure(BrowserProblem.FromException(ex), ex);
            return;
        }

        // A Load call made before the native view existed is honoured here.
        if (_pendingUrl is not null)
        {
            Navigate(core, _pendingUrl);
        }
    }

    private void Navigate(Microsoft.Web.WebView2.Core.CoreWebView2 core, string url)
    {
        if (_loadFailed) return;

        try
        {
            core.Navigate(url);
        }
        catch (Exception ex)
        {
            ReportFailure(BrowserProblem.FromException(ex), ex);
        }
    }

    private void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        // A document that came back with an error status is still a navigation
        // WebView2 calls successful, so the status code is checked as well.
        if (args.IsSuccess && args.HttpStatusCode < 400) return;

        // The page giving up on its own navigation is normal, for example when
        // a download takes over, so only a real failure reaches the user.
        if (args.WebErrorStatus is Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.OperationCanceled
            or Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.ConnectionAborted)
        {
            return;
        }

        // IsSuccess is deliberately not trusted: a 404 for the app page arrived
        // with IsSuccess true, and gating on it lost the real reason.
        var detail = $"{args.WebErrorStatus}, HTTP {args.HttpStatusCode} for {_pendingUrl ?? "the app page"}";
        ReportFailure(BrowserProblem.FromStatus(args.WebErrorStatus.ToString(), args.HttpStatusCode), detail);
    }

    private void OnProcessFailed(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs args)
    {
        ReportFailure(
            BrowserProblem.LoadFailed(),
            $"The browser process ended with {args.ProcessFailedKind}, code {args.ExitCode}.");
    }

    /// <summary>
    /// Reports once. The same failure fires again on every retry attempt, and
    /// the user does not need the message a second time.
    /// </summary>
    private void ReportFailure(BrowserProblem problem, Exception ex)
        => ReportFailure(problem, $"{ex.GetType().Name}: {ex.Message}");

    private void ReportFailure(BrowserProblem problem, string detail)
    {
        if (_loadFailed) return;
        _loadFailed = true;

        Console.Error.WriteLine($"[WebView] {problem.Detail} ({detail})");
        WebViewLog.Write($"{problem.Title} | {problem.Detail} | {detail}");
        LoadFailed?.Invoke(problem);
    }

    private static void OnPermissionRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs args)
    {
        if (args.PermissionKind == Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone)
        {
            args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow;
        }
    }

    private async void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        var handler = MessageReceived;
        if (handler is null) return;

        string raw;
        try
        {
            raw = args.TryGetWebMessageAsString();
        }
        catch (Exception)
        {
            // The page posted something that is not text.
            return;
        }

        if (string.IsNullOrWhiteSpace(raw)) return;

        try
        {
            var reply = await handler(raw, _shutdown.Token);
            if (!string.IsNullOrEmpty(reply)) PostMessage(reply);
        }
        catch (OperationCanceledException)
        {
            // The window closed while the host was answering.
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WebView] The page message failed: {ex.Message}");
        }
    }
}