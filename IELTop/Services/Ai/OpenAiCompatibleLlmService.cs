using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IELTop.Services.Storage;

namespace IELTop.Services.Ai;

/// <summary>
/// One image attached to a message, as base64 data ready for a vision model.
/// </summary>
public sealed record LlmImage(string Base64, string MediaType);

/// <summary>
/// One chat message in an OpenAI compatible request. Images are only sent when
/// the model supports vision and the user turned vision on in Settings.
/// </summary>
public sealed record LlmMessage(string Role, string Content, IReadOnlyList<LlmImage>? Images = null)
{
    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);
    public static LlmMessage Assistant(string content) => new("assistant", content);
    public static LlmMessage UserWithImages(string content, IReadOnlyList<LlmImage> images)
        => new("user", content, images);
}

/// <summary>
/// Result of a chat call. When streaming is used, <see cref="Text"/> holds the full reply.
/// </summary>
public sealed record LlmResult(bool Success, string Text, string Error)
{
    public static LlmResult Fail(string error) => new(false, string.Empty, error);
}

public interface ILlmService
{
    bool IsConfigured { get; }
    bool UseStreaming { get; }
    bool VisionEnabled { get; }
    Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default);
    Task<LlmResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Talks to any server that implements the OpenAI chat completions API,
/// for example OpenAI, Azure OpenAI compatible gateways, Ollama, LM Studio,
/// llama.cpp server or vLLM. Works online and offline on the local network.
/// </summary>
public sealed class OpenAiCompatibleLlmService : ILlmService
{
    private readonly ISettingsStore _settings;
    private readonly HttpClient _http;

    public OpenAiCompatibleLlmService(ISettingsStore settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.Current.LlmBaseUrl) &&
        !string.IsNullOrWhiteSpace(_settings.Current.LlmModel);

    public bool UseStreaming => _settings.Current.LlmUseStreaming;

    public bool VisionEnabled => _settings.Current.LlmVisionEnabled;

    public async Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var reply = await CompleteAsync(
            new[] { LlmMessage.User("Reply with the single word: ok") }, ct);
        return reply.Success
            ? new LlmResult(true, reply.Text, string.Empty)
            : reply;
    }

    public async Task<LlmResult> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        if (!IsConfigured) return LlmResult.Fail(NotConfiguredMessage);

        try
        {
            using var request = BuildRequest(messages, stream: false);
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return LlmResult.Fail(DescribeHttpError(response.StatusCode, body));

            var text = ExtractContent(body);
            return string.IsNullOrEmpty(text)
                ? LlmResult.Fail("The model returned an empty reply.")
                : new LlmResult(true, text, string.Empty);
        }
        catch (TaskCanceledException)
        {
            return LlmResult.Fail("The request timed out. Check the server and try again.");
        }
        catch (HttpRequestException)
        {
            // The raw socket message is not useful to a student. Give the cause and the fix.
            return LlmResult.Fail(
                "Could not reach the model server. Check that it is running and that the base URL in Settings is correct.");
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            yield return NotConfiguredMessage;
            yield break;
        }

        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;
        try
        {
            using var request = BuildRequest(messages, stream: true);
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                yield return DescribeHttpError(response.StatusCode, body);
                yield break;
            }

            stream = await response.Content.ReadAsStreamAsync(ct);
            reader = new StreamReader(stream);

            // Read to end with ReadLineAsync. Checking EndOfStream would block on the stream.
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var payload = line[5..].Trim();
                if (payload == "[DONE]") break;

                var delta = ExtractDelta(payload);
                if (!string.IsNullOrEmpty(delta)) yield return delta;
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
        }
    }

    private HttpRequestMessage BuildRequest(IReadOnlyList<LlmMessage> messages, bool stream)
    {
        var url = $"{_settings.Current.LlmBaseUrl.TrimEnd('/')}/chat/completions";
        var payload = new
        {
            model = _settings.Current.LlmModel,
            messages = messages.Select(BuildMessage).ToList(),
            temperature = _settings.Current.LlmTemperature,
            max_tokens = _settings.Current.LlmMaxTokens,
            stream
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }),
                Encoding.UTF8, "application/json")
        };

        var key = _settings.Current.LlmApiKey;
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    /// <summary>
    /// A plain message sends content as a string. A message with images sends the
    /// multimodal content array that vision models expect.
    /// </summary>
    private object BuildMessage(LlmMessage message)
    {
        var useImages = VisionEnabled
                        && message.Images is { Count: > 0 };

        if (!useImages)
            return new { role = message.Role, content = message.Content };

        var parts = new List<object> { new { type = "text", text = message.Content } };
        foreach (var image in message.Images!)
        {
            parts.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:{image.MediaType};base64,{image.Base64}" }
            });
        }

        return new { role = message.Role, content = parts };
    }

    private static string ExtractContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.GetArrayLength() == 0)
            return string.Empty;
        if (!choices[0].TryGetProperty("message", out var message))
            return string.Empty;
        return message.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ExtractDelta(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return string.Empty;
            if (!choices[0].TryGetProperty("delta", out var delta))
                return string.Empty;
            return delta.TryGetProperty("content", out var content)
                ? content.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string DescribeHttpError(System.Net.HttpStatusCode status, string body)
    {
        var shortBody = body.Length > 300 ? body[..300] : body;
        return status switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "The server rejected the API key. Check the key in Settings.",
            System.Net.HttpStatusCode.NotFound =>
                "The server has no chat endpoint at that URL. Check the base URL in Settings.",
            System.Net.HttpStatusCode.TooManyRequests =>
                "The server is rate limited. Wait a moment and try again.",
            _ => $"The model server returned an error ({(int)status}). {shortBody}"
        };
    }

    private const string NotConfiguredMessage =
        "No language model is configured. Open Settings and set the base URL, model and API key.";
}
