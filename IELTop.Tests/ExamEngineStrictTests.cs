using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.App;
using IELTop.Services.Exam;
using IELTop.Services.Storage;
using Xunit;

namespace IELTop.Tests;

/// <summary>A fake host that records what the engine asked the window to do.</summary>
public sealed class FakeSession : IExamSessionController
{
    public bool SupportsFullscreen { get; set; } = true;
    public int EnterFullscreenCount { get; private set; }
    public int ExitFullscreenCount { get; private set; }
    public int FocusCount { get; private set; }
    public bool AlwaysOnTop { get; private set; }
    public event Action<ExamFocusEvent>? FocusChanged;

    public void Raise(ExamFocusEvent kind) => FocusChanged?.Invoke(kind);

    public void EnterFullscreen() => EnterFullscreenCount++;
    public void ExitFullscreen() => ExitFullscreenCount++;
    public void Focus() => FocusCount++;
    public void SetAlwaysOnTop(bool on) => AlwaysOnTop = on;
}

/// <summary>A repository with one two-part paper so a run can start.</summary>
public sealed class FakeRepository : IExamRepository
{
    private readonly ExamPaper _paper;
    public FakeRepository(ExamPaper paper) => _paper = paper;

    public IReadOnlyList<ExamPaper> LoadPapers() => new[] { _paper };
    public string ExamsDir => ".";
    public string AudioDir => ".";
    public bool DeleteUserPaper(string title) => false;
    public bool IsUserPaper(string title) => false;
    public ImportResult ImportPaper(string json) => ImportResult.Ok("x");
    public ImportResult SavePaper(ExamPaper paper) => ImportResult.Ok(paper.Title);
    public ImportResult UpdatePaper(string originalTitle, ExamPaper paper) => ImportResult.Ok(paper.Title);
    public ExamPaper? GetPaper(string title) => _paper;
    public ExportResult ExportPapers(IEnumerable<string> titles, string folder)
        => new(true, 0, 0, folder, string.Empty);
}

internal sealed class NoopStt : ISttService
{
    public bool IsAvailable() => false;
    public Task<SttResult> TranscribeAsync(string wavPath, CancellationToken ct = default)
        => Task.FromResult(new SttResult(false, string.Empty, "none"));
}

internal sealed class NoopGec : IGecService
{
    public bool IsAvailable() => false;
    public Task<GecResult> CheckAsync(string text, CancellationToken ct = default)
        => Task.FromResult(GecResult.Fail("none"));
}

internal sealed class NoopModels : IModelLoadCoordinator
{
    public IReadOnlyList<string> WritingSlots => Array.Empty<string>();
    public IReadOnlyList<string> SpeakingSlots => Array.Empty<string>();
    public bool KeepReady => false;
    public Task<ModelPrepResult> PrepareAsync(IEnumerable<string> slots, CancellationToken ct = default)
        => Task.FromResult(new ModelPrepResult(false, false, string.Empty));
    public void ReleaseAfterUse(IEnumerable<string> slots) { }
    public string DescribeMode() => string.Empty;
}

internal sealed class NoopPronunciation : IPronunciationService
{
    public bool IsAvailable => false;
    public Task<PronunciationResult> AssessAsync(string wavPath, string targetText, CancellationToken ct = default)
        => Task.FromResult(new PronunciationResult { Success = false, Error = "none" });
}

