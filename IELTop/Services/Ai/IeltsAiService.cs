using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IELTop.Services.Ai;

/// <summary>
/// Structured writing feedback returned by the language model.
/// Band values are estimates for practice, never official IELTS scores.
/// The band is reported as a range, because examiners vary.
/// </summary>
public sealed class WritingFeedback
{
    [JsonPropertyName("estimated_band")]
    public double EstimatedBand { get; set; }

    [JsonPropertyName("band_low")]
    public double BandLow { get; set; }

    [JsonPropertyName("band_high")]
    public double BandHigh { get; set; }

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

    public string BandLabel => $"{IeltsBanding.RoundHalf(BandLow):0.0} to {IeltsBanding.RoundHalf(BandHigh):0.0}";
}

public sealed record WritingFeedbackResult(bool Success, WritingFeedback? Feedback, string Error)
{
    public static WritingFeedbackResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Structured speaking feedback for one cue or one full Speaking test.
/// </summary>
public sealed class SpeakingFeedback
{
    [JsonPropertyName("estimated_band")]
    public double EstimatedBand { get; set; }

    [JsonPropertyName("band_low")]
    public double BandLow { get; set; }

    [JsonPropertyName("band_high")]
    public double BandHigh { get; set; }

    [JsonPropertyName("fluency")]
    public double Fluency { get; set; }

    [JsonPropertyName("lexical_resource")]
    public double LexicalResource { get; set; }

    [JsonPropertyName("grammar")]
    public double Grammar { get; set; }

    [JsonPropertyName("pronunciation")]
    public double Pronunciation { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("strengths")]
    public List<string> Strengths { get; set; } = new();

    [JsonPropertyName("improvements")]
    public List<string> Improvements { get; set; } = new();

    public string BandLabel => $"{IeltsBanding.RoundHalf(BandLow):0.0} to {IeltsBanding.RoundHalf(BandHigh):0.0}";
}

public sealed record SpeakingFeedbackResult(bool Success, SpeakingFeedback? Feedback, string Error)
{
    public static SpeakingFeedbackResult Fail(string error) => new(false, null, error);
}

public interface IIeltsAiService
{
    bool IsAvailable { get; }
    bool VisionAvailable { get; }
    Task<WritingFeedbackResult> ReviewWritingAsync(string taskPrompt, string essay, int minimumWords, CancellationToken ct = default);
    Task<WritingFeedbackResult> ReviewWritingAsync(string taskPrompt, string essay, int minimumWords, MarkingStrictness strictness, CancellationToken ct = default);
    Task<SpeakingFeedbackResult> AssessSpeakingAsync(string cue, string transcript, MarkingStrictness strictness, CancellationToken ct = default);
    Task<LlmResult> ExplainReadingAsync(string passage, string question, string chosenKey, string correctKey, CancellationToken ct = default);
    Task<LlmResult> SuggestTopicAsync(string skill, CancellationToken ct = default);
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

    public Task<WritingFeedbackResult> ReviewWritingAsync(
        string taskPrompt, string essay, int minimumWords, CancellationToken ct = default)
        => ReviewWritingAsync(taskPrompt, essay, minimumWords, MarkingStrictness.Standard, ct);

