using System;
using System.Text.Json;
using IELTop.Services.Diagnostics;
using Microsoft.JSInterop;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// The Blazor side of the bridge. The page holds a reference to this object and
/// calls <see cref="CallAsync"/> for every request, which is the same router the
/// HTTP channel uses, so both channels run one set of handlers.
///
/// Push events go the other way: <see cref="Push"/> carries the exact JSON the
/// event stream sends, so the page can handle both channels with one listener.
/// </summary>
public sealed class JsBridge
{
    private readonly BridgeRouter _router;

    public JsBridge(BridgeRouter router)
    {
        _router = router;
        _router.OnEventPushed += OnPushed;
    }

    /// <summary>
    /// Runs one request from the page and returns the reply as JSON. The page
    /// keeps its own request id, so a reply is matched where the call started.
    /// </summary>
    [JSInvokable]
    public async Task<string> CallAsync(string id, string method, string? argsJson)
    {
        JsonElement? args = null;
        if (!string.IsNullOrWhiteSpace(argsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(argsJson);
                args = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                // A malformed bag cannot run as "no arguments": a truncated
                // base64 payload would then look like an empty recording.
                AppLog.Warn("bridge", $"Bad args for '{method}' (id {id}): {ex.Message}");
                return BridgeRouter.Serialize(new BridgeResponse { Id = id, Error = "The action arguments were invalid." });
            }
        }

        var response = await _router.HandleDirectAsync(id, method, args, CancellationToken.None)
            .ConfigureAwait(false);
        return BridgeRouter.Serialize(response);
    }

    /// <summary>Raised when an event has to reach the page. Carries ready-to-parse JSON.</summary>
    public event Action<string>? Push;

    private void OnPushed(string eventName, object? payload)
    {
        string message;

        // The engine already builds the full message for some events. Forwarding
        // it unchanged keeps the shape identical to the event stream.
        if (payload is string raw && raw.TrimStart().StartsWith('{'))
        {
            message = raw;
        }
        else
        {
            try
            {
                message = JsonSerializer.Serialize(
                    new { @event = eventName, payload },
                    BridgeJson.Options);
            }
            catch (Exception ex)
            {
                AppLog.Error("bridge", $"Event '{eventName}' could not be serialized.", ex);
                return;
            }
        }

        Push?.Invoke(message);
    }
}
