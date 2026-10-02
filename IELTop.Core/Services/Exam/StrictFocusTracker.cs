namespace IELTop.Services.Exam;

/// <summary>
/// Tracks focus changes for strict mode and decides when one becomes a
/// violation. Pure and time injected, so a test drives it without a window or a
/// real clock. The engine owns an instance and feeds it host events.
/// </summary>
public sealed class StrictFocusTracker
{
    private readonly StrictModePolicy _policy;
    private DateTime? _lostAt;

    public StrictFocusTracker(StrictModePolicy policy) => _policy = policy;

    /// <summary>True while the window is known to be out of the foreground.</summary>
    public bool IsAway => _lostAt is not null;

    public StrictModePolicy Policy => _policy;

    /// <summary>
    /// Feeds one focus event. Returns true when it should count as a violation.
    /// A loss only counts once the grace period has passed, and a return inside
    /// the grace period is forgiven. A minimize counts at once.
    /// </summary>
    public bool Observe(ExamFocusEvent kind, DateTime now)
    {
        switch (kind)
        {
            case ExamFocusEvent.Lost:
                _lostAt ??= now;
                return false;
            case ExamFocusEvent.Regained:
                _lostAt = null;
                return false;
            case ExamFocusEvent.Minimized:
                _lostAt = now;
                return _policy.CountFocusLoss;
            default:
                return false;
        }
    }

    /// <summary>
    /// Called on a clock tick. Returns true at most once per grace period while
    /// the window stays away, so a long switch away is counted, not spammed.
    /// </summary>
    public bool Tick(DateTime now)
    {
        if (_lostAt is not { } lostAt) return false;
        if (!_policy.CountFocusLoss) return false;
        if (now - lostAt < _policy.GracePeriod) return false;
        _lostAt = now;
        return true;
    }

    /// <summary>Forgets any pending loss, for example when the run ends.</summary>
    public void Reset() => _lostAt = null;
}