    public async Task<WritingFeedbackResult> ReviewWritingAsync(
        string taskPrompt, string essay, int minimumWords, MarkingStrictness strictness, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return WritingFeedbackResult.Fail(
                "No language model is configured. Open Settings to add one.");

        var stance = strictness switch
        {
            MarkingStrictness.Lenient => "Mark like an encouraging examiner. Reward what works, note only clear errors.",
            MarkingStrictness.Strict => "Mark like a very strict examiner. Penalize every error in task response, cohesion, words, and grammar. Do not inflate scores.",
            _ => "Mark like a typical IELTS examiner. Be fair and specific."
        };

        var user = new StringBuilder()
            .AppendLine("Task prompt:")
            .AppendLine(taskPrompt)
            .AppendLine()
            .AppendLine($"Minimum words: {minimumWords}")
            .AppendLine($"Marking level: {IeltsBanding.StrictnessLabel(strictness)}. {stance}")
            .AppendLine()
            .AppendLine("Student essay:")
            .AppendLine(essay)
            .AppendLine()
            .AppendLine("Score like an IELTS examiner. Give every band in half-band steps only")
            .AppendLine("(for example 5.5, 6.0, 6.5, never 6.3 or 6.7).")
            .AppendLine("The estimated_band must equal the mean of the four criteria.")
            .AppendLine("If the essay is under the minimum word count, penalize Task Response.")
            .AppendLine("Also give band_low and band_high around the estimate to show examiner variation (usually 0.5 each way).")
            .AppendLine()
            .AppendLine("Return only JSON with this shape:")
            .AppendLine("{")
            .AppendLine("  \"estimated_band\": 6.5,")
            .AppendLine("  \"band_low\": 6.0,")
            .AppendLine("  \"band_high\": 7.0,")
            .AppendLine("  \"task_response\": 6.5,")
            .AppendLine("  \"coherence\": 6.0,")
            .AppendLine("  \"lexical_resource\": 6.5,")
            .AppendLine("  \"grammar\": 6.0,")
            .AppendLine("  \"summary\": \"two short sentences\",")
            .AppendLine("  \"strengths\": [\"point\"],")
            .AppendLine("  \"improvements\": [\"point with a fix\"],")
            .AppendLine("  \"corrected_excerpt\": \"rewrite one weak sentence\"")
            .AppendLine("}")
            .ToString();

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);

        if (!result.Success) return WritingFeedbackResult.Fail(result.Error);

        var feedback = ParseFeedback(result.Text);
        if (feedback is null)
            return WritingFeedbackResult.Fail("The model reply could not be read. Try again.");
        return new WritingFeedbackResult(true, Sanitize(feedback, essay, minimumWords, strictness), string.Empty);
    }

    public async Task<SpeakingFeedbackResult> AssessSpeakingAsync(
        string cue, string transcript, MarkingStrictness strictness, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return SpeakingFeedbackResult.Fail(
                "No language model is configured. Open Settings to add one.");
        if (string.IsNullOrWhiteSpace(transcript))
            return SpeakingFeedbackResult.Fail("No spoken answer was recorded.");

        var stance = strictness switch
        {
            MarkingStrictness.Lenient => "Be encouraging, reward communication.",
            MarkingStrictness.Strict => "Be very strict. Penalize hesitation, limited words, grammar slips, and unclear sounds.",
            _ => "Be fair like a typical examiner."
        };

        var user = new StringBuilder()
            .AppendLine($"Speaking cue: {cue}")
            .AppendLine()
            .AppendLine("Student transcript (typed from their speech):")
            .AppendLine(transcript)
            .AppendLine()
            .AppendLine($"Marking level: {IeltsBanding.StrictnessLabel(strictness)}. {stance}")
            .AppendLine("Score Fluency and Coherence, Lexical Resource, Grammar, Pronunciation in half bands.")
            .AppendLine("estimated_band is the mean of the four. Give band_low and band_high 0.5 each way.")
            .AppendLine("Return only JSON: {\"estimated_band\": 6.5, \"band_low\": 6.0, \"band_high\": 7.0,")
            .AppendLine(" \"fluency\": 6.0, \"lexical_resource\": 6.5, \"grammar\": 6.0, \"pronunciation\": 6.0,")
            .AppendLine(" \"summary\": \"two sentences\", \"strengths\": [\"point\"], \"improvements\": [\"point with a fix\"]}")
            .ToString();

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
        if (!result.Success) return SpeakingFeedbackResult.Fail(result.Error);

        var json = ExtractJsonObject(result.Text);
        if (json is null) return SpeakingFeedbackResult.Fail("The model reply could not be read. Try again.");
        try
        {
            var raw = JsonSerializer.Deserialize<SpeakingFeedback>(json);
            if (raw is null) return SpeakingFeedbackResult.Fail("The model reply could not be read. Try again.");
            var fluency = IeltsBanding.RoundHalf(raw.Fluency);
            var lexical = IeltsBanding.RoundHalf(raw.LexicalResource);
            var grammar = IeltsBanding.RoundHalf(raw.Grammar);
            var pron = IeltsBanding.RoundHalf(raw.Pronunciation);
            var mid = IeltsBanding.RoundHalf((fluency + lexical + grammar + pron) / 4.0);
            var range = IeltsBanding.ToRange(mid, strictness);
            return new SpeakingFeedbackResult(true, new SpeakingFeedback
            {
                EstimatedBand = mid,
                BandLow = range.Low,
                BandHigh = range.High,
                Fluency = fluency,
                LexicalResource = lexical,
                Grammar = grammar,
                Pronunciation = pron,
                Summary = raw.Summary ?? string.Empty,
                Strengths = raw.Strengths ?? new(),
                Improvements = raw.Improvements ?? new()
            }, string.Empty);
        }
        catch (JsonException)
        {
            return SpeakingFeedbackResult.Fail("The model reply could not be read. Try again.");
        }
    }

