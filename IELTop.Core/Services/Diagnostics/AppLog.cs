using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace IELTop.Services.Diagnostics;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Off = 5,
}

/// <summary>
/// The one log the whole app writes to. It keeps a session file per launch plus
/// a "latest.log" that is always the newest run, rotates old files, and mirrors
/// to the debugger and stderr so a run under a debugger shows the same lines.
///
/// Rules that matter here:
/// - Logging must never break the app, so every write is wrapped and a file
///   error is counted, not thrown.
/// - A crash must leave the last lines on disk, so each write is flushed.
/// - The file goes under the user profile: an install folder is not writable
///   without elevation, and the log must still be written there.
///
/// The level comes from the IELTOP_LOG environment variable (trace, debug, info,
/// warn, error) and can be changed at runtime from the Diagnostics tab.
/// </summary>
public static class AppLog
{
    private const int KeepFiles = 12;

    private static readonly object Gate = new();
    private static readonly List<string> Pending = new();
    private static StreamWriter? _writer;
    private static StreamWriter? _latestWriter;
    private static string _dir = string.Empty;
    private static string _sessionFile = string.Empty;
    private static string _latestFile = string.Empty;
    private static bool _initialized;
    private static long _fileErrors;

    public static LogLevel Level { get; private set; } = DefaultLevel();
    public static string SessionId { get; private set; } = "not-started";
    public static string LogDirectory => _dir;
    public static string SessionFile => _sessionFile;
    public static string LatestFile => _latestFile;
    public static long FileErrors => Interlocked.Read(ref _fileErrors);

