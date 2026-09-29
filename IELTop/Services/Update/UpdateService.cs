using Velopack;
using Velopack.Sources;

namespace IELTop.Services.Update;

/// <summary>Outcome of one update check, ready for the UI.</summary>
public sealed record UpdateCheckResult(bool Success, bool UpdateAvailable, string Message, string Version)
{
    public static UpdateCheckResult Fail(string message) => new(false, false, message, string.Empty);
    public static UpdateCheckResult None(string message) => new(true, false, message, string.Empty);
}

public interface IUpdateService
{
    /// <summary>True when the app runs from a Velopack install. False for a dev build.</summary>
    bool IsInstalled { get; }
    string CurrentVersion { get; }

    /// <summary>Checks the update feed. Never throws.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the pending update. Call after a successful check.</summary>
    Task<UpdateCheckResult> DownloadAsync(CancellationToken ct = default);

    /// <summary>Applies a downloaded update and restarts the app.</summary>
    void ApplyAndRestart();
}

/// <summary>
/// Velopack auto-update. Reads the feed URL from settings so a developer,
/// school, or company can point the app at their own release host. When the
/// app is a plain dev build, or no feed is set, every call reports quietly
/// and the app keeps working.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly Storage.ISettingsStore _settings;
    private UpdateManager? _manager;
    private UpdateManager? _defaultManager;
    private UpdateInfo? _pending;

    public UpdateService(Storage.ISettingsStore settings)
    {
        _settings = settings;
    }

    private string FeedUrl => _settings.Current.UpdateFeedUrl;

    /// <summary>
    /// A manager with no feed, only to read install state and current version.
    /// Velopack exposes these on the instance, not as statics.
    /// </summary>
    private UpdateManager? DefaultManager()
    {
        if (_defaultManager is not null) return _defaultManager;
        try
        {
            _defaultManager = new UpdateManager();
        }
        catch (Exception)
        {
            // A dev build or a broken install reports as not installed below.
            return null;
        }
        return _defaultManager;
    }

    public bool IsInstalled => DefaultManager()?.IsInstalled == true;

    public string CurrentVersion
    {
        get
        {
            var manager = DefaultManager();
            if (manager?.IsInstalled == true && manager.CurrentVersion is not null)
                return manager.CurrentVersion.ToString();
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    private UpdateManager? Manager()
    {
        if (string.IsNullOrWhiteSpace(FeedUrl)) return null;
        if (_manager is not null) return _manager;
        try
        {
            // GitHub Releases feeds use the velopack source too; an https URL
            // pointing at a folder of release files works with SimpleWebSource.
            _manager = FeedUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(FeedUrl, accessToken: null, prerelease: false))
                : new UpdateManager(new SimpleWebSource(FeedUrl));
            return _manager;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
            return UpdateCheckResult.None("This is a development build. Auto update works only after a Velopack install.");
        var manager = Manager();
        if (manager is null)
            return UpdateCheckResult.None("No update feed set. Add one in Settings to check for updates.");

        try
        {
            _pending = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            return _pending is null
                ? UpdateCheckResult.None($"You have the latest version ({CurrentVersion}).")
                : new UpdateCheckResult(true, true,
                    $"Version {_pending.TargetFullRelease.Version} is available.", 
                    _pending.TargetFullRelease.Version.ToString());
        }
        catch (Exception)
        {
            return UpdateCheckResult.Fail(
                "Could not reach the update server. Check the feed URL and your connection.");
        }
    }

    public async Task<UpdateCheckResult> DownloadAsync(CancellationToken ct = default)
    {
        var manager = Manager();
        if (manager is null || _pending is null)
            return UpdateCheckResult.None("Run a check first to find an update.");

        try
        {
            await manager.DownloadUpdatesAsync(_pending, progress: null, cancelToken: ct)
                .ConfigureAwait(false);
            return new UpdateCheckResult(true, true,
                $"Version {_pending.TargetFullRelease.Version} downloaded. Restart to finish.",
                _pending.TargetFullRelease.Version.ToString());
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Fail("The download was stopped.");
        }
        catch (Exception)
        {
            return UpdateCheckResult.Fail("The update could not be downloaded. Try again later.");
        }
    }

    public void ApplyAndRestart()
    {
        var manager = Manager();
        if (manager is null || _pending is null) return;
        try
        {
            manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception)
        {
            // If the restart fails, the app keeps working on the old version.
        }
    }
}
