namespace IELTop.Services.Update;

/// <summary>Outcome of one update check, ready for the UI.</summary>
public sealed record UpdateCheckResult(bool Success, bool UpdateAvailable, string Message, string Version)
{
    public static UpdateCheckResult Fail(string message) => new(false, false, message, string.Empty);
    public static UpdateCheckResult None(string message) => new(true, false, message, string.Empty);
}

/// <summary>
/// Checks for a newer release. The updater mechanism is host specific, so
/// Core only describes what the UI needs and each host implements it.
/// </summary>
public interface IUpdateService
{
    /// <summary>True when the app runs from a packaged install. False for a dev build.</summary>
    bool IsInstalled { get; }
    string CurrentVersion { get; }

    /// <summary>Checks the update feed. Never throws.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the pending update. Call after a successful check.</summary>
    Task<UpdateCheckResult> DownloadAsync(CancellationToken ct = default);

    /// <summary>Applies a downloaded update and restarts the app.</summary>
    void ApplyAndRestart();
}
