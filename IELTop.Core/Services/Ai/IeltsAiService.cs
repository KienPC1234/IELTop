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

    /// <summary>Task 1 key. The prompt asks for one of the two keys.</summary>
    [JsonPropertyName("task_achievement")]
    public double TaskAchievement { get; set; }

    [JsonPropertyName("coherence")]
    public double Coherence { get; set; }

    [JsonPropertyName("lexical_resource")]
    public double LexicalResource { get; set; }

    [JsonPropertyName("grammar")]
    public double Grammar { get; set; }

    [JsonPropertyName("task_response_why")]
    public string TaskResponseWhy { get; set; } = string.Empty;

    [JsonPropertyName("task_achievement_why")]
    public string TaskAchievementWhy { get; set; } = string.Empty;

    [JsonPropertyName("coherence_why")]
    public string CoherenceWhy { get; set; } = string.Empty;

    [JsonPropertyName("lexical_why")]
    public string LexicalWhy { get; set; } = string.Empty;

    [JsonPropertyName("grammar_why")]
    public string GrammarWhy { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("strengths")]
    public List<string> Strengths { get; set; } = new();

    [JsonPropertyName("improvements")]
    public List<string> Improvements { get; set; } = new();

    [JsonPropertyName("corrected_excerpt")]
    public string CorrectedExcerpt { get; set; } = string.Empty;

    [JsonPropertyName("grammar_errors")]
    public List<GrammarErrorItem> GrammarErrors { get; set; } = new();

    public string BandLabel => $"{IeltsBanding.RoundHalf(BandLow):0.0} to {IeltsBanding.RoundHalf(BandHigh):0.0}";
}

public sealed class GrammarErrorItem
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    [JsonPropertyName("correction")]
    public string Correction { get; set; } = string.Empty;

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;
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

    [JsonPropertyName("fluency_why")]
    public string FluencyWhy { get; set; } = string.Empty;

    [JsonPropertyName("lexical_why")]
    public string LexicalWhy { get; set; } = string.Empty;

    [JsonPropertyName("grammar_why")]
    public string GrammarWhy { get; set; } = string.Empty;

    [JsonPropertyName("pronunciation_why")]
    public string PronunciationWhy { get; set; } = string.Empty;

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

/// <summary>Part ids the model picked for a full test, with its reason.</summary>
public sealed record TestPickResult(
    bool Success, IReadOnlyList<string> Ids, string Reason, string Error)
{
    public static TestPickResult Fail(string error)
        => new(false, Array.Empty<string>(), string.Empty, error);
}

/// <summary>Quality verdict for one mock test paper.</summary>
public sealed record PaperReviewResult(
    bool Success, double Score, IReadOnlyList<string> Strengths, IReadOnlyList<string> Fixes, string Error)
{
    public static PaperReviewResult Fail(string error)
        => new(false, 0, Array.Empty<string>(), Array.Empty<string>(), error);
}

/// <summary>One paper drafted by the model from pasted or imported text.</summary>
public sealed record PaperDraftResult(
    bool Success, IELTop.Models.ExamPaper? Paper, string Json, string Error)
{
    public static PaperDraftResult Fail(string error)
        => new(false, null, string.Empty, error);
}

public interface IIeltsAiService
{
    bool IsAvailable { get; }
    bool VisionAvailable { get; }
    Task<WritingFeedbackResult> ReviewWritingAsync(string taskPrompt, string essay, int minimumWords, CancellationToken ct = default);
    Task<WritingFeedbackResult> ReviewWritingAsync(string taskPrompt, string essay, int minimumWords, MarkingStrictness strictness, CancellationToken ct = default);
    Task<SpeakingFeedbackResult> AssessSpeakingAsync(string cue, string transcript, MarkingStrictness strictness, CancellationToken ct = default);
    Task<SpeakingFeedbackResult> AssessSpeakingAsync(string cue, string transcript, string partInfo, int spokenSeconds, MarkingStrictness strictness, CancellationToken ct = default);
    Task<LlmResult> ExplainReadingAsync(string passage, string question, string chosenKey, string correctKey, CancellationToken ct = default);
    Task<LlmResult> ExplainListeningAsync(string transcript, string question, string chosenAnswer, string correctAnswer, CancellationToken ct = default);
    Task<LlmResult> SuggestTopicAsync(string skill, CancellationToken ct = default);
    Task<TestPickResult> PickTestAsync(string catalog, string history, CancellationToken ct = default);
    Task<PaperReviewResult> ReviewPaperAsync(IELTop.Models.ExamPaper paper, CancellationToken ct = default);
    Task<PaperDraftResult> DraftPaperAsync(string rawText, string hint, CancellationToken ct = default);
    IAsyncEnumerable<string> CoachSpeakingAsync(string target, string heardPhonemes, string mistakes, CancellationToken ct = default);
    Task<LlmResult> ReadImageAsync(string prompt, string base64Image, string mediaType, CancellationToken ct = default);
    /// <summary>Reads one imported picture with a vision model for the import flow.</summary>
    Task<LlmResult> ReadImportImageAsync(IELTop.Services.Storage.ImportedImage image, string skill, CancellationToken ct = default);
    Task<LlmResult> TestAsync(CancellationToken ct = default);
}

