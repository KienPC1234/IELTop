using System;

namespace IELTop.Desktop.Web;

/// <summary>
/// What went wrong with the browser, in terms the student can act on: a plain
/// English line, and the address that fixes it when there is one.
/// </summary>
public sealed class BrowserProblem
{
    private BrowserProblem(string title, string detail, string? downloadUrl, bool canRetry = true)
    {
        Title = title;
        Detail = detail;
        DownloadUrl = downloadUrl;
        CanRetry = canRetry;
    }

    /// <summary>Short heading for the error screen.</summary>
    public string Title { get; }

    /// <summary>The cause and the fix, in plain English.</summary>
    public string Detail { get; }

    /// <summary>The page to open for the fix, or null when there is nothing to download.</summary>
    public string? DownloadUrl { get; }

    /// <summary>Whether retrying on its own could work.</summary>
    public bool CanRetry { get; }

    /// <summary>
    /// The engine could not be started at all. Whatever the underlying error
    /// text says, this is always an engine problem and never a page problem, so
    /// the fix is the same: install or update the engine. A retry cannot help,
    /// so the retry button is left off.
    /// </summary>
    public static BrowserProblem EngineUnavailable(string detail)
    {
        if (Mentions(detail, "older version", "version is not supported", "out of date", "WebView2Loader"))
        {
            return OutdatedEngine();
        }

        return MissingEngine();
    }

    /// <summary>
    /// The WebView2 engine is not installed. This is the one problem the
    /// student can fix themselves, so it is the one with a download link. A
    /// retry cannot help, so the button is left off.
    /// </summary>
    public static BrowserProblem MissingEngine() => new(
        "IELTop cannot open",
        "The Microsoft Edge WebView2 engine is not installed on this computer, so the app screen cannot open. "
        + "Install the Evergreen runtime, then start IELTop again.",
        "https://developer.microsoft.com/microsoft-edge/webview2/",
        canRetry: false);

    /// <summary>The engine is installed but too old to be used.</summary>
    public static BrowserProblem OutdatedEngine() => new(
        "IELTop needs a newer browser engine",
        "The Microsoft Edge WebView2 engine on this computer is too old for the app. "
        + "Update the Evergreen runtime, then start IELTop again.",
        "https://developer.microsoft.com/microsoft-edge/webview2/",
        canRetry: false);

    /// <summary>The app installed without its UI files.</summary>
    public static BrowserProblem MissingAppFiles() => new(
        "IELTop cannot open",
        "The app files are incomplete, so the app screen is missing. "
        + "Reinstall IELTop, or run 'npm run build' in IELTop.Desktop/UserInterface when building from source.",
        null);

    /// <summary>The browser process ended or a page could not be opened.</summary>
    public static BrowserProblem LoadFailed() => new(
        "IELTop cannot open",
        "The app screen could not be opened. Close and start IELTop again. "
        + "If it keeps failing, reinstall the app.",
        null);

    /// <summary>
    /// The download page was sent to the default browser. Shown after the button
    /// so the student knows what happened and what to do next, instead of the
    /// window going quiet with a button that looks dead.
    /// </summary>
    public static BrowserProblem DownloadPageOpened(string url, bool opened)
    {
        var detail = opened
            ? "The download page has been opened in your browser. Install the browser engine there, "
              + "then press Try again. The address is " + url + " if the page did not come up."
            : "Your browser could not be opened automatically. Open this address yourself, install the "
              + "browser engine, then press Try again: " + url;

        return new("Install the browser engine", detail, url);
    }

    /// <summary>
    /// Picks the problem from a browser failure. The engine type is read from
    /// the exception name because MAUI exposes only the WebView2 winmd, which
    /// does not carry the exception type.
    /// </summary>
    public static BrowserProblem FromException(Exception ex) => FromDetail(
        $"{ex.GetType().Name} {ex.Message}",
        0);

    /// <summary>Picks the problem from a navigation result.</summary>
    public static BrowserProblem FromStatus(string webErrorStatus, int httpStatusCode)
        => FromDetail(webErrorStatus, httpStatusCode);

    /// <summary>Picks the problem from whatever the browser reported.</summary>
    public static BrowserProblem FromDetail(string detail, int httpStatusCode)
    {
        if (Mentions(detail, "RuntimeNotFound", "No WebView2", "WebView2 runtime", "browser executable"))
        {
            return MissingEngine();
        }

        if (Mentions(detail, "older version", "version is not supported", "out of date", "WebView2Loader"))
        {
            return OutdatedEngine();
        }

        if (httpStatusCode is >= 400 and < 600)
        {
            return MissingAppFiles();
        }

        return LoadFailed();
    }

    private static bool Mentions(string detail, params string[] markers)
    {
        foreach (var marker in markers)
        {
            if (detail.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}