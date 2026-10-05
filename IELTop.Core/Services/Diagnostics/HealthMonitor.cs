using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace IELTop.Services.Diagnostics;

/// <summary>
/// Counters that answer "is anything wrong?" without reading the whole log: how
/// many bridge calls ran, how many failed, how long the slowest took, how many
/// unhandled errors were seen. The Diagnostics tab shows them, so one look says
/// whether the session is healthy.
/// </summary>
public static class HealthMonitor
{
    private sealed class Counter
    {
        public long Count;
        public long Failed;
        public long TotalMs;
        public long MaxMs;
        public string LastError = string.Empty;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Counter> Calls = new(StringComparer.Ordinal);
    private static long _unhandled;
    private static long _logged;
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    public static long UnhandledErrors => Interlocked.Read(ref _unhandled);
    public static long LoggedLines => Interlocked.Read(ref _logged);
    public static TimeSpan UpFor => Uptime.Elapsed;

    public static void CountLine() => Interlocked.Increment(ref _logged);
    public static void CountUnhandled() => Interlocked.Increment(ref _unhandled);

    /// <summary>Records one bridge call result. Milliseconds is the wall time.</summary>
    public static void RecordCall(string method, long ms, bool failed, string? error)
    {
        lock (Gate)
        {
            if (!Calls.TryGetValue(method, out var counter))
            {
                counter = new Counter();
                Calls[method] = counter;
            }
            counter.Count++;
            counter.TotalMs += ms;
            if (ms > counter.MaxMs) counter.MaxMs = ms;
            if (failed)
            {
                counter.Failed++;
                if (!string.IsNullOrEmpty(error)) counter.LastError = error!;
            }
        }
    }

    public sealed record CallStat(string Method, long Count, long Failed, long AvgMs, long MaxMs, string LastError);

    /// <summary>The calls seen this session, slowest first, so a problem stands out.</summary>
    public static IReadOnlyList<CallStat> Snapshot(int max = 50)
    {
        lock (Gate)
        {
            return Calls
                .Select(kv => new CallStat(
                    kv.Key,
                    kv.Value.Count,
                    kv.Value.Failed,
                    kv.Value.Count == 0 ? 0 : kv.Value.TotalMs / kv.Value.Count,
                    kv.Value.MaxMs,
                    kv.Value.LastError))
                .OrderByDescending(s => s.MaxMs)
                .ThenByDescending(s => s.Failed)
                .Take(max)
                .ToList();
        }
    }

    public static void Reset()
    {
        lock (Gate) { Calls.Clear(); }
    }

    /// <summary>
    /// Totals across every method seen, not just the ones shown in the list. The
    /// displayed table is capped, so summing that would undercount once more than
    /// a few dozen routes have run.
    /// </summary>
    public static (long Calls, long Failures, int Methods) Totals()
    {
        lock (Gate)
        {
            long calls = 0, failures = 0;
            foreach (var counter in Calls.Values)
            {
                calls += counter.Count;
                failures += counter.Failed;
            }
            return (calls, failures, Calls.Count);
        }
    }
}
