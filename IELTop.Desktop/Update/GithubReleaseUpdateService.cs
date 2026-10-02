using System.Net.Http;
using System.Text.Json;
using IELTop.Services.Update;

namespace IELTop.Desktop.Update;

/// <summary>
/// Checks GitHub Releases for a newer version on any OS. The app stays usable
/// while it checks and when the check fails. Downloading a release asset is
/// left to the browser: the UI opens the release page, which works the same on
/// Windows, macOS and Linux.
/// </summary>
public sealed class GithubReleaseUpdateService : IUpdateService
{
    private const string Owner = "KienPC1234";
    private const string Repo = "IELTop";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private string? _latestVersion;
    private string? _latestUrl;

    public bool IsInstalled
    {
        get
        {
            // A framework dependent build is a development build, so auto
            // update stays a manual step: open the release page and install.
            var path = AppContext.BaseDirectory;
            return File.Exists(Path.Combine(path, "Update.exe"))
                || File.Exists(Path.Combine(path, "update.exe"));
        }
    }

    public string CurrentVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("IELTop");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Fail("Could not reach the update server. Check your connection.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var page = doc.RootElement.TryGetProperty("html_url", out var h) ? h.GetString() ?? string.Empty : string.Empty;
            _latestVersion = TrimVersion(tag);
            _latestUrl = page;

            if (_latestVersion.Length == 0)
                return UpdateCheckResult.None($"You have the latest version ({CurrentVersion}).");

            bool newer = Compare(_latestVersion, CurrentVersion) > 0;
            return newer
                ? new UpdateCheckResult(true, true, $"Version {_latestVersion} is available.", _latestVersion)
                : UpdateCheckResult.None($"You have the latest version ({CurrentVersion}).");
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Fail("The update check was stopped.");
        }
        catch (Exception)
        {
            return UpdateCheckResult.Fail("Could not reach the update server. Check your connection.");
        }
    }

    public Task<UpdateCheckResult> DownloadAsync(CancellationToken ct = default)
    {
        // The release page hosts the installer for each OS; the user picks.
        return _latestUrl is { Length: > 0 }
            ? Task.FromResult(new UpdateCheckResult(true, true,
                $"Open the release page to download version {_latestVersion}.", _latestVersion ?? string.Empty))
            : Task.FromResult(UpdateCheckResult.None("Run a check first to find an update."));
    }

    public void ApplyAndRestart()
    {
        if (_latestUrl is not { Length: > 0 }) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _latestUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // If the browser cannot open, the user can visit the page by hand.
        }
    }

    private static string TrimVersion(string tag) => tag.TrimStart('v', 'V').Trim();

    /// <summary>Compares dotted versions. Returns positive when a is newer than b.</summary>
    private static int Compare(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int na = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
            int nb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
            if (na != nb) return na - nb;
        }
        return 0;
    }
}
