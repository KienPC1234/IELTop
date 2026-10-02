using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public JsonElement? Args { get; set; }
}

/// <summary>One answer back to the web UI. Exactly one of Result or Error is set.</summary>
public sealed class BridgeResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

/// <summary>
/// Routes method calls from the web UI to typed handlers. Every handler is
/// async so slow work (inference, the network) never blocks the window, and
/// a failing handler becomes a short error string, never a raw exception.
/// </summary>
public sealed class BridgeRouter
{
    private static readonly JsonSerializerOptions Json = BridgeJson.Options;

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers
        = new(StringComparer.Ordinal);

    /// <summary>Registers one method. The name is the key the web UI calls.</summary>
    public void Register(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _handlers[method] = handler;

    /// <summary>Registers a method that takes no arguments.</summary>
    public void Register(string method, Func<CancellationToken, Task<object?>> handler)
        => Register(method, (_, ct) => handler(ct));

    /// <summary>Registers a synchronous method.</summary>
    public void Register(string method, Func<object?> handler)
        => Register(method, (_, _) => Task.FromResult(handler()));

    public bool Has(string method) => _handlers.ContainsKey(method);

    /// <summary>Turns a raw web message into a JSON response string.</summary>
    public async Task<string> HandleAsync(string rawMessage, CancellationToken ct)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(rawMessage, Json);
        }
        catch (JsonException)
        {
            // Not a bridge message; answer nothing.
            return string.Empty;
        }

        if (request is null || string.IsNullOrEmpty(request.Id))
            return string.Empty;

        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            return Serialize(new BridgeResponse
            {
                Id = request.Id,
                Error = $"Unknown action: {request.Method}",
            });
        }

        try
        {
            var result = await handler(request.Args, ct).ConfigureAwait(false);
            return Serialize(new BridgeResponse { Id = request.Id, Result = result });
        }
        catch (OperationCanceledException)
        {
            return Serialize(new BridgeResponse { Id = request.Id, Error = "The action was cancelled." });
        }
        catch (Exception ex)
        {
            // Never surface a stack trace to the UI; log it and send a short line.
            Console.Error.WriteLine($"[bridge] {request.Method} failed: {ex}");
            return Serialize(new BridgeResponse
            {
                Id = request.Id,
                Error = "The action could not be completed. See the log for details.",
            });
        }
    }

    private static string Serialize(BridgeResponse response)
        => JsonSerializer.Serialize(response, Json);
}