    public Task<LlmResult> SuggestTopicAsync(string skill, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return Task.FromResult(LlmResult.Fail(
                "No language model is configured. Open Settings to add one."));
        var user = $"Suggest one {skill} mock test prompt for IELTS. " +
                   "Reply with two lines only. Line 1: topic. Line 2: the task or cue.";
        return _llm.CompleteAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    /// <summary>
    /// Explains one wrong Reading answer in two short sentences: why the
    /// correct key is right and why the chosen key is wrong.
    /// </summary>
    public Task<LlmResult> ExplainReadingAsync(
        string passage, string question, string chosenKey, string correctKey, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return Task.FromResult(LlmResult.Fail(
                "No language model is configured. Open Settings to add one."));
        var picked = string.IsNullOrWhiteSpace(chosenKey) ? "no answer" : chosenKey;
        var user =
            "Passage:\n" + passage + "\n\n" +
            "Question:\n" + question + "\n\n" +
            $"The student chose {picked}. The correct answer is {correctKey}.\n" +
            "In two short sentences, say why the correct answer is right " +
            "and why the student choice is wrong. Quote the passage.";
        return _llm.CompleteAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    /// <summary>
    /// Enforces real IELTS band rules on a model reply, because models are bad
    /// at arithmetic: every band is clamped to 0-9 and snapped to a half band,
    /// and the overall band is the mean of the four criteria. An essay under
    /// the minimum word count is capped, as an examiner would penalize it.
    /// The band is then widened to a range for the chosen strictness.
    /// </summary>
    private static WritingFeedback Sanitize(WritingFeedback raw, string essay, int minimumWords, MarkingStrictness strictness)
    {
        var task = RoundHalf(Clamp(raw.TaskResponse));
        var coherence = RoundHalf(Clamp(raw.Coherence));
        var lexical = RoundHalf(Clamp(raw.LexicalResource));
        var grammar = RoundHalf(Clamp(raw.Grammar));
        var overall = RoundHalf((task + coherence + lexical + grammar) / 4.0);

        var summary = raw.Summary ?? string.Empty;
        if (CountWords(essay) < minimumWords)
        {
            task = Math.Min(task, 5.0);
            overall = Math.Min(overall, 5.0);
            summary = string.IsNullOrWhiteSpace(summary)
                ? "The essay is under the minimum word count, so the score is capped."
                : summary + " The essay is under the minimum word count, so the score is capped.";
        }

        var range = IeltsBanding.ToRange(overall, strictness);
        return new WritingFeedback
        {
            EstimatedBand = overall,
            BandLow = range.Low,
            BandHigh = range.High,
            TaskResponse = task,
            Coherence = coherence,
            LexicalResource = lexical,
            Grammar = grammar,
            Summary = summary,
            Strengths = raw.Strengths ?? new(),
            Improvements = raw.Improvements ?? new(),
            CorrectedExcerpt = raw.CorrectedExcerpt ?? string.Empty
        };
    }

    private static double Clamp(double band)
        => double.IsFinite(band) ? Math.Clamp(band, 0.0, 9.0) : 0.0;

    /// <summary>Official IELTS rounding: mean snapped to the nearest half band.</summary>
    private static double RoundHalf(double band)
        => Math.Round(band * 2.0, MidpointRounding.AwayFromZero) / 2.0;

    private static int CountWords(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

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
