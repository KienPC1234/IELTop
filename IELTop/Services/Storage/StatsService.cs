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
    string LlmModel);

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
        int words = 0, due = 0, attempts = 0;
        double average = 0, best = 0;

        try
        {
            using var db = new AppDbContext();
            words = db.Words.Count();

            var today = DateTime.UtcNow.Date;
            due = db.StudyRecords.Count(r => r.NextReview <= today);

            var spoken = db.SpeakingAttempts.ToList();
            attempts = spoken.Count;
            if (attempts > 0)
            {
                average = Math.Round(spoken.Average(s => s.Accuracy), 1);
                best = Math.Round(spoken.Max(s => s.Accuracy), 1);
            }
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
            _settings.Current.LlmModel);
    }
}
