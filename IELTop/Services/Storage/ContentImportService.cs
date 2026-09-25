using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IELTop.Models;
using IELTop.Services.Ai;

namespace IELTop.Services.Storage;

/// <summary>
/// What part of the app imported content belongs to.
/// </summary>
public enum ContentKind
{
    Reading,
    Listening,
    Writing,
    Exam
}

/// <summary>
/// Result of reading one file: extracted text plus a guess at its kind and a
/// first draft of the structured content.
/// </summary>
public sealed record ImportDraft(
    bool Success,
    string Error,
    string Title,
    ContentKind Kind,
    string RawText,
    ReadingPassage? Reading,
    ListeningItem? Listening,
    WritingTask? Writing,
    bool DraftedByAi)
{
    public static ImportDraft Fail(string error)
        => new(false, error, string.Empty, ContentKind.Reading, string.Empty, null, null, null, false);
}

public interface IContentImportService
{
    bool AiAvailable { get; }
    bool VisionAvailable { get; }
    Task<ImportDraft> ImportAsync(string path, ContentKind? forcedKind, CancellationToken ct = default);
    Task<ImportDraft> ImportImageAsync(string path, CancellationToken ct = default);
}

/// <summary>
/// Turns a picked file into practice content. With a language model configured
/// it drafts the title and the multiple choice questions, then the user reviews
/// and edits. Without a model it still imports the raw text and the user fills
/// in the rest, so nothing is blocked.
/// </summary>
public sealed class ContentImportService : IContentImportService
{
    private readonly IFileTextExtractor _extractor;
    private readonly ILlmService _llm;
    private readonly IIeltsAiService _ai;

    public ContentImportService(IFileTextExtractor extractor, ILlmService llm, IIeltsAiService ai)
    {
        _extractor = extractor;
        _llm = llm;
        _ai = ai;
    }

    public bool AiAvailable => _llm.IsConfigured;

    public bool VisionAvailable => _ai.VisionAvailable;

    /// <summary>
    /// Reads an image with a vision model, then treats the text it returns like
    /// any other import. Without vision this reports clearly what to change.
    /// </summary>
    public async Task<ImportDraft> ImportImageAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return ImportDraft.Fail("The image was not found.");
        if (!_ai.IsAvailable)
            return ImportDraft.Fail("Reading images needs a language model. Open Settings to add one.");
        if (!_ai.VisionAvailable)
            return ImportDraft.Fail("Vision is turned off. Enable it in Settings to read images.");

