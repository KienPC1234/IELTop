using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Services.Storage;
using OpenAI;
using OpenAI.Chat;

namespace IELTop.Services.Ai;

/// <summary>
/// One image attached to a message, as base64 data ready for a vision model.
/// </summary>
public sealed record LlmImage(string Base64, string MediaType);

/// <summary>
/// One chat message in an OpenAI compatible request. Images are only sent when
/// the model supports vision and the user turned vision on in Settings.
/// </summary>
public sealed record LlmMessage(
    string Role,
    string Content,
    IReadOnlyList<LlmImage>? Images = null,
    string? ToolCallId = null)
{
    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);
    public static LlmMessage Assistant(string content) => new("assistant", content);
    public static LlmMessage Tool(string toolCallId, string content) => new("tool", content, null, toolCallId);
    public static LlmMessage UserWithImages(string content, IReadOnlyList<LlmImage> images)
        => new("user", content, images);
}

/// <summary>
/// Definition of an OpenAI-compatible function tool schema.
/// </summary>
public sealed record LlmToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema,
    bool Strict = false);

/// <summary>
/// Asynchronous executor callback for invoked tools.
/// </summary>
public delegate Task<string> LlmToolExecutorAsync(string toolName, string argumentsJson, CancellationToken ct);

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
    Task<LlmResult> CompleteWithToolsAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        LlmToolExecutorAsync toolExecutor,
        Action<string, string>? onToolInvoked = null,
        Action<string>? onTokenChunk = null,
        CancellationToken ct = default);
    Task<LlmResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Talks to any server that implements the standard OpenAI chat completions API
/// (OpenAI, Azure, vLLM, Ollama, LM Studio, llama.cpp server) using the official
/// OpenAI C# library with tool calling (functions) and real-time streaming.
/// </summary>
public sealed class OpenAiCompatibleLlmService : ILlmService
{
    private readonly ISettingsStore _settings;

    public OpenAiCompatibleLlmService(ISettingsStore settings)
    {
        _settings = settings;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.Current.LlmBaseUrl) &&
        !string.IsNullOrWhiteSpace(_settings.Current.LlmModel);

    public bool UseStreaming => _settings.Current.LlmUseStreaming;

    public bool VisionEnabled => _settings.Current.LlmVisionEnabled;

    public Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
        => CompleteAsync(new[] { LlmMessage.User(LlmPrompts.TestConnectionPrompt) }, ct);

    public async Task<LlmResult> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
    {
        if (!IsConfigured) return LlmResult.Fail(NotConfiguredMessage);

        var endpoint = _settings.Current.LlmBaseUrl;
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeoutOf(_settings));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            var client = CreateChatClient();
            var chatMessages = MapMessages(messages, VisionEnabled);
            var options = BuildOptions();

            ChatCompletion completion = await client.CompleteChatAsync(chatMessages, options, linked.Token)
                .ConfigureAwait(false);
            watch.Stop();

