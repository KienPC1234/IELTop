using System.Globalization;
using IELTop.Data;
using Microsoft.EntityFrameworkCore;

namespace IELTop.Services.Storage;

/// <summary>One day of study activity: exam runs plus speaking attempts.</summary>
public sealed record ActivityDay(DateTime Date, int Count);

/// <summary>One finished test as a point on the band trend chart.</summary>
public sealed record BandPoint(string Label, double BandLow, double BandHigh);

/// <summary>
/// Real numbers for the dashboard, read straight from the local database.
/// Nothing here is invented: an empty database reports zero.
/// </summary>
public sealed record DashboardStats(
    int ModelsReady,
    int ModelsTotal,
    bool LlmConfigured,
    string LlmModel,
    int ExamAttempts,
    string LastBandLabel,
    int StreakDays,
    bool ActiveToday,
    IReadOnlyList<ActivityDay> WeeklyActivity,
    IReadOnlyList<BandPoint> BandTrend);

public interface IStatsService
{
    DashboardStats Build();
}

public sealed class StatsService : IStatsService
{
    private readonly ISettingsStore _settings;

    public StatsService(ISettingsStore settings)
    {
        _settings = settings;
    }

    public DashboardStats Build()
    {
        // Numbers and dates leave this service as plain English text, so the
        // dashboard reads the same on every machine, whatever its locale.
        var culture = CultureInfo.InvariantCulture;
        int exams = 0;
        string lastBand = "No test yet";
        int streak = 0;
        bool activeToday = false;
        var weekly = new List<ActivityDay>();
        var trend = new List<BandPoint>();

        try
        {
            using var db = new AppDbContext();

            var attempts = db.ExamAttempts
                .Select(a => new { a.Id, a.BandLow, a.BandHigh, a.CreatedAt })
                .ToList();
            exams = attempts.Count;

            var last = attempts.OrderByDescending(a => a.Id).FirstOrDefault();
            if (last is not null)
                lastBand = string.Create(culture, $"{last.BandLow:0.0} to {last.BandHigh:0.0}");

            // One activity day counts exam runs and speaking attempts.
            var examDays = attempts
                .GroupBy(a => a.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.Count());
            var speakingDays = db.SpeakingAttempts
                .Select(s => s.CreatedAt)
                .ToList()
                .GroupBy(d => d.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            int CountOn(DateTime day) =>
                (examDays.TryGetValue(day, out var e) ? e : 0)
                + (speakingDays.TryGetValue(day, out var s) ? s : 0);

            var today = DateTime.Now.Date;
            activeToday = CountOn(today) > 0;

            // Streak counts consecutive active days. Before the first study of
            // the day, yesterday's run is still counted, so a streak only
            // breaks after a full day is missed.
            var cursor = activeToday ? today : today.AddDays(-1);
            while (CountOn(cursor) > 0)
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                weekly.Add(new ActivityDay(day, CountOn(day)));
            }

            foreach (var a in attempts
                .OrderByDescending(a => a.CreatedAt)
                .Take(10)
                .OrderBy(a => a.CreatedAt))
            {
                trend.Add(new BandPoint(
                    a.CreatedAt.ToString("d/M", culture), a.BandLow, a.BandHigh));
            }
        }
        catch (Exception)
        {
            // A first run may have no database yet. Zeroes are the honest answer.
        }

        var slots = Ai.OnnxModelRegistry.Slots;
        int ready = slots.Count(s => Ai.OnnxModelRegistry.IsComplete(s.Name));

        return new DashboardStats(
            ready, slots.Count,
            !string.IsNullOrWhiteSpace(_settings.Current.LlmModel),
            _settings.Current.LlmModel,
            exams, lastBand,
            streak, activeToday, weekly, trend);
    }
}
