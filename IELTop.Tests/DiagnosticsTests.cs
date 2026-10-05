using System;
using System.IO;
using IELTop.Services.Diagnostics;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The log and the counters are the tools used to debug everything else, so they
/// get their own tests: a broken logger would hide the very faults it exists to
/// show.
/// </summary>
public sealed class DiagnosticsTests
{
    public DiagnosticsTests()
    {
        AppLog.Initialize("IELTop.Tests", "0.0.0");
    }

    [Fact]
    public void Log_writes_a_latest_file_that_the_tail_reads_back()
    {
        var marker = "diag-" + Guid.NewGuid().ToString("N");
        AppLog.Info("test", marker);
        AppLog.Flush();

        Assert.True(File.Exists(AppLog.LatestFile), "latest.log should exist after Initialize.");
        Assert.Contains(marker, AppLog.Tail(200));
    }

    [Fact]
    public void The_tail_and_export_work_while_the_file_is_still_open_for_writing()
    {
        var marker = "live-" + Guid.NewGuid().ToString("N");
        AppLog.Info("test", marker);
        AppLog.Flush();

        // The app keeps latest.log open for the whole session, so reading it has
        // to work without the writer closing first.
        Assert.Contains(marker, AppLog.Tail(200));

        var target = Path.Combine(Path.GetTempPath(), $"ieltop-export-{Guid.NewGuid():N}.log");
        try
        {
            Assert.True(AppLog.Export(target));
            Assert.Contains(marker, File.ReadAllText(target));
        }
        finally
        {
            try { File.Delete(target); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_message_below_the_current_level_is_dropped()
    {
        var marker = "quiet-" + Guid.NewGuid().ToString("N");
        try
        {
            AppLog.SetLevel(LogLevel.Error);
            AppLog.Info("test", marker);
            AppLog.Flush();

            Assert.DoesNotContain(marker, AppLog.Tail(500));
        }
        finally
        {
            AppLog.SetLevel(LogLevel.Trace);
        }
    }

    [Fact]
    public void The_oldest_session_files_are_pruned()
    {
        // Pruning runs at Initialize; with one session the file must still be there.
        Assert.True(File.Exists(AppLog.SessionFile));
        Assert.NotEqual("not-started", AppLog.SessionId);
    }

    [Fact]
    public void Health_monitor_totals_count_every_method_seen()
    {
        var one = "unit." + Guid.NewGuid().ToString("N") + ".one";
        var two = "unit." + Guid.NewGuid().ToString("N") + ".two";

        var before = HealthMonitor.Totals();
        HealthMonitor.RecordCall(one, 5, failed: false, error: null);
        HealthMonitor.RecordCall(one, 7, failed: true, error: "boom");
        HealthMonitor.RecordCall(two, 3, failed: false, error: null);
        var after = HealthMonitor.Totals();

        Assert.Equal(2, after.Methods - before.Methods);
        Assert.Equal(3, after.Calls - before.Calls);
        Assert.Equal(1, after.Failures - before.Failures);
    }

    [Fact]
    public void Health_monitor_keeps_the_slowest_time_per_method()
    {
        var method = "unit." + Guid.NewGuid().ToString("N");
        HealthMonitor.RecordCall(method, 4, failed: false, error: null);
        HealthMonitor.RecordCall(method, 40, failed: false, error: null);

        var stat = Array.Find(HealthMonitor.Snapshot().ToArray(), s => s.Method == method);
        Assert.NotNull(stat);
        Assert.Equal(2, stat!.Count);
        Assert.Equal(40, stat.MaxMs);
    }
}