internal sealed class NoopAi : IIeltsAiService
{
    public bool IsAvailable => false;
    public bool VisionAvailable => false;
    public Task<WritingFeedbackResult> ReviewWritingAsync(string a, string b, int c, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WritingFeedbackResult> ReviewWritingAsync(string a, string b, int c, MarkingStrictness d, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SpeakingFeedbackResult> AssessSpeakingAsync(string a, string b, MarkingStrictness c, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SpeakingFeedbackResult> AssessSpeakingAsync(string a, string b, string c, int d, MarkingStrictness e, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LlmResult> ExplainReadingAsync(string a, string b, string c, string d, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LlmResult> ExplainListeningAsync(string a, string b, string c, string d, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LlmResult> SuggestTopicAsync(string skill, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TestPickResult> PickTestAsync(string catalog, string history, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PaperReviewResult> ReviewPaperAsync(ExamPaper paper, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PaperDraftResult> DraftPaperAsync(string rawText, string hint, CancellationToken ct = default) => throw new NotImplementedException();
    public IAsyncEnumerable<string> CoachSpeakingAsync(string target, string heardPhonemes, string mistakes, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LlmResult> ReadImageAsync(string prompt, string base64Image, string mediaType, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LlmResult> ReadImportImageAsync(ImportedImage image, string skill, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<LlmResult> TestAsync(CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>The engine drives strict mode through the host port, never a window.</summary>
public sealed class ExamEngineStrictTests
{
    private static ExamPaper Paper()
    {
        var reading = new ExamPart
        {
            Id = "R1",
            Skill = "Reading",
            Title = "Reading Part 1",
            Instructions = "Read and answer.",
            Minutes = 20,
            Questions = new List<ExamQuestion>
            {
                new() { Number = 1, Kind = "gap", Prompt = "The answer is ___.", GapAnswer = "x" },
            },
        };
        return new ExamPaper
        {
            Title = "Test",
            Source = "test",
            Category = "Academic",
            Parts = new List<ExamPart> { reading },
        };
    }

    private static (ExamEngine Engine, FakeSession Session) Build()
    {
        var session = new FakeSession();
        var engine = new ExamEngine(
            new FakeRepository(Paper()), new NoopAi(), new NoopStt(), new NoopGec(),
            new NoopModels(), new NoopPronunciation(), session);
        return (engine, session);
    }

    [Fact]
    public async Task Enforce_enters_fullscreen_on_start()
    {
        var (engine, session) = Build();
        engine.SetStrictLevel(StrictLevel.Enforce);
        await engine.StartExamAsync(CancellationToken.None);
        Assert.True(session.AlwaysOnTop);
        Assert.True(session.EnterFullscreenCount >= 1);
    }

    [Fact]
    public async Task Off_does_not_touch_fullscreen()
    {
        var (engine, session) = Build();
        engine.SetStrictLevel(StrictLevel.Off);
        await engine.StartExamAsync(CancellationToken.None);
        Assert.False(session.AlwaysOnTop);
        Assert.Equal(0, session.EnterFullscreenCount);
    }

    [Fact]
    public async Task A_quick_alt_tab_is_not_counted()
    {
        var (engine, session) = Build();
        engine.SetStrictLevel(StrictLevel.Enforce);
        await engine.StartExamAsync(CancellationToken.None);
        engine.StartPart();

        session.Raise(ExamFocusEvent.Lost);
        session.Raise(ExamFocusEvent.Regained);
        Assert.Equal(0, engine.Run.StrictViolations);
    }

    [Fact]
    public async Task Minimize_counts_one_violation()
    {
        var (engine, session) = Build();
        engine.SetStrictLevel(StrictLevel.Enforce);
        await engine.StartExamAsync(CancellationToken.None);
        engine.StartPart();

        session.Raise(ExamFocusEvent.Minimized);
        Assert.Equal(1, engine.Run.StrictViolations);
    }

    [Fact]
    public async Task Warn_does_not_count_a_minimize()
    {
        var (engine, session) = Build();
        engine.SetStrictLevel(StrictLevel.Warn);
        await engine.StartExamAsync(CancellationToken.None);
        engine.StartPart();

        session.Raise(ExamFocusEvent.Minimized);
        Assert.Equal(0, engine.Run.StrictViolations);
    }

    [Fact]
    public async Task Cancel_lifts_fullscreen_and_always_on_top()
    {
        var (engine, session) = Build();
        engine.SetStrictLevel(StrictLevel.Enforce);
        await engine.StartExamAsync(CancellationToken.None);
        session.Raise(ExamFocusEvent.Lost);
        engine.CancelRunningTest();

        Assert.False(session.AlwaysOnTop);
        Assert.True(session.ExitFullscreenCount >= 1);
        Assert.Equal(0, engine.Run.StrictViolations);
    }

    [Fact]
    public async Task Setup_reports_host_support()
    {
        var (engine, _) = Build();
        engine.Load();
        Assert.True(engine.Setup.HostSupportsStrict);
        Assert.Equal("Off", engine.Setup.StrictLevel);
    }

    [Fact]
    public void Null_session_is_a_safe_default()
    {
        var engine = new ExamEngine(
            new FakeRepository(Paper()), new NoopAi(), new NoopStt(), new NoopGec(),
            new NoopModels(), new NoopPronunciation());
        engine.Load();
        Assert.False(engine.Setup.HostSupportsStrict);
        engine.SetStrictMode(true);
        Assert.True(engine.StrictMode);
    }
}
