using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Services.Diagnostics;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// One JSON configuration for every message across the bridge, so a reply and
/// a pushed event always look the same to the web UI. Enums cross as their
/// names ("Setup"), never as numbers.
/// </summary>
public static class BridgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>One call from the web UI: a method name and a JSON argument bag.</summary>
public sealed class BridgeRequest
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("args")] public JsonElement? Args { get; set; }
}

/// <summary>
/// Thrown by a handler to send a short, user readable error to the web UI
/// instead of the generic fallback line. The message must already be plain
/// English with no technical terms.
/// </summary>
public sealed class BridgeException : Exception
{
    public BridgeException(string message) : base(message) { }
}

/// <summary>One answer back to the web UI. Exactly one of Result or Error is set.</summary>
public sealed class BridgeResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

/// <summary>
/// Routes method calls from the web UI to typed handlers. Supports direct HTTP RPC,
/// batch execution, real-time push events, and true cancellation tokens.
/// </summary>
public sealed class BridgeRouter
{
    private static readonly JsonSerializerOptions Json = BridgeJson.Options;

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers
        = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new();

    /// <summary>Global event hook for broadcasting real-time push events over the event stream.</summary>
    public event Action<string, object?>? OnEventBroadcast;

    /// <summary>
    /// Fires for every push event as well, so a host that reaches the page over
    /// JS interop can forward it. Kept separate from the stream hook because the
    /// two channels have different lifetimes: the stream outlives any page, the
    /// interop object belongs to one.
    /// </summary>
    public event Action<string, object?>? OnEventPushed;

    /// <summary>Publishes an event to all connected web UI subscribers.</summary>
    public void Broadcast(string eventName, object? payload)
    {
        OnEventBroadcast?.Invoke(eventName, payload);
        OnEventPushed?.Invoke(eventName, payload);
    }

    /// <summary>Registers one method. The name is the key the web UI calls.</summary>
    public void Register(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _handlers[method] = handler;

    /// <summary>Registers a method that takes no arguments.</summary>
    public void Register(string method, Func<CancellationToken, Task<object?>> handler)
        => Register(method, (_, ct) => handler(ct));

    /// <summary>Registers a synchronous method.</summary>
    public void Register(string method, Func<object?> handler)
        => Register(method, (_, _) => Task.FromResult(handler()));

    /// <summary>Cancels a bridge request that is still running, by its id.</summary>
    public bool CancelRequest(string requestId)
    {
        if (!string.IsNullOrEmpty(requestId) && _inFlight.TryRemove(requestId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>Executes a method directly with typed JSON arguments without double string serialization.</summary>
    public async Task<BridgeResponse> HandleDirectAsync(string id, string method, JsonElement? args, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(method))
            return new BridgeResponse { Id = id, Error = "Method name is required." };

        // The page sends this when a request times out or is aborted, so a long
        // action stops on the host instead of running to the end unseen.
        if (string.Equals(method, "bridge.cancel", StringComparison.OrdinalIgnoreCase))
        {
            var targetId = args is { } a && a.TryGetProperty("targetId", out var tid) ? tid.GetString() ?? "" : "";
            return new BridgeResponse { Id = id, Result = new { cancelled = CancelRequest(targetId) } };
        }

        if (!_handlers.TryGetValue(method, out var handler))
        {
            AppLog.Warn("bridge", $"Unknown action '{method}' from the page (id {id}).");
            HealthMonitor.RecordCall(method, 0, failed: true, $"Unknown action: {method}");
            return new BridgeResponse
            {
                Id = id,
                Error = $"Unknown action: {method}",
            };
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!string.IsNullOrEmpty(id))
            _inFlight[id] = linkedCts;

        // Every call is timed and logged: the finish line at Debug, the start
        // line (which is noisier) at Trace, and a failure at Error with the full
        // exception. So one run says what happened, and Trace adds per-call detail.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        AppLog.Trace("bridge", $"{method} start (id {id}) args={Summarize(args)}");
        try
        {
            var result = await handler(args, linkedCts.Token).ConfigureAwait(false);
            watch.Stop();
            AppLog.Debug("bridge", $"{method} ok in {watch.ElapsedMilliseconds} ms (id {id})");
            HealthMonitor.RecordCall(method, watch.ElapsedMilliseconds, failed: false, null);
            return new BridgeResponse { Id = id, Result = result };
        }
        catch (OperationCanceledException)
        {
            watch.Stop();
            AppLog.Debug("bridge", $"{method} cancelled after {watch.ElapsedMilliseconds} ms (id {id})");
            HealthMonitor.RecordCall(method, watch.ElapsedMilliseconds, failed: false, null);
            return new BridgeResponse { Id = id, Error = "The action was cancelled." };
        }
        catch (BridgeException ex)
        {
            watch.Stop();
            AppLog.Warn("bridge", $"{method} refused: {ex.Message} (id {id})");
            HealthMonitor.RecordCall(method, watch.ElapsedMilliseconds, failed: true, ex.Message);
            return new BridgeResponse { Id = id, Error = ex.Message };
        }
        catch (Exception ex)
        {
            watch.Stop();
            AppLog.Error("bridge", $"{method} failed after {watch.ElapsedMilliseconds} ms (id {id})", ex);
            HealthMonitor.RecordCall(method, watch.ElapsedMilliseconds, failed: true, ex.Message);
            return new BridgeResponse
            {
                Id = id,
                Error = "The action could not be completed. See the log for details.",
            };
        }
        finally
        {
            if (!string.IsNullOrEmpty(id))
                _inFlight.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// A short, safe view of the arguments for the log: keys and sizes, never the
    /// whole payload. An API key or an essay must not land in the log file.
    /// </summary>
    private static string Summarize(JsonElement? args)
    {
        if (args is not { } element) return "null";
        if (element.ValueKind != JsonValueKind.Object) return element.ValueKind.ToString();

        var parts = new List<string>();
        foreach (var property in element.EnumerateObject())
        {
            var size = property.Value.ValueKind switch
            {
                JsonValueKind.String => ((property.Value.GetString()?.Length ?? 0)) + " chars",
                JsonValueKind.Array => property.Value.GetArrayLength() + " items",
                JsonValueKind.Object => "object",
                _ => property.Value.ValueKind.ToString().ToLowerInvariant(),
            };
            parts.Add($"{property.Name}={size}");
        }
        return parts.Count == 0 ? "{}" : "{" + string.Join(", ", parts) + "}";
    }

    /// <summary>Turns a raw web message into a JSON response string (for postMessage fallback).</summary>
    public async Task<string> HandleAsync(string rawMessage, CancellationToken ct)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(rawMessage, Json);
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        if (request is null || string.IsNullOrEmpty(request.Id))
            return string.Empty;

        var response = await HandleDirectAsync(request.Id, request.Method, request.Args, ct).ConfigureAwait(false);
        return Serialize(response);
    }

    public static string Serialize(object? value)
        => JsonSerializer.Serialize(value, Json);
}

