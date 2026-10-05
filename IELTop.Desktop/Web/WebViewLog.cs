using System;
using System.IO;
using IELTop.Services.Diagnostics;

namespace IELTop.Desktop.Web;

/// <summary>
/// Writes browser failures to a file so a report from a user says what went
/// wrong. The console is not visible in a packaged app, so without this a blank
/// window would leave nothing to look at.
///
/// The file lives under the user profile rather than beside the app: an install
/// directory is not writable without elevation, and swallowing the write error
/// would leave exactly the silent failure this file exists to prevent.
///
/// Lines also go to the one app log (AppLog) so the Diagnostics tab shows a
/// browser failure next to everything else, in one place and one order.
/// </summary>
internal static class WebViewLog
{
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        AppLog.Error("webview", line);
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IELTop", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "webview.log"),
                    $"{DateTimeOffset.Now:u} {line}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never be the reason the app misbehaves.
        }
    }
}