    private static LogLevel DefaultLevel()
    {
        var raw = Environment.GetEnvironmentVariable("IELTOP_LOG");
        if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }
#if DEBUG
        return LogLevel.Debug;
#else
        return LogLevel.Info;
#endif
    }

    /// <summary>Opens the session file, prunes old ones, and writes a header.</summary>
    public static void Initialize(string appName, string version)
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                _dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IELTop", "logs");
                Directory.CreateDirectory(_dir);

                SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                _sessionFile = Path.Combine(_dir, $"ieltop-{SessionId}.log");
                _latestFile = Path.Combine(_dir, "latest.log");

                var writer = new StreamWriter(new FileStream(
                    _sessionFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true,
                };
                _writer = writer;

                // latest.log is the newest run, so a report can always point at
                // one path without knowing the timestamp of the session.
                var latest = new StreamWriter(new FileStream(
                    _latestFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = true,
                };
                _latestWriter = latest;

                PruneOldFiles();

                writer.WriteLine($"# {appName} {version}");
                writer.WriteLine($"# session {SessionId} started {DateTimeOffset.Now:u}");
                writer.WriteLine($"# level {Level}");
                writer.WriteLine($"# os {Environment.OSVersion}, runtime {Environment.Version}, pid {Environment.ProcessId}");
                writer.WriteLine(new string('-', 72));
                latest.WriteLine($"# {appName} {version}");
                latest.WriteLine($"# session {SessionId} started {DateTimeOffset.Now:u}");
                latest.WriteLine($"# level {Level}");
                latest.WriteLine(new string('-', 72));
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _fileErrors);
                System.Diagnostics.Debug.WriteLine($"[AppLog] could not open log: {ex.Message}");
                _writer = null;
            }
        }
    }

    /// <summary>Changes the level at runtime from the Diagnostics tab.</summary>
    public static void SetLevel(LogLevel level)
    {
        Level = level;
        Info("Log", $"Log level changed to {level}.");
    }

    public static LogLevel ParseLevel(string? value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed) && parsed != LogLevel.Off
            ? parsed
            : DefaultLevel();

    public static void Trace(string category, string message) => Write(LogLevel.Trace, category, message, null);
    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);
    public static void Info(string category, string message) => Write(LogLevel.Info, category, message, null);
    public static void Warn(string category, string message, Exception? ex = null) => Write(LogLevel.Warn, category, message, ex);
    public static void Error(string category, string message, Exception? ex = null) => Write(LogLevel.Error, category, message, ex);

    public static void Write(LogLevel level, string category, string message, Exception? ex = null)
    {
        if (level < Level) return;

        HealthMonitor.CountLine();
        var line = Format(level, category, message, ex);

        // Mirror to the debugger and stderr so a debugger run shows the same
        // lines the file gets. Never at Info to avoid console noise in release.
        System.Diagnostics.Debug.WriteLine(line);
        if (level >= LogLevel.Warn)
        {
            Console.Error.WriteLine(line);
        }

        lock (Gate)
        {
            if (_writer is null)
            {
                // The file failed to open; keep a small buffer so the first
                // lines survive if it opens later, but do not grow without end.
                if (Pending.Count < 200) Pending.Add(line);
                return;
            }

            try
            {
                while (Pending.Count > 0)
                {
                    var pending = Pending[0];
                    _writer.WriteLine(pending);
                    _latestWriter?.WriteLine(pending);
                    Pending.RemoveAt(0);
                }
                _writer.WriteLine(line);
                _latestWriter?.WriteLine(line);
            }
            catch (Exception writeEx)
            {
                Interlocked.Increment(ref _fileErrors);
                System.Diagnostics.Debug.WriteLine($"[AppLog] write failed: {writeEx.Message}");
            }
        }
    }

    private static string Format(LogLevel level, string category, string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.Append(DateTimeOffset.Now.ToString("HH:mm:ss.fff"));
        sb.Append(' ').Append(level.ToString().ToUpperInvariant().PadRight(5));
        sb.Append(" [").Append(Thread.CurrentThread.ManagedThreadId.ToString().PadLeft(3)).Append("] ");
        sb.Append(category).Append(": ").Append(message);
        if (ex is not null)
        {
            sb.AppendLine();
            sb.Append("      ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                foreach (var frame in ex.StackTrace!.Split('\n'))
                {
                    sb.AppendLine();
                    sb.Append("      ").Append(frame.TrimEnd());
                }
            }
            if (ex.InnerException is not null)
            {
                sb.AppendLine();
                sb.Append("      caused by ").Append(ex.InnerException.GetType().FullName)
                  .Append(": ").Append(ex.InnerException.Message);
            }
        }
        return sb.ToString();
    }

    /// <summary>Reads the last lines of the latest log, for the Diagnostics tab.</summary>
    public static string Tail(int lines = 200)
    {
        try
        {
            var path = _latestFile;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;

            // The app holds latest.log open for writing the whole session, so the
            // reader has to permit that writer or it fails with a sharing
            // violation and the tab shows an error instead of the log.
            var all = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = reader.ReadLine()) is not null) all.Add(line);
            }

            var take = Math.Max(1, Math.Min(lines, all.Count));
            return string.Join(Environment.NewLine, all.Skip(all.Count - take));
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _fileErrors);
            return $"Could not read the log: {ex.Message}";
        }
    }

    /// <summary>Copies the latest log to a place the user picked, for a report.</summary>
    public static bool Export(string targetPath)
    {
        try
        {
            if (!File.Exists(_latestFile)) return false;

            // Same sharing rule as Tail: read the live file without needing the
            // writer to close it first.
            using (var source = new FileStream(_latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var destination = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                source.CopyTo(destination);
            }
            return true;
        }
        catch (Exception ex)
        {
            Error("Log", "Could not export the log.", ex);
            return false;
        }
    }

    public static void Flush()
    {
        lock (Gate)
        {
            try { _writer?.Flush(); } catch { /* never throws */ }
            try { _latestWriter?.Flush(); } catch { /* never throws */ }
        }
    }

    private static void PruneOldFiles()
    {
        try
        {
            var files = new DirectoryInfo(_dir).GetFiles("ieltop-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepFiles)
                .ToList();
            foreach (var file in files)
            {
                try { file.Delete(); } catch { /* a locked old file is fine */ }
            }
        }
        catch { /* pruning is best effort */ }
    }
}
