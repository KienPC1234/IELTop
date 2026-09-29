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
/// StatusCode, ElapsedMs and Endpoint are debug details for the Settings test.
/// The API key is never included here.
/// </summary>
public sealed record LlmResult(
    bool Success,
    string Text,
    string Error,
    int StatusCode = 0,
    long ElapsedMs = 0,
    string Endpoint = "")
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

    public Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
        => CompleteAsync(
            new[] { LlmMessage.User(LlmPrompts.TestConnectionPrompt) }, ct);

    public async Task<LlmResult> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        if (!IsConfigured) return LlmResult.Fail(NotConfiguredMessage);

        var endpoint = $"{_settings.Current.LlmBaseUrl.TrimEnd('/')}/chat/completions";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeoutOf(_settings));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var request = BuildRequest(messages, stream: false);
            using var response = await _http.SendAsync(request, linked.Token);
            var body = await response.Content.ReadAsStringAsync(ct);
            watch.Stop();

            if (!response.IsSuccessStatusCode)
                return new LlmResult(false, string.Empty,
                    DescribeHttpError(response.StatusCode, body),
                    (int)response.StatusCode, watch.ElapsedMilliseconds, endpoint);

            var text = ExtractContent(body);
            return string.IsNullOrEmpty(text)
                ? new LlmResult(false, string.Empty,
                    "The model returned an empty reply.",
                    (int)response.StatusCode, watch.ElapsedMilliseconds, endpoint)
                : new LlmResult(true, text, string.Empty,
                    (int)response.StatusCode, watch.ElapsedMilliseconds, endpoint);
        }
        catch (TaskCanceledException)
        {
            watch.Stop();
            return new LlmResult(false, string.Empty,
                "The request timed out. Check the server and try again.",
                0, watch.ElapsedMilliseconds, endpoint);
        }
        catch (HttpRequestException)
        {
            watch.Stop();
            // The raw socket message is not useful to a student. Give the cause and the fix.
            return new LlmResult(false, string.Empty,
                "Could not reach the model server. Check that it is running and that the base URL in Settings is correct.",
                0, watch.ElapsedMilliseconds, endpoint);
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
        using var timeout = new CancellationTokenSource(TimeoutOf(_settings));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var request = BuildRequest(messages, stream: true);
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(linked.Token);
                yield return DescribeHttpError(response.StatusCode, body);
                yield break;
            }

            stream = await response.Content.ReadAsStreamAsync(linked.Token);
            reader = new StreamReader(stream);

            // Read to end with ReadLineAsync. Checking EndOfStream would block on the stream.
            while (await reader.ReadLineAsync(linked.Token) is { } line)
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
        double topP = _settings.Current.LlmTopP;
        if (!double.IsFinite(topP) || topP <= 0 || topP > 1) topP = 1.0;
        var payload = new
        {
            model = _settings.Current.LlmModel,
            messages = messages.Select(BuildMessage).ToList(),
            temperature = _settings.Current.LlmTemperature,
            top_p = topP,
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

    private static TimeSpan TimeoutOf(ISettingsStore settings)
    {
        int seconds = settings.Current.LlmTimeoutSeconds;
        if (seconds < 15) seconds = 15;
        if (seconds > 300) seconds = 300;
        return TimeSpan.FromSeconds(seconds);
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
