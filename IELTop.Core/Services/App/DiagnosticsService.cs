using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using IELTop.Services.Diagnostics;

namespace IELTop.Services.App;

public sealed record DiagnosticsSnapshot
{
    public string Version { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string Level { get; init; } = "Info";
    public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();
    public string LogDirectory { get; init; } = string.Empty;
    public string LatestFile { get; init; } = string.Empty;
    public string SessionFile { get; init; } = string.Empty;
    public long LoggedLines { get; init; }
    public long UnhandledErrors { get; init; }
    public long FileErrors { get; init; }
    public string UptimeLabel { get; init; } = string.Empty;
    public long WorkingSetMb { get; init; }
    public string DatabasePath { get; init; } = string.Empty;
    public string DatabaseSizeLabel { get; init; } = "-";
    public string DatabaseIntegrity { get; init; } = string.Empty;
    public int BridgeMethods { get; init; }
    public long BridgeCalls { get; init; }
    public long BridgeFailures { get; init; }
    public IReadOnlyList<HealthMonitor.CallStat> SlowCalls { get; init; } = Array.Empty<HealthMonitor.CallStat>();
    public string LogTail { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>True when nothing needs attention: no unhandled error, no failed call.</summary>
    public bool IsHealthy => UnhandledErrors == 0 && BridgeFailures == 0 && FileErrors == 0;
}

/// <summary>
/// One place that answers "what is going on" for the Diagnostics tab: the log
/// tail, the counters, the database check, and the slowest bridge calls. It does
/// not run any model and does not change settings, so it is safe to open any
/// time a session looks wrong.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly string _version;

    public DiagnosticsService(string version)
    {
        _version = version;
    }

    public DiagnosticsSnapshot Snapshot(int tailLines = 200, bool checkDatabase = false)
    {
        var stats = HealthMonitor.Snapshot();
        var totals = HealthMonitor.Totals();

        var dbPath = Data.AppDbContext.DefaultDatabasePath();
        string dbSizeLabel = "-";
        try
        {
            var info = new FileInfo(dbPath);
            if (info.Exists)
            {
                dbSizeLabel = info.Length >= 1048576
                    ? $"{info.Length / 1048576} MB"
                    : $"{Math.Max(1, info.Length / 1024)} KB";
            }
        }
        catch { /* the size is cosmetic */ }

        string integrity = "not checked";
        if (checkDatabase)
        {
            try
            {
                integrity = Data.AppDbContext.CheckIntegrityAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                integrity = $"error: {ex.Message}";
            }
        }

        long workingSetMb = 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            workingSetMb = process.WorkingSet64 / 1048576;
        }
        catch { /* cosmetic */ }

        return new DiagnosticsSnapshot
        {
            Version = _version,
            SessionId = AppLog.SessionId,
            Level = AppLog.Level.ToString(),
            Levels = new[] { "Trace", "Debug", "Info", "Warn", "Error" },
            LogDirectory = AppLog.LogDirectory,
            LatestFile = AppLog.LatestFile,
            SessionFile = AppLog.SessionFile,
            LoggedLines = HealthMonitor.LoggedLines,
            UnhandledErrors = HealthMonitor.UnhandledErrors,
            FileErrors = AppLog.FileErrors,
            UptimeLabel = FormatUptime(HealthMonitor.UpFor),
            WorkingSetMb = workingSetMb,
            DatabasePath = dbPath,
            DatabaseSizeLabel = dbSizeLabel,
            DatabaseIntegrity = integrity,
            BridgeMethods = totals.Methods,
            BridgeCalls = totals.Calls,
            BridgeFailures = totals.Failures,
            SlowCalls = stats,
            LogTail = AppLog.Tail(tailLines),
        };
    }

    public DiagnosticsSnapshot SetLevel(string level)
    {
        AppLog.SetLevel(AppLog.ParseLevel(level));
        return Snapshot();
    }

    /// <summary>Writes a marker line, so "what I did" stands out in the log.</summary>
    public DiagnosticsSnapshot Mark(string note)
    {
        AppLog.Info("user", string.IsNullOrWhiteSpace(note) ? "(empty marker)" : note.Trim());
        return Snapshot();
    }

    public DiagnosticsSnapshot Reload() => Snapshot();

    /// <summary>Runs the database integrity check and returns the result.</summary>
    public DiagnosticsSnapshot CheckDatabase() => Snapshot(checkDatabase: true);

    public DiagnosticsSnapshot Export(string targetPath)
    {
        var path = string.IsNullOrWhiteSpace(targetPath) ? DefaultExportPath() : targetPath.Trim();
        var ok = AppLog.Export(path);
        return Snapshot() with
        {
            StatusMessage = ok ? $"Log exported to {path}" : "Could not export the log.",
        };
    }

    /// <summary>A dated copy under Documents, so an export is easy to find.</summary>
    private static string DefaultExportPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "IELTop-logs");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"ieltop-{AppLog.SessionId}.log");
    }

    private static string FormatUptime(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s"
            : $"{span.Minutes}m {span.Seconds}s";
}
