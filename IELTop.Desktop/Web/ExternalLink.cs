using System;
using System.Diagnostics;

namespace IELTop.Desktop.Web;

/// <summary>
/// Opens a web address in the student's own browser. Used for the download page
/// of a missing engine: the WebView is exactly what is not working, so the
/// address has to go somewhere else.
/// </summary>
internal static class ExternalLink
{
    /// <summary>
    /// Opens the address, or reports why it could not. Returns false instead of
    /// throwing so the caller can put the address on screen as a last resort.
    /// </summary>
    public static bool Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            WebViewLog.Write($"Could not open {url}: {ex.Message}");
            return false;
        }
    }
}