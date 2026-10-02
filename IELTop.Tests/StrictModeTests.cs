using IELTop.Services.Exam;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The strict mode rule is pure data plus a time injected tracker, so it is
/// tested here without a window, a clock, or a running test.
/// </summary>
public sealed class StrictModePolicyTests
{
    [Fact]
    public void Off_policy_enforces_nothing()
    {
        var policy = StrictModePolicy.For(StrictLevel.Off);
        Assert.False(policy.ForceFullscreen);
        Assert.False(policy.AlwaysOnTop);
        Assert.False(policy.CountFocusLoss);
    }

    [Fact]
    public void Warn_policy_forces_fullscreen_but_does_not_count()
    {
        var policy = StrictModePolicy.For(StrictLevel.Warn);
        Assert.True(policy.ForceFullscreen);
        Assert.True(policy.AlwaysOnTop);
        Assert.False(policy.CountFocusLoss);
    }

    [Fact]
    public void Enforce_policy_counts_focus_loss()
    {
        var policy = StrictModePolicy.For(StrictLevel.Enforce);
        Assert.True(policy.ForceFullscreen);
        Assert.True(policy.CountFocusLoss);
        Assert.True(policy.GracePeriod > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("Off", StrictLevel.Off)]
    [InlineData("Warn", StrictLevel.Warn)]
    [InlineData("Enforce", StrictLevel.Enforce)]
    [InlineData("Strict", StrictLevel.Enforce)]
    [InlineData("something else", StrictLevel.Enforce)]
    [InlineData(null, StrictLevel.Enforce)]
    public void ParseLevel_maps_labels(string? value, StrictLevel expected)
        => Assert.Equal(expected, StrictModePolicy.ParseLevel(value));

    [Fact]
    public void Labels_round_trip()
    {
        foreach (var level in new[] { StrictLevel.Off, StrictLevel.Warn, StrictLevel.Enforce })
            Assert.Equal(level, StrictModePolicy.ParseLevel(StrictModePolicy.Label(level)));
    }

    [Fact]
    public void Describe_says_when_the_host_cannot_enforce()
    {
        var enforce = StrictModePolicy.For(StrictLevel.Enforce);
        Assert.Contains("cannot force", enforce.Describe(hostSupportsFullscreen: false));
        Assert.Contains("always on top", enforce.Describe(hostSupportsFullscreen: true));
    }
}

/// <summary>The focus debounce: a quick alt-tab is forgiven, a long one counts.</summary>
public sealed class StrictFocusTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static StrictFocusTracker Enforce() =>
        new(StrictModePolicy.For(StrictLevel.Enforce));

    [Fact]
    public void Quick_return_inside_grace_is_forgiven()
    {
        var tracker = Enforce();
        Assert.False(tracker.Observe(ExamFocusEvent.Lost, T0));
        Assert.False(tracker.Observe(ExamFocusEvent.Regained, T0.AddMilliseconds(200)));
        // A tick after the (already ended) loss never counts.
        Assert.False(tracker.Tick(T0.AddSeconds(5)));
        Assert.False(tracker.IsAway);
    }

    [Fact]
    public void Staying_away_past_grace_counts_once()
    {
        var tracker = Enforce();
        tracker.Observe(ExamFocusEvent.Lost, T0);
        Assert.False(tracker.Tick(T0.AddMilliseconds(300)));
        Assert.True(tracker.Tick(T0.AddSeconds(2)));
        // Not again immediately, only after another grace window.
        Assert.False(tracker.Tick(T0.AddSeconds(2).AddMilliseconds(100)));
        Assert.True(tracker.Tick(T0.AddSeconds(4)));
    }

    [Fact]
    public void Minimize_counts_at_once()
    {
        var tracker = Enforce();
        Assert.True(tracker.Observe(ExamFocusEvent.Minimized, T0));
    }

    [Fact]
    public void Warn_policy_never_counts()
    {
        var tracker = new StrictFocusTracker(StrictModePolicy.For(StrictLevel.Warn));
        tracker.Observe(ExamFocusEvent.Lost, T0);
        Assert.False(tracker.Tick(T0.AddSeconds(10)));
        Assert.False(tracker.Observe(ExamFocusEvent.Minimized, T0));
    }

    [Fact]
    public void Reset_clears_a_pending_loss()
    {
        var tracker = Enforce();
        tracker.Observe(ExamFocusEvent.Lost, T0);
        tracker.Reset();
        Assert.False(tracker.IsAway);
        Assert.False(tracker.Tick(T0.AddSeconds(30)));
    }
}
