using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.App;
using IELTop.Services.Learn;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The speaking tutor with no models installed: the bank is always there, the
/// snapshot says honestly what is missing, scoring refuses without its model
/// instead of half saving, and sessions work like the other Study tabs. No
/// network, no key, no model files.
/// </summary>
[Collection("AppState")]
public sealed class SpeakingTutorTests : IDisposable
{
    private readonly string _dbPath;

    public SpeakingTutorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_tutor_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static SpeakingTutorService Offline() =>
        new(new LlmOff(), new SttOff(), new MddOff());

    [Fact]
    public void Bank_has_cues_for_every_part()
    {
        Assert.Equal(8, SpeakingBank.Part1.Count);
        Assert.All(SpeakingBank.Part1, t => Assert.True(t.Questions.Count >= 4, t.Topic));
        Assert.Equal(6, SpeakingBank.Part2.Count);
        Assert.All(SpeakingBank.Part2, c => Assert.True(c.Bullets.Count >= 3, c.Title));
        Assert.Equal(6, SpeakingBank.Part3.Count);
        Assert.All(SpeakingBank.Part3, s => Assert.Equal(3, s.Questions.Count));
        Assert.True(SpeakingBank.ReadAloud.Count >= 10);
        Assert.All(
            SpeakingBank.Part1.SelectMany(t => t.Questions)
                .Concat(SpeakingBank.Part2.Select(c => c.Prompt))
                .Concat(SpeakingBank.Part3.SelectMany(s => s.Questions))
                .Concat(SpeakingBank.ReadAloud),
            q => Assert.False(string.IsNullOrWhiteSpace(q)));
    }

    [Fact]
    public void Bank_caps_match_real_test_timing()
    {
        Assert.Equal(60, SpeakingBank.MaxSeconds("Part1"));
        Assert.Equal(60, SpeakingBank.MaxSeconds("Part2Prep"));
        Assert.Equal(120, SpeakingBank.MaxSeconds("Part2"));
        Assert.Equal(90, SpeakingBank.MaxSeconds("Part3"));
    }

    [Fact]
    public void Snapshot_offline_names_what_is_missing()
    {
        var snap = Offline().Snapshot();

        Assert.False(snap.SttReady);
        Assert.False(snap.MddReady);
        Assert.False(snap.CanUseAi);
        Assert.NotEmpty(snap.SttHint);
        Assert.NotEmpty(snap.MddHint);
        Assert.NotEmpty(snap.AiHint);
        Assert.Equal(new[] { "Part1", "Part2", "Part3", "ReadAloud" }, snap.Parts);
        Assert.NotEmpty(snap.Cues);
    }

    [Fact]
    public void Cues_cover_every_part()
    {
        Assert.Equal(
            SpeakingBank.Part1.Sum(t => t.Questions.Count),
            SpeakingTutorService.CuesFor("Part1").Count);
        Assert.Equal(SpeakingBank.Part2.Count, SpeakingTutorService.CuesFor("Part2").Count);
        Assert.Equal(
            SpeakingBank.Part3.Sum(s => s.Questions.Count),
            SpeakingTutorService.CuesFor("Part3").Count);
        Assert.Equal(SpeakingBank.ReadAloud.Count, SpeakingTutorService.CuesFor("ReadAloud").Count);
        Assert.Equal(
            SpeakingTutorService.CuesFor("Part1").Count,
            SpeakingTutorService.CuesFor("nope").Count);
    }

    [Fact]
    public async Task Answer_without_a_speech_model_is_refused_and_saves_nothing()
    {
        var tutor = Offline();
        var created = tutor.NewSession("Part1");

        var snap = await tutor.SubmitAnswerAsync(created.SelectedSessionId, "Part1", "Home: Do you cook?", "AAAA", CancellationToken.None);

        Assert.NotEmpty(snap.StatusMessage);
        var attempts = await AppDbContext.TableAsync<TutorSpeakingAttempt>().ToListAsync();
        Assert.Empty(attempts);
    }

    [Fact]
    public async Task ReadAloud_without_a_pronunciation_model_is_refused_and_saves_nothing()
    {
        var tutor = Offline();
        var created = tutor.NewSession("ReadAloud");

        var (snap, words) = await tutor.SubmitReadAloudAsync(
            created.SelectedSessionId, "The weather is nice today.", "AAAA", CancellationToken.None);

        Assert.NotEmpty(snap.StatusMessage);
        Assert.Empty(words);
        var attempts = await AppDbContext.TableAsync<TutorSpeakingAttempt>().ToListAsync();
        Assert.Empty(attempts);
    }

