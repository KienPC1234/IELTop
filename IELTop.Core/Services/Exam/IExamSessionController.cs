namespace IELTop.Services.Exam;

/// <summary>
/// A signal that the test window left the foreground, came back, or was
/// minimized. The engine owns the strict mode decision; the host only reports
/// what the operating system did.
/// </summary>
public enum ExamFocusEvent
{
    Lost,
    Regained,
    Minimized,
}

/// <summary>
/// The host side of a running test: what the engine needs from the window but
/// must not know how to do. Core defines this port so the engine stays free of
/// Photino, Win32, or any platform API, and so a test can drive strict mode
/// with a fake.
/// </summary>
public interface IExamSessionController
{
    /// <summary>True when the host can force the window full screen and on top.</summary>
    bool SupportsFullscreen { get; }

    /// <summary>Enters full screen for the test. No-op when unsupported.</summary>
    void EnterFullscreen();

    /// <summary>Leaves full screen when the test ends. No-op when unsupported.</summary>
    void ExitFullscreen();

    /// <summary>Brings the test window back to the front.</summary>
    void Focus();

    /// <summary>Keeps the test above other windows, or releases it.</summary>
    void SetAlwaysOnTop(bool on);

    /// <summary>Raised by the host when the window focus or state changes.</summary>
    event Action<ExamFocusEvent>? FocusChanged;
}

/// <summary>
/// The default controller for hosts with no window: a headless run, a unit
/// test, or a build without the native shell. Every call is a no-op and no
/// focus event ever fires, so strict mode still counts nothing and never
/// blocks the run.
/// </summary>
public sealed class NullExamSessionController : IExamSessionController
{
    public static readonly NullExamSessionController Instance = new();

    public bool SupportsFullscreen => false;
    public void EnterFullscreen() { }
    public void ExitFullscreen() { }
    public void Focus() { }
    public void SetAlwaysOnTop(bool on) { }
    public event Action<ExamFocusEvent>? FocusChanged
    {
        add { }
        remove { }
    }
}
