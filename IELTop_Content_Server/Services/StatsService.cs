using System.Collections.Concurrent;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Counts what the server serves. Counters stay in memory and are
/// flushed to the store on a timer, so a download never waits on a
/// database write.
/// </summary>
public interface IStatsService
{
    void RecordApiCall();
    void RecordPaperDownload();
    void RecordAudioServed();
    void RecordSubmission();
    Task<DailyStat> TodayAsync(CancellationToken ct = default);
    Task<List<DailyStat>> RecentAsync(int days, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}

public sealed class StatsService : IStatsService
{
    private long _apiCalls;
    private long _paperDownloads;
    private long _audioServed;
    private long _submissions;
    private volatile DailyStat? _today;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public StatsService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public void RecordApiCall() => Interlocked.Increment(ref _apiCalls);
    public void RecordPaperDownload() => Interlocked.Increment(ref _paperDownloads);
    public void RecordAudioServed() => Interlocked.Increment(ref _audioServed);
    public void RecordSubmission() => Interlocked.Increment(ref _submissions);

    public async Task<DailyStat> TodayAsync(CancellationToken ct = default)
    {
        var stat = await ReadTodayAsync(ct);
        lock (this)
        {
            stat.Downloads += Interlocked.Read(ref _paperDownloads);
            stat.AudioServed += Interlocked.Read(ref _audioServed);
            stat.ApiCalls += Interlocked.Read(ref _apiCalls);
            stat.Submissions += Interlocked.Read(ref _submissions);
        }
        return stat;
    }

    public async Task<List<DailyStat>> RecentAsync(int days, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.DailyStats
            .OrderByDescending(s => s.Day)
            .Take(Math.Clamp(days, 1, 90))
            .AsNoTracking()
            .ToListAsync(ct);

        var today = await TodayAsync(ct);
        bool hasToday = rows.Any(r => r.Day == today.Day);
        if (!hasToday)
            rows.Insert(0, today);
        return rows;
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        long api = Interlocked.Read(ref _apiCalls);
        long papers = Interlocked.Read(ref _paperDownloads);
        long audio = Interlocked.Read(ref _audioServed);
        long subs = Interlocked.Read(ref _submissions);
        if (api == 0 && papers == 0 && audio == 0 && subs == 0)
            return;

        if (!await _flushGate.WaitAsync(0, ct))
            return;
        try
        {
            string day = Day();
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var row = await db.DailyStats.FirstOrDefaultAsync(s => s.Day == day, ct);
            if (row is null)
            {
                row = new DailyStat { Day = day };
                db.DailyStats.Add(row);
            }

            row.Downloads += papers;
            row.AudioServed += audio;
            row.ApiCalls += api;
            row.Submissions += subs;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            Interlocked.Add(ref _apiCalls, -api);
            Interlocked.Add(ref _paperDownloads, -papers);
            Interlocked.Add(ref _audioServed, -audio);
            Interlocked.Add(ref _submissions, -subs);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task<DailyStat> ReadTodayAsync(CancellationToken ct)
    {
        string day = Day();
        if (_today is { } cached && cached.Day == day)
            return new DailyStat
            {
                Day = cached.Day,
                Downloads = cached.Downloads,
                AudioServed = cached.AudioServed,
                ApiCalls = cached.ApiCalls,
                Submissions = cached.Submissions
            };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.DailyStats.AsNoTracking().FirstOrDefaultAsync(s => s.Day == day, ct)
            ?? new DailyStat { Day = day };
        _today = row;
        return new DailyStat
        {
            Day = row.Day,
            Downloads = row.Downloads,
            AudioServed = row.AudioServed,
            ApiCalls = row.ApiCalls,
            Submissions = row.Submissions
        };
    }

    private static string Day() => DateTime.UtcNow.ToString("yyyy-MM-dd");
}

/// <summary>
/// Flushes the in memory counters to the store on a timer.
/// </summary>
public sealed class StatsFlushService(
    IStatsService stats,
    ILogger<StatsFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                await stats.FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Stats flush failed.");
            }
        }
    }
}