/// <summary>
/// Builds IELTS specific prompts and parses the model replies.
/// Keeps prompt wording and JSON parsing in one place.
/// </summary>
public sealed class IeltsAiService : IIeltsAiService
{
    private readonly ILlmService _llm;
    private readonly IELTop.Services.Storage.ISettingsStore _settings;

    private static readonly JsonSerializerOptions FeedbackJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public IeltsAiService(ILlmService llm, IELTop.Services.Storage.ISettingsStore settings)
    {
        _llm = llm;
        _settings = settings;
    }

    private string SystemPrompt => string.IsNullOrWhiteSpace(_settings.Current.LlmSystemPrompt)
        ? LlmPrompts.DefaultSystemPrompt
        : _settings.Current.LlmSystemPrompt.Trim();

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

    /// <summary>
    /// Reads one imported picture with a vision model. Text pages come back
    /// as transcription, charts come back as a word description usable as a
    /// Writing Task 1 task. Refuses early without a vision model.
    /// </summary>
    public Task<LlmResult> ReadImportImageAsync(
        IELTop.Services.Storage.ImportedImage image, string skill, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return Task.FromResult(LlmResult.Fail(
                "No language model is configured. Open Settings to add one."));
        if (!_llm.VisionEnabled)
            return Task.FromResult(LlmResult.Fail(
                "Vision is turned off. Enable it in Settings to read pictures."));
        return ReadImageAsync(
            LlmPrompts.BuildReadImage(image.Name, skill), image.Base64, image.MediaType, ct);
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

        var stance = LlmPrompts.WritingStance(strictness);

        var user = LlmPrompts.BuildWritingPrompt(
            taskPrompt, essay, minimumWords,
            IeltsBanding.StrictnessLabel(strictness), stance);

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);

        if (!result.Success) return WritingFeedbackResult.Fail(result.Error);