        string base64;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            base64 = Convert.ToBase64String(bytes);
        }
        catch (Exception)
        {
            return ImportDraft.Fail("The image could not be read.");
        }

        var prompt =
            "Read this image and write out the study text it contains. " +
            "If it holds questions, keep them. Reply with the plain text only, no extra comments.";

        var result = await _ai.ReadImageAsync(prompt, base64, _extractor.MediaTypeOf(path), ct);
        if (!result.Success)
            return ImportDraft.Fail(result.Error);

        var title = Path.GetFileNameWithoutExtension(path);
        return BuildManualDraft(title, GuessKind(path, result.Text), result.Text) with { DraftedByAi = true };
    }

    public async Task<ImportDraft> ImportAsync(string path, ContentKind? forcedKind, CancellationToken ct = default)
    {
        // Images take the vision path. Text files use the extractor.
        if (_extractor.IsImage(path))
            return await ImportImageAsync(path, ct);

        var extracted = await _extractor.ExtractAsync(path, ct);
        if (!extracted.Success)
            return ImportDraft.Fail(extracted.Error);

        var title = Path.GetFileNameWithoutExtension(path);
        var text = extracted.Text;

        // Guessing the kind needs no model: keywords in the file are enough for a first pass.
        var kind = forcedKind ?? GuessKind(path, text);

        if (!AiAvailable)
            return BuildManualDraft(title, kind, text);

        var drafted = await TryAiDraftAsync(title, kind, text, ct);
        return drafted ?? BuildManualDraft(title, kind, text);
    }

    private ImportDraft BuildManualDraft(string title, ContentKind kind, string text)
    {
        // No model: keep the text and create an empty shell the user can fill.
        return kind switch
        {
            ContentKind.Reading => new ImportDraft(true, string.Empty, title, kind, text,
                new ReadingPassage { Id = MakeId("R", title), Title = title, Source = "Imported by the user", Body = text },
                null, null, false),
            ContentKind.Listening => new ImportDraft(true, string.Empty, title, kind, text,
                null,
                new ListeningItem { Id = MakeId("L", title), Title = title, Source = "Imported by the user", Transcript = text },
                null, false),
            ContentKind.Writing => new ImportDraft(true, string.Empty, title, kind, text,
                null, null,
                new WritingTask { Id = MakeId("W", title), Title = title, Source = "Imported by the user", Prompt = text },
                false),
            _ => new ImportDraft(true, string.Empty, title, kind, text,
                new ReadingPassage { Id = MakeId("R", title), Title = title, Source = "Imported by the user", Body = text },
                null, null, false),
        };
    }

    /// <summary>
    /// Ask the model for a structured draft. Returns null on any problem so the
    /// caller can fall back to the manual draft instead of failing the import.
    /// </summary>
    private async Task<ImportDraft?> TryAiDraftAsync(string title, ContentKind kind, string text, CancellationToken ct)
    {
        var clipped = text.Length > 6000 ? text[..6000] : text;
        var prompt =
            "You turn raw study material into structured IELTS practice content.\n" +
            $"The material is likely a {kind} piece.\n\n" +
            "Raw material:\n" + clipped + "\n\n" +
            "Reply with JSON only, in this shape:\n" +
            "{\n" +
            "  \"title\": \"short title\",\n" +
            "  \"passage\": \"the main text, cleaned up. Keep it under 400 words.\",\n" +
            "  \"transcript\": \"spoken text if this is listening, else empty\",\n" +
            "  \"prompt\": \"the essay question if this is writing, else empty\",\n" +
            "  \"questions\": [\n" +
            "    { \"prompt\": \"question text\", \"options\": [\n" +
            "        {\"key\":\"A\",\"text\":\"...\"},{\"key\":\"B\",\"text\":\"...\"},{\"key\":\"C\",\"text\":\"...\"} ],\n" +
            "      \"correctKey\": \"B\" }\n" +
            "  ]\n" +
            "}";

        var result = await _llm.CompleteAsync(
            new[] { LlmMessage.System("You prepare IELTS study material and reply with JSON only."), LlmMessage.User(prompt) },
            ct);

        if (!result.Success) return null;

        var draft = ParseDraft(result.Text);
        if (draft is null) return null;

        var finalTitle = string.IsNullOrWhiteSpace(draft.Title) ? title : draft.Title.Trim();
        var questions = ToExamQuestions(draft.Questions);

        return kind switch
        {
            ContentKind.Listening => new ImportDraft(true, string.Empty, finalTitle, kind, text,
                null,
                new ListeningItem
                {
                    Id = MakeId("L", finalTitle),
                    Title = finalTitle,
                    Source = "Imported by the user",
                    Transcript = string.IsNullOrWhiteSpace(draft.Transcript) ? text : draft.Transcript,
                    Questions = questions
                },
                null, true),

            ContentKind.Writing => new ImportDraft(true, string.Empty, finalTitle, kind, text,
                null, null,
                new WritingTask
                {
                    Id = MakeId("W", finalTitle),
                    Title = finalTitle,
                    Source = "Imported by the user",
                    Prompt = string.IsNullOrWhiteSpace(draft.Prompt) ? text : draft.Prompt
                },
                true),

            _ => new ImportDraft(true, string.Empty, finalTitle, kind, text,
                new ReadingPassage
                {
                    Id = MakeId("R", finalTitle),
                    Title = finalTitle,
                    Source = "Imported by the user",
                    Body = string.IsNullOrWhiteSpace(draft.Passage) ? text : draft.Passage,
                    Questions = questions
                },
                null, null, true)
        };
    }

    private static List<ExamQuestion> ToExamQuestions(List<AiQuestion>? source)
    {
        var list = new List<ExamQuestion>();
        if (source is null) return list;

        int number = 1;
        foreach (var q in source)
        {
            if (string.IsNullOrWhiteSpace(q.Prompt) || q.Options is null || q.Options.Count == 0)
                continue;

            var options = q.Options
                .Where(o => !string.IsNullOrWhiteSpace(o.Text))
                .Select((o, i) => new ExamOption
                {
                    Key = string.IsNullOrWhiteSpace(o.Key) ? ((char)('A' + i)).ToString() : o.Key,
                    Text = o.Text
                })
                .ToList();

            if (options.Count == 0) continue;

            var correct = options.Any(o => o.Key == q.CorrectKey)
                ? q.CorrectKey
                : options[0].Key;

            list.Add(new ExamQuestion
            {
                Number = number++,
                Prompt = q.Prompt,
                Options = options,
                CorrectKey = correct
            });
        }
        return list;
    }

    private static AiDraft? ParseDraft(string raw)
    {
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            return JsonSerializer.Deserialize<AiDraft>(raw[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keyword hints. Cheap, offline, and good enough to pick a starting kind.
    /// </summary>
    private static ContentKind GuessKind(string path, string text)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var head = text.Length > 2000 ? text[..2000].ToLowerInvariant() : text.ToLowerInvariant();
        var haystack = name + " " + head;

        if (haystack.Contains("listening") || haystack.Contains("transcript") || haystack.Contains("audio"))
            return ContentKind.Listening;
        if (haystack.Contains("writing") || haystack.Contains("essay") || haystack.Contains("task 2"))
            return ContentKind.Writing;
        if (haystack.Contains("mock") || haystack.Contains("full test") || haystack.Contains("practice test"))
            return ContentKind.Exam;
        return ContentKind.Reading;
    }

    private static string MakeId(string prefix, string title)
    {
        var slug = new string(title
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 40) slug = slug[..40];
        return $"{prefix}-{slug}";
    }

    // Shapes for deserializing the model reply.
    private sealed class AiDraft
    {
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("passage")] public string Passage { get; set; } = string.Empty;
        [JsonPropertyName("transcript")] public string Transcript { get; set; } = string.Empty;
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
        [JsonPropertyName("questions")] public List<AiQuestion>? Questions { get; set; }
    }

    private sealed class AiQuestion
    {
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = string.Empty;
        [JsonPropertyName("options")] public List<AiOption>? Options { get; set; }
        [JsonPropertyName("correctKey")] public string CorrectKey { get; set; } = string.Empty;
    }

    private sealed class AiOption
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }
}
