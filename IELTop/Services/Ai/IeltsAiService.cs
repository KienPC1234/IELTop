using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IELTop.Services.Ai;

/// <summary>
/// Structured writing feedback returned by the language model.
/// Band values are estimates for practice, never official IELTS scores.
/// </summary>
public sealed class WritingFeedback
{
    [JsonPropertyName("estimated_band")]
    public double EstimatedBand { get; set; }

    [JsonPropertyName("task_response")]
    public double TaskResponse { get; set; }

    [JsonPropertyName("coherence")]
    public double Coherence { get; set; }

    [JsonPropertyName("lexical_resource")]
    public double LexicalResource { get; set; }

    [JsonPropertyName("grammar")]
    public double Grammar { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("strengths")]
    public List<string> Strengths { get; set; } = new();

    [JsonPropertyName("improvements")]
    public List<string> Improvements { get; set; } = new();

    [JsonPropertyName("corrected_excerpt")]
    public string CorrectedExcerpt { get; set; } = string.Empty;
}

public sealed record WritingFeedbackResult(bool Success, WritingFeedback? Feedback, string Error)
{
    public static WritingFeedbackResult Fail(string error) => new(false, null, error);
}

public interface IIeltsAiService
{
    bool IsAvailable { get; }
    bool VisionAvailable { get; }
    Task<WritingFeedbackResult> ReviewWritingAsync(string taskPrompt, string essay, int minimumWords, CancellationToken ct = default);
    IAsyncEnumerable<string> ExplainWordAsync(string word, CancellationToken ct = default);
    IAsyncEnumerable<string> CoachSpeakingAsync(string target, string heardPhonemes, string mistakes, CancellationToken ct = default);
    Task<LlmResult> ReadImageAsync(string prompt, string base64Image, string mediaType, CancellationToken ct = default);
    Task<LlmResult> TestAsync(CancellationToken ct = default);
}

/// <summary>
/// Builds IELTS specific prompts and parses the model replies.
/// Keeps prompt wording and JSON parsing in one place.
/// </summary>
public sealed class IeltsAiService : IIeltsAiService
{
    private const string SystemPrompt =
        "You are a strict but fair IELTS examiner and tutor. " +
        "You give practical feedback a learner can act on. " +
        "You never invent official scores. Estimated bands are guidance for practice only. " +
        "Reply using the requested format exactly.";

    private readonly ILlmService _llm;

    public IeltsAiService(ILlmService llm)
    {
        _llm = llm;
    }

    public bool IsAvailable => _llm.IsConfigured;

    public bool VisionAvailable => _llm.IsConfigured && _llm.VisionEnabled;

    public Task<LlmResult> TestAsync(CancellationToken ct = default) => _llm.TestConnectionAsync(ct);

    /// <summary>
    /// Ask a vision model to read an image, for example a photo of a page. The
    /// reply is plain text, which then feeds the normal import drafting.
    /// </summary>
    public Task<LlmResult> ReadImageAsync(string prompt, string base64Image, string mediaType, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return Task.FromResult(LlmResult.Fail(
                "No language model is configured. Open Settings to add one."));
        if (!_llm.VisionEnabled)
            return Task.FromResult(LlmResult.Fail(
                "Vision is turned off. Enable it in Settings to read images."));

        var message = LlmMessage.UserWithImages(prompt, new[] { new LlmImage(base64Image, mediaType) });
        return _llm.CompleteAsync(new[] { LlmMessage.System(SystemPrompt), message }, ct);
    }

    public async Task<WritingFeedbackResult> ReviewWritingAsync(
        string taskPrompt, string essay, int minimumWords, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return WritingFeedbackResult.Fail(
                "No language model is configured. Open Settings to add one.");

        var user = new StringBuilder()
            .AppendLine("Task prompt:")
            .AppendLine(taskPrompt)
            .AppendLine()
            .AppendLine($"Minimum words: {minimumWords}")
            .AppendLine()
            .AppendLine("Student essay:")
            .AppendLine(essay)
            .AppendLine()
            .AppendLine("Return only JSON with this shape:")
            .AppendLine("{")
            .AppendLine("  \"estimated_band\": 6.5,")
            .AppendLine("  \"task_response\": 6.5,")
            .AppendLine("  \"coherence\": 6.0,")
            .AppendLine("  \"lexical_resource\": 6.5,")
            .AppendLine("  \"grammar\": 6.0,")
            .AppendLine("  \"summary\": \"two short sentences\",")
            .AppendLine("  \"strengths\": [\"point\"],")
            .AppendLine("  \"improvements\": [\"point\"],")
            .AppendLine("  \"corrected_excerpt\": \"rewrite one weak sentence\"")
            .AppendLine("}")
            .ToString();

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);

        if (!result.Success) return WritingFeedbackResult.Fail(result.Error);

        var feedback = ParseFeedback(result.Text);
        return feedback is null
            ? WritingFeedbackResult.Fail("The model reply could not be read. Try again.")
            : new WritingFeedbackResult(true, feedback, string.Empty);
    }

    public IAsyncEnumerable<string> ExplainWordAsync(string word, CancellationToken ct = default)
    {
        var user =
            $"Explain the English word \"{word}\" for an IELTS learner. " +
            "Give the part of speech, an IPA transcription, a short meaning, " +
            "one example sentence, and one common mistake to avoid. Keep it under 120 words.";
        return _llm.StreamAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    public IAsyncEnumerable<string> CoachSpeakingAsync(
        string target, string heardPhonemes, string mistakes, CancellationToken ct = default)
    {
        var user =
            "A student practised this sentence aloud.\n" +
            $"Target: {target}\n" +
            $"Sounds the model heard: {heardPhonemes}\n" +
            $"Detected mistakes: {mistakes}\n\n" +
            "Give three short, specific tips to fix the pronunciation. " +
            "Mention the mouth position when helpful. Keep it under 120 words.";
        return _llm.StreamAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    private static WritingFeedback? ParseFeedback(string raw)
    {
        var json = ExtractJsonObject(raw);
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WritingFeedback>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Models often wrap JSON in prose or code fences. Pull out the first object.
    /// </summary>
    private static string? ExtractJsonObject(string raw)
    {
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return raw[start..(end + 1)];
    }
}
