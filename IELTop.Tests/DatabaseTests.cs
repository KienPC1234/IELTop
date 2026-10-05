using IELTop.Data;
using IELTop.Models;
using Xunit;

namespace IELTop.Tests;

[Collection("AppState")]
public sealed class DatabaseTests : IDisposable
{
    private readonly string _tempDbPath;

    public DatabaseTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"ieltop_test_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_tempDbPath);
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
            var shm = _tempDbPath + "-shm";
            if (File.Exists(shm)) File.Delete(shm);
            var wal = _tempDbPath + "-wal";
            if (File.Exists(wal)) File.Delete(wal);
        }
        catch { }
    }

    [Fact]
    public async Task EnsureCreatedAsync_CreatesTablesAndPassesIntegrityCheck()
    {
        await AppDbContext.EnsureCreatedAsync();

        var integrityResult = await AppDbContext.CheckIntegrityAsync();
        Assert.Equal("ok", integrityResult);
    }

    [Fact]
    public async Task AsyncDb_InsertAndQueryAttempts_WorksCorrectly()
    {
        await AppDbContext.EnsureCreatedAsync();
        var db = AppDbContext.AsyncDb;

        var attempt = new ExamAttempt
        {
            PaperTitle = "Test Exam",
            Scope = "Reading",
            BandLow = 7.0,
            BandHigh = 7.5,
            Correct = 32,
            Total = 40,
            CreatedAt = DateTime.UtcNow
        };

        var inserted = await db.InsertAsync(attempt);
        Assert.Equal(1, inserted);
        Assert.True(attempt.Id > 0);

        var list = await db.Table<ExamAttempt>().Where(x => x.Scope == "Reading").ToListAsync();
        Assert.Single(list);
        Assert.Equal("Test Exam", list[0].PaperTitle);
        Assert.Equal(7.5, list[0].BandHigh);
    }

    [Fact]
    public async Task RunInTransactionAsync_RollsBackOnError()
    {
        await AppDbContext.EnsureCreatedAsync();
        var db = AppDbContext.AsyncDb;

        var initialCount = await db.Table<ExamAttempt>().CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await AppDbContext.RunInTransactionAsync(conn =>
            {
                conn.Insert(new ExamAttempt
                {
                    PaperTitle = "Will Rollback",
                    Scope = "Listening",
                    CreatedAt = DateTime.UtcNow
                });
                throw new InvalidOperationException("Force rollback");
            });
        });

        var afterCount = await db.Table<ExamAttempt>().CountAsync();
        Assert.Equal(initialCount, afterCount);
    }
}
