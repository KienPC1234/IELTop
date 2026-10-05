using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Services.App;
using IELTop.Services.Diagnostics;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// The Diagnostics tab: read the log tail, the counters, the database check,
/// change the log level, drop a marker, export the log. Nothing here needs a
/// model or the network, so it works when everything else looks broken.
/// </summary>
public sealed class DiagnosticsBridge
{
    private readonly DiagnosticsService _diagnostics;

    public DiagnosticsBridge(DiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void Register(BridgeRouter router)
    {
        router.Register("diagnostics.snapshot", Act(a =>
            _diagnostics.Snapshot(Int(a, "tailLines") is var n && n > 0 ? n : 200)));
        router.Register("diagnostics.checkDatabase", Act(_ => _diagnostics.CheckDatabase()));
        router.Register("diagnostics.setLevel", Act(a => _diagnostics.SetLevel(Str(a, "level"))));
        router.Register("diagnostics.mark", Act(a => _diagnostics.Mark(Str(a, "note"))));
        router.Register("diagnostics.export", Act(a => _diagnostics.Export(Str(a, "path"))));
        router.Register("diagnostics.openFolder", Act(_ =>
        {
            var folder = _diagnostics.Snapshot(tailLines: 1).LogDirectory;
            OpenFolder(folder);
            return new { folder };
        }));

        // The page reports its own uncaught errors here, so a JavaScript fault
        // lands in the same log as everything else instead of only the console.
        router.Register("diagnostics.clientError", Act(a =>
        {
            AppLog.Error("web", $"Page error: {Str(a, "message")} | source={Str(a, "source")}:{Str(a, "line")} | stack={Str(a, "stack")}");
            return new { logged = true };
        }));
        router.Register("diagnostics.clientLog", Act(a =>
        {
            AppLog.Debug("web", $"{Str(a, "level")}: {Str(a, "message")}");
            return new { logged = true };
        }));
    }

    private static Func<JsonElement?, CancellationToken, Task<object?>> Act(Func<JsonElement?, object?> handler)
        => (args, _) => Task.FromResult(handler(args));

    private static string Str(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;

    private static int Int(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception) { /* the UI still shows the path, so nothing is lost */ }
    }
}