            string text = ExtractContent(completion);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new LlmResult(false, string.Empty,
                    "The model returned an empty reply.",
                    200, watch.ElapsedMilliseconds, endpoint);
            }

            return new LlmResult(true, text.Trim(), string.Empty, 200, watch.ElapsedMilliseconds, endpoint);
        }
        catch (Exception ex)
        {
            watch.Stop();
            return HandleException(ex, watch.ElapsedMilliseconds, endpoint);
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

        using var timeout = new CancellationTokenSource(TimeoutOf(_settings));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        string? startupError = null;
        AsyncCollectionResult<StreamingChatCompletionUpdate>? updates = null;
        try
        {
            var client = CreateChatClient();
            var chatMessages = MapMessages(messages, VisionEnabled);
            var options = BuildOptions();
            updates = client.CompleteChatStreamingAsync(chatMessages, options, linked.Token);
        }
        catch (Exception ex)
        {
            startupError = DescribeException(ex);
        }

        if (startupError != null)
        {
            yield return startupError;
            yield break;
        }

        string? streamError = null;
        IAsyncEnumerator<StreamingChatCompletionUpdate>? enumerator = null;
        try
        {
            enumerator = updates!.GetAsyncEnumerator(linked.Token);
        }
        catch (Exception ex)
        {
            streamError = DescribeException(ex);
        }

        if (streamError != null)
        {
            yield return streamError;
            yield break;
        }

        while (true)
        {
            StreamingChatCompletionUpdate update;
            try
            {
                if (!await enumerator!.MoveNextAsync().ConfigureAwait(false)) break;
                update = enumerator.Current;
            }
            catch (Exception ex)
            {
                streamError = DescribeException(ex);
                break;
            }

            if (update.ContentUpdate is { Count: > 0 })
            {
                foreach (var part in update.ContentUpdate)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        yield return part.Text;
                    }
                }
            }
        }

        if (streamError != null)
        {
            yield return $" [stream interrupted: {streamError}]";
        }

        if (enumerator != null)
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes an interactive chat turn with support for OpenAI tool definitions,
    /// handling model tool invocations, running C# tool handlers, and looping back
    /// until the final synthesised response is generated.
    private sealed class PendingStreamingToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public System.Text.StringBuilder Arguments { get; } = new();
    }

    /// <summary>
    /// Executes an interactive chat turn with support for OpenAI tool definitions,
    /// handling model tool invocations, running C# tool handlers, and streaming
    /// individual tokens directly from the model over SSE when onTokenChunk is provided.
    /// </summary>
    public async Task<LlmResult> CompleteWithToolsAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        LlmToolExecutorAsync toolExecutor,
        Action<string, string>? onToolInvoked = null,
        Action<string>? onTokenChunk = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return LlmResult.Fail(NotConfiguredMessage);

        var endpoint = _settings.Current.LlmBaseUrl;
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeoutOf(_settings));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            var client = CreateChatClient();
            var chatMessages = MapMessages(messages, VisionEnabled);
            var options = BuildOptions(tools);

            int round = 0;
            const int maxRounds = 5;

            while (round++ < maxRounds)
            {
                if (onTokenChunk != null)
                {
                    // Real-time token streaming direct from OpenAI SSE endpoint
                    var streamSb = new System.Text.StringBuilder();
                    var pendingTools = new Dictionary<int, PendingStreamingToolCall>();

                    var updates = client.CompleteChatStreamingAsync(chatMessages, options, linked.Token);
                    await foreach (var update in updates.ConfigureAwait(false))
                    {
                        if (update.ContentUpdate is { Count: > 0 })
                        {
                            foreach (var part in update.ContentUpdate)
                            {
                                if (!string.IsNullOrEmpty(part.Text))
                                {
                                    streamSb.Append(part.Text);
                                    onTokenChunk(part.Text);
                                }
                            }
                        }

                        if (update.ToolCallUpdates is { Count: > 0 })
                        {
                            foreach (var tc in update.ToolCallUpdates)
                            {
                                if (!pendingTools.TryGetValue(tc.Index, out var p))
                                {
                                    p = new PendingStreamingToolCall();
                                    pendingTools[tc.Index] = p;
                                }

                                if (!string.IsNullOrEmpty(tc.ToolCallId))
                                    p.Id = tc.ToolCallId;
                                if (!string.IsNullOrEmpty(tc.FunctionName))
                                    p.Name = tc.FunctionName;
                                if (tc.FunctionArgumentsUpdate is { } argsChunk)
                                    p.Arguments.Append(argsChunk.ToString());
                            }
                        }
                    }

                    if (pendingTools.Count > 0)
                    {
                        var toolCalls = new List<ChatToolCall>();
                        foreach (var kvp in pendingTools.OrderBy(k => k.Key))
                        {
                            var p = kvp.Value;
                            string callId = string.IsNullOrEmpty(p.Id) ? $"call_{kvp.Key}" : p.Id;
                            toolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                                callId,
                                p.Name,
                                BinaryData.FromString(p.Arguments.ToString())));
                        }

                        chatMessages.Add(new AssistantChatMessage(toolCalls));

                        foreach (var tc in toolCalls)
                        {
                            string toolName = tc.FunctionName;
                            string argsJson = tc.FunctionArguments?.ToString() ?? "{}";

                            onToolInvoked?.Invoke(toolName, argsJson);

                            string toolResult;
                            try
                            {
                                toolResult = await toolExecutor(toolName, argsJson, linked.Token).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                            }

                            chatMessages.Add(new ToolChatMessage(tc.Id, toolResult));
                        }

                        // Continue to next round so the model generates the final synthesized answer token-by-token
                        continue;
                    }

                    // Direct token streaming finished with no tool calls
                    watch.Stop();
                    return new LlmResult(true, streamSb.ToString().Trim(), string.Empty, 200, watch.ElapsedMilliseconds, endpoint);
                }
                else
                {
                    // Non-streaming mode (e.g. streaming turned off in Settings)
                    ChatCompletion completion = await client.CompleteChatAsync(chatMessages, options, linked.Token)
                        .ConfigureAwait(false);

                    if (completion.FinishReason == ChatFinishReason.ToolCalls && completion.ToolCalls is { Count: > 0 })
                    {
                        chatMessages.Add(new AssistantChatMessage(completion));

                        foreach (ChatToolCall toolCall in completion.ToolCalls)
                        {
                            string toolName = toolCall.FunctionName;
                            string argsJson = toolCall.FunctionArguments?.ToString() ?? "{}";

                            onToolInvoked?.Invoke(toolName, argsJson);

                            string toolResult;
                            try
                            {
                                toolResult = await toolExecutor(toolName, argsJson, linked.Token).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                            }

                            chatMessages.Add(new ToolChatMessage(toolCall.Id, toolResult));
                        }

                        continue;
                    }

                    watch.Stop();
                    string text = ExtractContent(completion);
                    return new LlmResult(true, text.Trim(), string.Empty, 200, watch.ElapsedMilliseconds, endpoint);
                }
            }

            watch.Stop();
            return new LlmResult(false, string.Empty,
                "The model exceeded the maximum tool execution rounds.",
                200, watch.ElapsedMilliseconds, endpoint);
        }
        catch (Exception ex)
        {
            watch.Stop();
            return HandleException(ex, watch.ElapsedMilliseconds, endpoint);
        }
    }

    private ChatClient CreateChatClient()
    {
        var rawUrl = (_settings.Current.LlmBaseUrl ?? string.Empty).Trim();
        var model = (_settings.Current.LlmModel ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(model)) model = "gpt-4o-mini";

        var apiKey = _settings.Current.LlmApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = "local-server-token";

        var clientOptions = new OpenAIClientOptions
        {
            NetworkTimeout = TimeoutOf(_settings)
        };

        if (rawUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            rawUrl = rawUrl[..^"/chat/completions".Length].TrimEnd('/');
        }

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var endpointUri))
        {
            clientOptions.Endpoint = endpointUri;
        }

        return new ChatClient(model, new ApiKeyCredential(apiKey), clientOptions);
    }

    private ChatCompletionOptions BuildOptions(IReadOnlyList<LlmToolDefinition>? tools = null)
    {
        var options = new ChatCompletionOptions();

        if (_settings.Current.LlmTemperature > 0)
            options.Temperature = (float)_settings.Current.LlmTemperature;

        double topP = _settings.Current.LlmTopP;
        if (double.IsFinite(topP) && topP > 0 && topP <= 1)
            options.TopP = (float)topP;

        if (_settings.Current.LlmMaxTokens > 0)
            options.MaxOutputTokenCount = _settings.Current.LlmMaxTokens;

        if (tools is { Count: > 0 })
        {
            foreach (var tool in tools)
            {
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    functionName: tool.Name,
                    functionDescription: tool.Description,
                    functionParameters: BinaryData.FromString(tool.ParametersJsonSchema),
                    functionSchemaIsStrict: tool.Strict));
            }
        }

        return options;
    }

    private static List<ChatMessage> MapMessages(IReadOnlyList<LlmMessage> messages, bool visionEnabled)
    {
        var list = new List<ChatMessage>(messages.Count);
        foreach (var m in messages)
        {
            switch (m.Role.ToLowerInvariant())
            {
                case "system":
                    list.Add(new SystemChatMessage(m.Content));
                    break;
                case "assistant":
                    list.Add(new AssistantChatMessage(m.Content));
                    break;
                case "tool":
                    list.Add(new ToolChatMessage(m.ToolCallId ?? "call_0", m.Content));
                    break;
                case "user":
                default:
                    if (visionEnabled && m.Images is { Count: > 0 })
                    {
                        var parts = new List<ChatMessageContentPart>
                        {
                            ChatMessageContentPart.CreateTextPart(m.Content)
                        };
                        foreach (var img in m.Images)
                        {
                            try
                            {
                                var bytes = Convert.FromBase64String(img.Base64);
                                parts.Add(ChatMessageContentPart.CreateImagePart(
                                    BinaryData.FromBytes(bytes), img.MediaType));
                            }
                            catch
                            {
                                // Skip malformed images
                            }
                        }
                        list.Add(new UserChatMessage(parts));
                    }
                    else
                    {
                        list.Add(new UserChatMessage(m.Content));
                    }
                    break;
            }
        }
        return list;
    }

    private static string ExtractContent(ChatCompletion completion)
    {
        if (completion.Content is { Count: > 0 })
        {
            return string.Concat(completion.Content.Select(c => c.Text));
        }
        return string.Empty;
    }

    private static TimeSpan TimeoutOf(ISettingsStore settings)
    {
        int seconds = settings.Current.LlmTimeoutSeconds;
        if (seconds < 15) seconds = 15;
        if (seconds > 600) seconds = 600;
        return TimeSpan.FromSeconds(seconds);
    }

    private static LlmResult HandleException(Exception ex, long elapsedMs, string endpoint)
    {
        if (ex is ClientResultException cre)
        {
            string msg = cre.Status switch
            {
                401 => "The server rejected the API key. Check the key in Settings.",
                404 => "The server has no chat endpoint at that URL. Check the base URL in Settings.",
                429 => "The server is rate limited. Wait a moment and try again.",
                _ => $"The model server returned error ({cre.Status}): {cre.Message}"
            };
            return new LlmResult(false, string.Empty, msg, cre.Status, elapsedMs, endpoint);
        }

        if (ex is OperationCanceledException)
        {
            return new LlmResult(false, string.Empty,
                "The request timed out. Check the server and try again.",
                0, elapsedMs, endpoint);
        }

        return new LlmResult(false, string.Empty,
            $"Could not reach the model server: {ex.Message}",
            0, elapsedMs, endpoint);
    }

    private static string DescribeException(Exception ex)
    {
        if (ex is ClientResultException cre)
        {
            return cre.Status switch
            {
                401 => "The server rejected the API key. Check the key in Settings.",
                404 => "The server has no chat endpoint at that URL. Check the base URL in Settings.",
                429 => "The server is rate limited. Wait a moment and try again.",
                _ => $"The model server returned error ({cre.Status}): {cre.Message}"
            };
        }
        if (ex is OperationCanceledException)
        {
            return "The request timed out. Check the server and try again.";
        }
        return $"Could not reach the model server: {ex.Message}";
    }

    private const string NotConfiguredMessage =
        "No language model is configured. Open Settings and set the base URL, model and API key.";
}
