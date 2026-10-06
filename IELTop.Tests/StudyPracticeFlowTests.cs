using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Learn;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The practice flow with a fake model, so the JSON it returns is fixed and the
/// test says exactly what the app does with each question kind: match, completion,
/// gap, single. No network, no key.
/// </summary>
[Collection("AppState")]
public sealed class StudyPracticeFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _lessonDir;
    private readonly LessonService _lessons = new();

    public StudyPracticeFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_flow_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();

        _lessonDir = Path.Combine(Path.GetTempPath(), $"ieltop_flow_lessons_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_lessonDir);
        LessonService.SetUserDirForTesting(_lessonDir);

        File.WriteAllText(Path.Combine(_lessonDir, "unit-1.json"), """
        {
          "unit": "Unit 1",
          "title": "Unit 1",
          "source": "flow test",
          "sections": [
            { "id": "v", "skill": "Vocabulary", "title": "Unit 1 Vocabulary", "isAnswerKey": false,
              "blocks": [ { "type": "text", "text": "arctic means very cold. drought means a long dry spell." } ] }
          ],
          "vocabulary": [], "slides": [], "audio": []
        }
        """, System.Text.Encoding.UTF8);
        _lessons.Reload();
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        LessonService.SetUserDirForTesting(null);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_lessonDir, recursive: true); } catch { /* best effort */ }
    }

    private const string CannedJson = """
    {
      "title": "Unit 1 set",
      "questions": [
        { "kind": "gap", "prompt": "The ___ was very cold.", "options": [], "rows": [], "correctKey": "", "gapAnswer": "arctic|the arctic", "explanation": "arctic means very cold" },
        { "kind": "single", "prompt": "What does drought mean?", "options": [{"key":"A","text":"a long dry spell"},{"key":"B","text":"heavy rain"}], "rows": [], "correctKey": "A", "gapAnswer": "", "explanation": "from the sheet" },
        { "kind": "match", "prompt": "Match each word with its meaning.", "options": [], "rows": [{"label":"arctic","answer":"very cold"},{"label":"drought","answer":"a long dry spell"}], "correctKey": "", "gapAnswer": "", "explanation": "both from the sheet" },
        { "kind": "completion", "prompt": "Note: arctic means ___, drought means ___.", "options": [], "rows": [], "correctKey": "", "gapAnswer": "very cold|little rain|a long dry spell", "explanation": "from the note" }
      ]
    }
    """;

    private async Task<(StudyService Study, int SetId)> BuildAsync()
    {
        var study = new StudyService(_lessons, new FakeLlm(CannedJson));
        var session = study.NewPracticeSession("unit-1", "Unit 1 vocabulary");
        var built = await study.BuildPracticeAsync(session.SelectedSessionId, "unit-1", "Vocabulary", "Standard", 4);
        Assert.NotNull(built.OpenSet);
        return (study, built.OpenSet!.Id);
    }

    [Fact]
    public async Task Match_questions_are_parsed_and_keep_their_rows()
    {
        var (study, setId) = await BuildAsync();
        var set = study.OpenSet(setId).OpenSet!;

        var match = set.Questions.Single(q => q.Kind == "match");
        Assert.Equal(2, match.MatchRows.Count);
        Assert.Contains(match.MatchRows, r => r.Label == "arctic" && r.Answer == "very cold");
        Assert.Contains(match.MatchRows, r => r.Label == "drought" && r.Answer == "a long dry spell");

        // The other kinds survived the same parse.
        Assert.Contains(set.Questions, q => q.Kind == "gap");
        Assert.Contains(set.Questions, q => q.Kind == "single");
        Assert.Contains(set.Questions, q => q.Kind == "completion");
    }

    [Fact]
    public async Task A_match_answer_scores_right_only_when_every_row_is_placed()
    {
        var (study, setId) = await BuildAsync();
        var set = study.OpenSet(setId).OpenSet!;
        var match = set.Questions.Single(q => q.Kind == "match");

        study.Answer(setId, match.Id, "arctic=very cold;drought=very cold", toggleFlag: false);
        Assert.False(study.OpenSet(setId).OpenSet!.Questions.Single(q => q.Id == match.Id).IsCorrect);

        study.Answer(setId, match.Id, "ARCTIC = Very Cold ; drought=a long dry spell", toggleFlag: false);
        Assert.True(study.OpenSet(setId).OpenSet!.Questions.Single(q => q.Id == match.Id).IsCorrect);
    }

    [Fact]
    public async Task Completion_and_gap_accept_any_listed_answer()
    {
        var (study, setId) = await BuildAsync();
        var set = study.OpenSet(setId).OpenSet!;

        var gap = set.Questions.Single(q => q.Kind == "gap");
        study.Answer(setId, gap.Id, "the arctic", toggleFlag: false);
        Assert.True(study.OpenSet(setId).OpenSet!.Questions.Single(q => q.Id == gap.Id).IsCorrect);

        var note = set.Questions.Single(q => q.Kind == "completion");
        study.Answer(setId, note.Id, "a long dry spell", toggleFlag: false);
        Assert.True(study.OpenSet(setId).OpenSet!.Questions.Single(q => q.Id == note.Id).IsCorrect);
    }

    [Fact]
    public async Task Submitting_the_set_scores_the_whole_thing()
    {
        var (study, setId) = await BuildAsync();
        var set = study.OpenSet(setId).OpenSet!;

        foreach (var q in set.Questions)
        {
            var answer = q.Kind switch
            {
                "gap" => "arctic",
                "single" => "A",
                "match" => "arctic=very cold;drought=a long dry spell",
                _ => "very cold",
            };
            study.Answer(setId, q.Id, answer, toggleFlag: false);
        }

        var submitted = study.SubmitSet(setId).OpenSet!;
        Assert.Equal("Submitted", submitted.Status);
        Assert.Equal(submitted.Questions.Count, submitted.Score);
    }

    [Fact]
    public async Task Pinned_sessions_sort_first_and_toggle_back()
    {
        var study = new StudyService(_lessons, new FakeLlm(CannedJson));
        var older = study.NewPracticeSession("unit-1", "older");
        var newer = study.NewPracticeSession("unit-1", "newer");

        // Newer is first by default.
        var before = study.PracticeSnapshot(newer.SelectedSessionId, 0).Sessions;
        Assert.Equal(newer.SelectedSessionId, before[0].Id);

        // Pin the older one; it should jump to the top.
        study.PinPracticeSession(older.SelectedSessionId);
        var after = study.PracticeSnapshot(newer.SelectedSessionId, 0).Sessions;
        Assert.Equal(older.SelectedSessionId, after[0].Id);
        Assert.True(after[0].Pinned);

        study.PinPracticeSession(older.SelectedSessionId);
        var unpinned = study.PracticeSnapshot(newer.SelectedSessionId, 0).Sessions;
        Assert.False(unpinned.Single(s => s.Id == older.SelectedSessionId).Pinned);
    }

    [Fact]
    public void Chat_search_with_no_match_does_not_create_a_session()
    {
        var study = new StudyService(_lessons, new FakeLlm(CannedJson));
        study.NewChatSession("");
        var before = study.ChatSnapshot(0, "", "").Sessions.Count;

        var filtered = study.ChatSnapshot(0, "", "zzz-no-such-session");

        Assert.Empty(filtered.Sessions);
        Assert.Equal(0, filtered.SelectedSessionId);
        // The search must not have quietly created a stray session.
        Assert.Equal(before, study.ChatSnapshot(0, "", "").Sessions.Count);
    }

    [Fact]
    public void Practice_session_list_has_no_chat_sessions()
    {
        var study = new StudyService(_lessons, new FakeLlm(CannedJson));
        study.NewChatSession("unit-1");
        var practice = study.NewPracticeSession("unit-1", "mixed");

        var snap = study.PracticeSnapshot(practice.SelectedSessionId, 0, "All", "");

        Assert.All(snap.Sessions, s => Assert.Equal("Practice", s.Kind));
        Assert.Contains(snap.Sessions, s => s.Id == practice.SelectedSessionId);
    }

    [Fact]
    public void Setting_the_chat_unit_persists_it()
    {
        var study = new StudyService(_lessons, new FakeLlm(CannedJson));
        var chat = study.NewChatSession("");

        var updated = study.SetChatUnit(chat.SelectedSessionId, "unit-1");

        Assert.Equal("unit-1", updated.SelectedUnit);
        Assert.Equal("unit-1", study.ChatSnapshot(chat.SelectedSessionId, "", "").SelectedUnit);
    }
}

/// <summary>A model stub that returns one fixed reply, so the flow is deterministic.</summary>
internal sealed class FakeLlm : ILlmService
{
    private readonly string _reply;

    public FakeLlm(string reply) => _reply = reply;

    public bool IsConfigured => true;
    public bool UseStreaming => false;
    public bool VisionEnabled => false;

    public Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => Task.FromResult(new LlmResult(true, _reply, string.Empty));

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<LlmResult> CompleteWithToolsAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        LlmToolExecutorAsync toolExecutor,
        Action<string, string>? onToolInvoked = null,
        Action<string>? onTokenChunk = null,
        CancellationToken ct = default)
    {
        if (onTokenChunk != null) onTokenChunk(_reply);
        return Task.FromResult(new LlmResult(true, _reply, string.Empty));
    }

    public Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new LlmResult(true, "OK", string.Empty));
}
