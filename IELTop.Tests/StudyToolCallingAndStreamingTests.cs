using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Learn;
using IELTop.Services.Storage;
using Xunit;

namespace IELTop.Tests;

[Collection("AppState")]
public sealed class StudyToolCallingAndStreamingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _lessonDir;
    private readonly LessonService _lessons;

    public StudyToolCallingAndStreamingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_tools_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();

        _lessonDir = Path.Combine(Path.GetTempPath(), $"ieltop_tool_lessons_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_lessonDir);
        LessonService.SetUserDirForTesting(_lessonDir);

        File.WriteAllText(Path.Combine(_lessonDir, "unit-1.json"), """
        {
          "unit": "Unit 1",
          "title": "Unit 1",
          "source": "test",
          "sections": [
            {
              "id": "sec-1",
              "skill": "Grammar",
              "topic": "Relative Clauses",
              "title": "Defining Relative Clauses",
              "isAnswerKey": false,
              "blocks": [
                {
                  "type": "text",
                  "text": "Defining relative clauses specify which person or thing we are talking about using who, which, or that."
                }
              ]
            }
          ],
          "vocabulary": [
            {
              "word": "ecosystem",
              "group": "environment",
              "form": "noun",
              "meaning": "All the living things in an area and the way they affect each other.",
              "example": "Pollution can damage fragile marine ecosystems.",
              "ipa": "/ˈiː.kəʊˌsɪs.təm/"
            }
          ]
        }
        """, System.Text.Encoding.UTF8);

        _lessons = new LessonService();
        _lessons.Reload();
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        LessonService.SetUserDirForTesting(null);
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_lessonDir, true); } catch { }
    }

    [Fact]
    public void Tutor_tools_have_valid_openai_function_definitions()
    {
        Assert.NotEmpty(StudyService.TutorTools);
        Assert.Equal(3, StudyService.TutorTools.Count);

        var toolNames = StudyService.TutorTools.Select(t => t.Name).ToList();
        Assert.Contains("search_curriculum", toolNames);
        Assert.Contains("get_vocabulary_entry", toolNames);
        Assert.Contains("generate_interactive_exercise", toolNames);

        foreach (var tool in StudyService.TutorTools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.False(string.IsNullOrWhiteSpace(tool.ParametersJsonSchema));

            // Validate that the JSON Schema is well formed JSON
            using var doc = JsonDocument.Parse(tool.ParametersJsonSchema);
            Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
            Assert.True(doc.RootElement.TryGetProperty("properties", out _));
        }
    }

    [Fact]
    public async Task Tool_executor_runs_curriculum_search_and_returns_valid_json()
    {
        var mockLlm = new FakeLlm("Mock reply");
        var study = new StudyService(_lessons, mockLlm);

        string args = "{\"query\":\"relative clauses\",\"unit\":\"\"}";
        string resultJson = await study.ExecuteTutorToolAsync("search_curriculum", args, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(resultJson));
        using var doc = JsonDocument.Parse(resultJson);
        Assert.True(doc.RootElement.TryGetProperty("result", out var res));
        Assert.True(res.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Tool_executor_runs_vocabulary_lookup_and_returns_definition()
    {
        var mockLlm = new FakeLlm("Mock reply");
        var study = new StudyService(_lessons, mockLlm);

        string args = "{\"word\":\"ecosystem\"}";
        string resultJson = await study.ExecuteTutorToolAsync("get_vocabulary_entry", args, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(resultJson));
        using var doc = JsonDocument.Parse(resultJson);
        Assert.Equal("ecosystem", doc.RootElement.GetProperty("word").GetString());
        Assert.Contains("living things", doc.RootElement.GetProperty("definition").GetString());
        Assert.Equal("/ˈiː.kəʊˌsɪs.təm/", doc.RootElement.GetProperty("ipa").GetString());
    }

    [Fact]
    public async Task AskAsync_streams_chunks_to_broadcaster_in_real_time()
    {
        var chunks = new List<string>();
        bool sawDone = false;

        var streamingLlm = new StreamingMockLlm(new[] { "Defining ", "relative ", "clauses ", "explain ", "things." });
        var study = new StudyService(_lessons, streamingLlm);

        study.SetBroadcaster((eventName, payload) =>
        {
            if (eventName == "study.chat.chunk")
            {
                var json = JsonSerializer.Serialize(payload);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("delta", out var d))
                {
                    var text = d.GetString() ?? "";
                    if (!string.IsNullOrEmpty(text)) chunks.Add(text);
                }
                if (doc.RootElement.TryGetProperty("isDone", out var isDone) && isDone.GetBoolean())
                {
                    sawDone = true;
                }
            }
        });

        var session = study.NewChatSession("");
        var snapshot = await study.AskAsync(session.SelectedSessionId, "How do relative clauses work?", "");

        Assert.True(sawDone, "Broadcaster should fire isDone = true when stream completes");
        Assert.Equal(5, chunks.Count);
        Assert.Equal("Defining relative clauses explain things.", string.Concat(chunks));

        var lastMessage = snapshot.Messages.Last(m => m.Role == "assistant");
        Assert.Equal("Defining relative clauses explain things.", lastMessage.Text);
    }

    [Fact]
    public void CurriculumOverview_exposes_mainTheme_and_grammarFocus()
    {
        var mockLlm = new FakeLlm("Mock reply");
        var study = new StudyService(_lessons, mockLlm);

        var overview = study.CurriculumOverview();
        Assert.NotEmpty(overview);

        var u1 = overview.First(u => u.Slug == "unit-1");
        Assert.Contains("Unit 1", u1.Title);
        Assert.Contains("Relative Clauses", u1.GrammarFocus);
    }

    private sealed class StreamingMockLlm : ILlmService
    {
        private readonly IReadOnlyList<string> _tokens;

        public StreamingMockLlm(IReadOnlyList<string> tokens)
        {
            _tokens = tokens;
        }

        public bool IsConfigured => true;
        public bool UseStreaming => true;
        public bool VisionEnabled => false;

        public Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => Task.FromResult(new LlmResult(true, string.Concat(_tokens), string.Empty));

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<LlmMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var token in _tokens)
            {
                await Task.Yield();
                yield return token;
            }
        }

        public Task<LlmResult> CompleteWithToolsAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            LlmToolExecutorAsync toolExecutor,
            Action<string, string>? onToolInvoked = null,
            Action<string>? onTokenChunk = null,
            CancellationToken ct = default)
        {
            if (onTokenChunk != null)
            {
                foreach (var token in _tokens) onTokenChunk(token);
            }
            return CompleteAsync(messages, ct);
        }

        public Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(new LlmResult(true, "OK", string.Empty));
    }
}