    [Fact]
    public void Sessions_pin_rename_and_delete()
    {
        var tutor = Offline();
        var created = tutor.NewSession("Part2");
        Assert.True(created.SelectedSessionId > 0);

        var renamed = tutor.RenameSession(created.SelectedSessionId, "My part 2");
        Assert.Contains(renamed.Sessions, s => s.Title == "My part 2");

        var pinned = tutor.PinSession(created.SelectedSessionId);
        Assert.True(pinned.Sessions.First(s => s.Id == created.SelectedSessionId).Pinned);

        var afterDelete = tutor.DeleteSession(created.SelectedSessionId);
        Assert.DoesNotContain(afterDelete.Sessions, s => s.Id == created.SelectedSessionId);
    }

    [Fact]
    public async Task OpenAttempt_selects_it_from_history()
    {
        var tutor = Offline();
        var created = tutor.NewSession("Part1");
        var attempt = new TutorSpeakingAttempt
        {
            SessionId = created.SelectedSessionId,
            Mode = "Answer",
            Part = "Part1",
            Cue = "Food: Can you cook?",
            Transcript = "Yes I can cook rice.",
            WordsPerMinute = 110,
            SpeechSeconds = 5,
            PauseCount = 1,
        };
        await AppDbContext.InsertAsync(attempt);

        var (snap, words) = await tutor.OpenAttemptAsync(
            attempt.Id, created.SelectedSessionId, "Part1", CancellationToken.None);

        Assert.Equal(attempt.Id, snap.OpenAttempt?.Id);
        Assert.Empty(words);
    }

    [Fact]
    public async Task Feedback_without_a_model_returns_the_hint()
    {
        var tutor = Offline();
        var created = tutor.NewSession("Part1");

        var snap = await tutor.FeedbackAsync(999, created.SelectedSessionId, "Part1", CancellationToken.None);

        Assert.NotEmpty(snap.StatusMessage);
    }

    [Fact]
    public async Task Feedback_with_a_model_saves_the_reply()
    {
        var tutor = new SpeakingTutorService(new FakeLlm("Fluency 6.0. Speak more."), new SttOff(), new MddOff());
        var created = tutor.NewSession("Part1");
        var attempt = new TutorSpeakingAttempt
        {
            SessionId = created.SelectedSessionId,
            Mode = "Answer",
            Part = "Part1",
            Cue = "Food: Can you cook?",
            Transcript = "Yes I can cook rice.",
            WordsPerMinute = 110,
            SpeechSeconds = 5,
            PauseCount = 1,
        };
        await AppDbContext.InsertAsync(attempt);

        var snap = await tutor.FeedbackAsync(attempt.Id, created.SelectedSessionId, "Part1", CancellationToken.None);

        Assert.Equal("Fluency 6.0. Speak more.", snap.OpenAttempt?.AiFeedback);
    }

    [Fact]
    public void Feedback_prompt_carries_the_measurements()
    {
        var prompt = StudyPrompts.BuildSpeakingFeedback(
            "Answer", "Part1", "Food: Can you cook?", "Yes I can cook rice.",
            110, 5.2, 1, 0.4, 0.85, 0, 0);

        Assert.Contains("Yes I can cook rice.", prompt);
        Assert.Contains("110", prompt);
        Assert.Contains("0.85", prompt);
    }
}

/// <summary>Language model that is not configured, so AI paths stay disabled.</summary>
internal sealed class LlmOff : ILlmService
{
    public bool IsConfigured => false;
    public bool UseStreaming => false;
    public bool VisionEnabled => false;

    public Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        => Task.FromResult(LlmResult.Fail("No language model."));

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
        => Task.FromResult(LlmResult.Fail("No language model."));

    public Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(LlmResult.Fail("No language model."));
}

internal sealed class SttOff : ISttService
{
    public bool IsAvailable() => false;

    public Task<SttResult> TranscribeAsync(string wavPath, CancellationToken ct = default)
        => Task.FromResult(SttResult.Fail("No transcription model."));
}

internal sealed class MddOff : IMddPhonemeService
{
    public bool IsModelAvailable() => false;

    public Task<MddResult> AssessAsync(string wavPath, string targetText, CancellationToken ct = default)
        => Task.FromResult(MddResult.Fail("No pronunciation model.", targetText));

    public Task<double> ClarityAsync(string wavPath, CancellationToken ct = default)
        => Task.FromResult(0.0);
}
