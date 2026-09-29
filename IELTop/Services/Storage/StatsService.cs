using IELTop.Data;
using Microsoft.EntityFrameworkCore;

namespace IELTop.Services.Storage;

/// <summary>
/// Real numbers for the dashboard, read straight from the local database.
/// Nothing here is invented: an empty database reports zero.
/// </summary>
public sealed record DashboardStats(
    int WordCount,
    int WordsDueToday,
    int SpeakingAttempts,
    double AverageAccuracy,
    double BestAccuracy,
    int ModelsReady,
    int ModelsTotal,
    bool LlmConfigured,
    string LlmModel,
    int ExamAttempts,
    string LastBandLabel);

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
        int words = 0, due = 0, attempts = 0, exams = 0;
        double average = 0, best = 0;
        string lastBand = "No test yet";

        try
        {
            using var db = new AppDbContext();
            words = db.Words.Count();

            var today = DateTime.UtcNow.Date;
            due = db.StudyRecords.Count(r => r.NextReview <= today);

            // Aggregate in SQL so a long history never loads row by row.
            var spoken = db.SpeakingAttempts
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Avg = g.Average(s => s.Accuracy), Max = g.Max(s => s.Accuracy) })
                .FirstOrDefault();
            if (spoken is not null)
            {
                attempts = spoken.Count;
                average = Math.Round(spoken.Avg, 1);
                best = Math.Round(spoken.Max, 1);
            }

            exams = db.ExamAttempts.Count();
            var last = db.ExamAttempts
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.BandLow, a.BandHigh })
                .FirstOrDefault();
            if (last is not null)
                lastBand = $"{last.BandLow:0.0} to {last.BandHigh:0.0}";
        }
        catch (Exception)
        {
            // A first run may have no database yet. Zeroes are the honest answer.
        }

        var slots = Ai.OnnxModelRegistry.Slots;
        int ready = slots.Count(s => Ai.OnnxModelRegistry.IsComplete(s.Name));

        return new DashboardStats(
            words, due, attempts, average, best,
            ready, slots.Count,
            !string.IsNullOrWhiteSpace(_settings.Current.LlmModel),
            _settings.Current.LlmModel,
            exams, lastBand);
    }
}
