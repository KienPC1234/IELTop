using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IELTop_Content_Server.Options;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// What a review produced. Score is 0 to 100, minus one means the model
/// was not consulted. Tags are lowercase single words or short phrases.
/// </summary>
public sealed class ReviewResult
{
    public bool Ran { get; set; }
    public bool Passed { get; set; }
    public int Score { get; set; } = -1;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<string> Problems { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public bool LooksLikeStandardPaper { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public interface ILlmReviewService
{
    bool Enabled { get; }
    Task<ReviewResult> ReviewAsync(string title, string source, string license, string paperText, CancellationToken ct = default);
}

/// <summary>
/// Reviews a submitted paper with any OpenAI compatible chat endpoint.
/// When no model is configured the review is skipped and the submission
/// waits for a human, so the portal never depends on a model.
/// </summary>
public sealed class LlmReviewService(
    IHttpClientFactory clients,
    IOptions<LlmOptions> options,
    IOptions<ContributeOptions> contribute,
    ILogger<LlmReviewService> logger) : ILlmReviewService
{
    private readonly LlmOptions _llm = options.Value;
    private readonly ContributeOptions _contribute = contribute.Value;

    public bool Enabled => _llm.Enabled;

    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ReviewResult> ReviewAsync(
        string title, string source, string license, string paperText, CancellationToken ct = default)
    {
        if (!Enabled)
            return new ReviewResult { Ran = false, Error = "No model is configured." };

        string prompt = BuildPrompt(title, source, license, paperText);
        var client = clients.CreateClient("llm");
        var payload = new
        {
            model = _llm.Model,
            temperature = _llm.Temperature,
            max_tokens = _llm.MaxTokens,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = prompt }
            }
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_llm.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(_llm.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _llm.ApiKey);

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Clamp(_llm.TimeoutSeconds, 15, 300)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            using var response = await client.SendAsync(request, linked.Token);
            string body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Review model returned HTTP {Status}", (int)response.StatusCode);
                return new ReviewResult
                {
                    Ran = false,
                    Error = $"The review model returned an error ({(int)response.StatusCode})."
                };
            }

            string content = ExtractContent(body);
            return Parse(content);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ReviewResult { Ran = false, Error = "The review model timed out." };
        }
        catch (HttpRequestException)
        {
            return new ReviewResult { Ran = false, Error = "The review model could not be reached." };
        }
        catch (JsonException)
        {
            return new ReviewResult { Ran = false, Error = "The review model reply could not be read." };
        }
    }

    private const string SystemPrompt =
        "You are the review editor for an IELTS practice library. You check that a "
        + "submitted paper is a real practice test and not junk, spam, or copyrighted "
        + "material the author cannot share. You always answer with a single JSON object "
        + "and nothing else. Never invent content that is not in the submission.";

    private string BuildPrompt(string title, string source, string license, string paperText)
    {
        string text = paperText.Length > 24_000 ? paperText[..24_000] : paperText;
        return $$"""
        Review this IELTS practice paper submission.

        Title: {{title}}
        Declared source: {{source}}
        Declared license: {{license}}

        Paper content:
        ---
        {{text}}
        ---

        Answer with one JSON object only, using exactly these keys:
        {
          "looksLikeStandardPaper": true or false,
          "score": integer 0 to 100, how ready it is for students,
          "passed": true or false, whether it should go to a human editor,
          "summary": "one short sentence",
          "tags": ["lowercase topic tags, 1 to 6 of them"],
          "problems": ["concrete problems, empty when none"],
          "suggestions": ["concrete improvements, empty when none"]
        }

        Reject with passed false when the content is spam, gibberish, an answer key "
        + "only, clearly copied from a paid exam without a license, or missing most "
        + "of the questions and answers. Otherwise pass it for a human editor.
        """;
    }

    private ReviewResult Parse(string content)
    {
        string json = ExtractJsonObject(content);
        if (json.Length == 0)
            return new ReviewResult { Ran = true, Error = "The review reply had no JSON object." };

        try
        {
            var parsed = JsonSerializer.Deserialize<ModelReview>(json, ReadJson);
            if (parsed is null)
                return new ReviewResult { Ran = true, Error = "The review reply could not be read." };

            return new ReviewResult
            {
                Ran = true,
                Passed = parsed.Passed,
                Score = Math.Clamp(parsed.Score, 0, 100),
                Summary = (parsed.Summary ?? string.Empty).Trim(),
                Tags = Clean(parsed.Tags),
                Problems = Clean(parsed.Problems),
                Suggestions = Clean(parsed.Suggestions),
                LooksLikeStandardPaper = parsed.LooksLikeStandardPaper,
                RawJson = json
            };
        }
        catch (JsonException)
        {
            return new ReviewResult { Ran = true, Error = "The review reply could not be read." };
        }
    }

    private static List<string> Clean(List<string>? items)
    {
        if (items is null)
            return new();
        return items
            .Select(t => (t ?? string.Empty).Trim().ToLowerInvariant())
            .Where(t => t.Length is > 0 and <= 40)
            .Distinct()
            .Take(12)
            .ToList();
    }

    /// <summary>
    /// The model sometimes wraps JSON in prose or a fenced block. This
    /// pulls the first balanced object out so a chatty reply still works.
    /// </summary>
    private static string ExtractJsonObject(string content)
    {
        int start = content.IndexOf('{');
        if (start < 0)
            return string.Empty;

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < content.Length; i++)
        {
            char c = content[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return content[start..(i + 1)];
                    break;
            }
        }
        return string.Empty;
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

    private sealed class ModelReview
    {
        [JsonPropertyName("looksLikeStandardPaper")]
        public bool LooksLikeStandardPaper { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("passed")]
        public bool Passed { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("problems")]
        public List<string>? Problems { get; set; }

        [JsonPropertyName("suggestions")]
        public List<string>? Suggestions { get; set; }
    }
}