        var feedback = ParseFeedback(result.Text);
        if (feedback is null)
            return WritingFeedbackResult.Fail("The model reply could not be read. Try again.");
        return new WritingFeedbackResult(true, Sanitize(feedback, essay, minimumWords, strictness), string.Empty);
    }

    public Task<SpeakingFeedbackResult> AssessSpeakingAsync(
        string cue, string transcript, MarkingStrictness strictness, CancellationToken ct = default)
        => AssessSpeakingAsync(cue, transcript, string.Empty, 0, strictness, ct);

    public async Task<SpeakingFeedbackResult> AssessSpeakingAsync(
        string cue, string transcript, string partInfo, int spokenSeconds, MarkingStrictness strictness, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return SpeakingFeedbackResult.Fail(
                "No language model is configured. Open Settings to add one.");
        if (string.IsNullOrWhiteSpace(transcript))
            return SpeakingFeedbackResult.Fail("No spoken answer was recorded.");

        var stance = LlmPrompts.SpeakingStance(strictness);

        int words = CountWords(transcript);
        var user = LlmPrompts.BuildSpeakingPrompt(
            cue, transcript, partInfo, spokenSeconds, words,
            IeltsBanding.StrictnessLabel(strictness), stance);

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
        if (!result.Success) return SpeakingFeedbackResult.Fail(result.Error);

        var json = ExtractJsonObject(result.Text);
        if (json is null) return SpeakingFeedbackResult.Fail("The model reply could not be read. Try again.");
        try
        {
            var raw = JsonSerializer.Deserialize<SpeakingFeedback>(json, FeedbackJsonOptions);
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
                FluencyWhy = raw.FluencyWhy ?? string.Empty,
                LexicalWhy = raw.LexicalWhy ?? string.Empty,
                GrammarWhy = raw.GrammarWhy ?? string.Empty,
                PronunciationWhy = raw.PronunciationWhy ?? string.Empty,
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
        var clean = string.IsNullOrWhiteSpace(skill) ? "Writing" : skill.Trim();
        var user = LlmPrompts.BuildSuggestTopic(clean);
        return _llm.CompleteAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    /// <summary>
    /// Builds a full test from the catalog below. Listening parts only work
    /// inside a multi skill test, so include them only with other skills.
    /// Reply with only JSON: {"ids": ["paper|part", ...], "reason": "one sentence"}.
    /// Order the ids in test order: Listening, Reading, Writing, Speaking.
    /// Favor the weakest skills from the history. Pick 4 to 8 parts.
    /// </summary>
    public async Task<TestPickResult> PickTestAsync(
        string catalog, string history, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return TestPickResult.Fail(
                "No language model is configured. Open Settings to add one.");

        var user = LlmPrompts.BuildPickTest(catalog, history);

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
        if (!result.Success) return TestPickResult.Fail(result.Error);

        var json = ExtractJsonObject(result.Text);
        if (json is null) return TestPickResult.Fail("The model reply could not be read. Try again.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var ids = doc.RootElement.TryGetProperty("ids", out var list)
                ? list.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => s.Contains('|'))
                    .Distinct()
                    .Take(8)
                    .ToList()
                : new List<string>();
            var reason = doc.RootElement.TryGetProperty("reason", out var why)
                ? why.GetString() ?? string.Empty
                : string.Empty;
            if (ids.Count < 2)
                return TestPickResult.Fail("The model picked too few parts. Try again.");
            return new TestPickResult(true, ids, reason, string.Empty);
        }
        catch (JsonException)
        {
            return TestPickResult.Fail("The model reply could not be read. Try again.");
        }
    }

    /// <summary>
    /// Rates one mock test paper: skill balance, instruction clarity, and
    /// answer key sanity. Score is 0 to 10, practice guidance only.
    /// </summary>
    public async Task<PaperReviewResult> ReviewPaperAsync(
        IELTop.Models.ExamPaper paper, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return PaperReviewResult.Fail(
                "No language model is configured. Open Settings to add one.");

        var outline = new StringBuilder()
            .AppendLine($"Title: {paper.Title}")
            .AppendLine($"Category: {paper.Category}, Level: {paper.Level}");
        foreach (var part in paper.Parts.Take(12))
        {
            int choice = part.Questions.Count(q =>
                string.Equals(q.Kind, "choice", StringComparison.OrdinalIgnoreCase));
            int gap = part.Questions.Count(q =>
                string.Equals(q.Kind, "gap", StringComparison.OrdinalIgnoreCase));
            int match = part.Questions.Count(q =>
                string.Equals(q.Kind, "match", StringComparison.OrdinalIgnoreCase));
            outline.AppendLine($"- {part.Skill} {part.Id}: {part.Title} " +
                $"({part.Minutes} min, {part.Questions.Count} questions: {choice} choice, {gap} gap, {match} match)");
        }

        var user = LlmPrompts.BuildReviewPaper(outline.ToString());

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
        if (!result.Success) return PaperReviewResult.Fail(result.Error);

        var json = ExtractJsonObject(result.Text);
        if (json is null) return PaperReviewResult.Fail("The model reply could not be read. Try again.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                ? Math.Clamp(s.GetDouble(), 0, 10)
                : 0;
            static List<string> Strings(JsonElement e, string name)
                => e.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray()
                        .Select(x => x.GetString() ?? string.Empty)
                        .Where(x => x.Length > 0)
                        .Take(3)
                        .ToList()
                    : new List<string>();
            return new PaperReviewResult(true, Math.Round(score, 1),
                Strings(root, "strengths"), Strings(root, "fixes"), string.Empty);
        }
        catch (JsonException)
        {
            return PaperReviewResult.Fail("The model reply could not be read. Try again.");
        }
    }

    /// <summary>
    /// Turns pasted or imported text into one IELTop paper draft.
    /// The caller shows the JSON for review before saving, never auto saves.
    /// </summary>
    public async Task<PaperDraftResult> DraftPaperAsync(
        string rawText, string hint, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return PaperDraftResult.Fail(
                "No language model is configured. Open Settings to add one.");
        if (string.IsNullOrWhiteSpace(rawText))
            return PaperDraftResult.Fail("There is no text to format yet.");

        var clipped = rawText.Trim();
        if (clipped.Length > 12_000)
            clipped = clipped[..12_000];
        var want = string.IsNullOrWhiteSpace(hint) ? "Reading" : hint.Trim();
        var user = LlmPrompts.BuildDraftPaper(clipped, want);

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
        if (!result.Success) return PaperDraftResult.Fail(result.Error);

        var json = ExtractJsonObject(result.Text);
        if (json is null) return PaperDraftResult.Fail("The model reply could not be read. Try again.");
        if (!IELTop.Services.Storage.PaperValidator.TryParse(
            json, out var paper, out var issues) || paper is null)
        {
            // Keep the raw JSON so the editor can load it for hand fixes.
            string pretty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                pretty = JsonSerializer.Serialize(doc.RootElement,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException) { pretty = json; }
            string why = issues.Count > 0 ? issues[0] : "The draft needs fixes before saving.";
            return new PaperDraftResult(false, null, pretty, $"The draft needs fixes: {why}");
        }
        string formatted;
        try
        {
            using var doc = JsonDocument.Parse(json);
            formatted = JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { formatted = json; }
        return new PaperDraftResult(true, paper, formatted, string.Empty);
    }

    /// <summary>
    /// Explains one wrong Reading answer. The caller passes full question
    /// detail including option texts, because keys alone mean nothing.
    /// </summary>
    public Task<LlmResult> ExplainReadingAsync(
        string passage, string question, string chosenKey, string correctKey, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return Task.FromResult(LlmResult.Fail(
                "No language model is configured. Open Settings to add one."));
        var picked = string.IsNullOrWhiteSpace(chosenKey) ? "no answer" : chosenKey;
        var user = LlmPrompts.BuildReadingExplanation(passage, question, picked, correctKey);
        return _llm.CompleteAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    /// <summary>
    /// Explains one wrong Listening answer. Points at the listening trap:
    /// sound alike words, plurals, word limits, or a paraphrase that was missed.
    /// </summary>
    public Task<LlmResult> ExplainListeningAsync(
        string transcript, string question, string chosenAnswer, string correctAnswer, CancellationToken ct = default)
    {
        if (!_llm.IsConfigured)
            return Task.FromResult(LlmResult.Fail(
                "No language model is configured. Open Settings to add one."));
        var picked = string.IsNullOrWhiteSpace(chosenAnswer) ? "no answer" : chosenAnswer;
        var user = LlmPrompts.BuildListeningExplanation(transcript, question, picked, correctAnswer);
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
        double firstRaw = raw.TaskAchievement > 0 ? raw.TaskAchievement : raw.TaskResponse;
        var task = RoundHalf(Clamp(firstRaw));
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
        string firstWhy = !string.IsNullOrWhiteSpace(raw.TaskAchievementWhy)
            ? raw.TaskAchievementWhy
            : raw.TaskResponseWhy ?? string.Empty;
        return new WritingFeedback
        {
            EstimatedBand = overall,
            BandLow = range.Low,
            BandHigh = range.High,
            TaskResponse = task,
            TaskAchievement = task,
            Coherence = coherence,
            LexicalResource = lexical,
            Grammar = grammar,
            TaskResponseWhy = firstWhy,
            TaskAchievementWhy = firstWhy,
            CoherenceWhy = raw.CoherenceWhy ?? string.Empty,
            LexicalWhy = raw.LexicalWhy ?? string.Empty,
            GrammarWhy = raw.GrammarWhy ?? string.Empty,
            Summary = summary,
            Strengths = raw.Strengths ?? new(),
            Improvements = raw.Improvements ?? new(),
            CorrectedExcerpt = raw.CorrectedExcerpt ?? string.Empty,
            GrammarErrors = raw.GrammarErrors ?? new()
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
        var user = LlmPrompts.BuildCoachSpeaking(target, heardPhonemes, mistakes);
        return _llm.StreamAsync(new[] { LlmMessage.System(SystemPrompt), LlmMessage.User(user) }, ct);
    }

    private static WritingFeedback? ParseFeedback(string raw)
    {
        var json = ExtractJsonObject(raw);
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WritingFeedback>(json, FeedbackJsonOptions);
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
