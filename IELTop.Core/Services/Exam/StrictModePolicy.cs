using System.Globalization;

namespace IELTop.Services.Exam;

/// <summary>How hard strict mode tries to keep the student inside the test.</summary>
public enum StrictLevel
{
    /// <summary>No enforcement. Leaving the window is allowed and not counted.</summary>
    Off = 0,

    /// <summary>Full screen and always on top, but leaving is only noted, not punished.</summary>
    Warn = 1,

    /// <summary>Full screen, always on top, and every focus loss is counted.</summary>
    Enforce = 2,
}

/// <summary>The behavior a strict level asks the host for. Pure data, no side effects.</summary>
public sealed record StrictModePolicy(
    StrictLevel Level,
    bool ForceFullscreen,
    bool AlwaysOnTop,
    bool CountFocusLoss,
    TimeSpan GracePeriod)
{
    /// <summary>
    /// A short grace window so an alt-tab that bounces straight back does not
    /// count as leaving. An alt-tab usually returns within a moment; a real
    /// switch away lasts longer.
    /// </summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMilliseconds(700);

    public static StrictModePolicy For(StrictLevel level) => level switch
    {
        StrictLevel.Enforce => new(level, true, true, true, DefaultGrace),
        StrictLevel.Warn => new(level, true, true, false, DefaultGrace),
        _ => new(StrictLevel.Off, false, false, false, TimeSpan.Zero),
    };

    /// <summary>Reads the marking level string used by the setup screen.</summary>
    public static StrictLevel ParseLevel(string? value) => value switch
    {
        "Off" => StrictLevel.Off,
        "Warn" => StrictLevel.Warn,
        "Enforce" or "Strict" => StrictLevel.Enforce,
        _ => StrictLevel.Enforce,
    };

    public static string Label(StrictLevel level) => level switch
    {
        StrictLevel.Off => "Off",
        StrictLevel.Warn => "Warn",
        _ => "Enforce",
    };

    /// <summary>The short line shown under the strict mode switch.</summary>
    public string Describe(bool hostSupportsFullscreen)
    {
        var forced = ForceFullscreen && hostSupportsFullscreen
            ? "Full screen and always on top are on."
            : ForceFullscreen
                ? "Full screen is requested, but this host cannot force it."
                : "Full screen stays as you set it.";
        return Level switch
        {
            StrictLevel.Off => "Strict mode is off. You can leave the test freely.",
            StrictLevel.Warn => $"{forced} Leaving is noted but not counted when you come back quickly.",
            _ => $"{forced} Leaving for more than {GracePeriod.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s is counted as a violation.",
        };
    }
}
