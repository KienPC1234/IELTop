using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Services.App;
using IELTop.Services.Learn;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// Exposes the Study services to the web UI: the tutor chat, the practice
/// builder, and the vocabulary browser. Read actions return the fresh snapshot
/// for their tab. The two that call a language model are async and take a
/// CancellationToken, so a long build can be cancelled and never blocks the
/// window.
/// </summary>
public sealed class StudyBridge
{
    private readonly StudyService _study;
    private readonly SpeakingTutorService _tutor;

    public StudyBridge(StudyService study, SpeakingTutorService tutor)
    {
        _study = study;
        _tutor = tutor;
    }

    public void Register(BridgeRouter router)
    {
        _study.SetBroadcaster((evt, payload) => router.Broadcast(evt, payload));

        // ---- Chat ----
        router.Register("study.chat.snapshot", Act(a =>
            _study.ChatSnapshot(Int(a, "sessionId"), Str(a, "unit"), Str(a, "query"))));
        router.Register("study.chat.newSession", Act(a => _study.NewChatSession(Str(a, "unit"))));
        router.Register("study.chat.selectSession", Act(a =>
            _study.ChatSnapshot(Int(a, "sessionId"), Str(a, "unit"))));
        router.Register("study.chat.setUnit", Act(a =>
            _study.SetChatUnit(Int(a, "sessionId"), Str(a, "unit"))));
        router.Register("study.chat.renameSession", Act(a =>
            _study.RenameSession(Int(a, "sessionId"), Str(a, "title"))));
        router.Register("study.chat.pinSession", Act(a =>
            _study.PinSession(Int(a, "sessionId"))));
        router.Register("study.chat.deleteSession", Act(a =>
        {
            _study.DeleteSession(Int(a, "sessionId"));
            return _study.ChatSnapshot(0, Str(a, "unit"));
        }));
        router.Register("study.chat.ask", ActAsync(async (a, ct) =>
            await _study.AskAsync(Int(a, "sessionId"), Str(a, "question"), Str(a, "unit"), ct)));
        router.Register("study.chat.runTool", ActAsync(async (a, ct) =>
            await _study.RunToolAsync(Int(a, "sessionId"), Str(a, "tool"), Str(a, "input"), Str(a, "unit"), ct)));
        router.Register("study.tutor.curriculum", Act(_ => _study.CurriculumOverview()));
        router.Register("study.tutor.teachTopic", ActAsync(async (a, ct) =>
            await _study.TeachTopicAsync(Int(a, "sessionId"), Str(a, "unit"), Str(a, "topic"), ct)));
        router.Register("study.tutor.generateExercise", ActAsync(async (a, ct) =>
            await _study.GenerateInteractiveExerciseAsync(Int(a, "sessionId"), Str(a, "skill"), Str(a, "topic"), Str(a, "kind"), Str(a, "unit"), ct)));
        router.Register("study.tutor.checkAnswer", Act(a =>
            _study.CheckExerciseAnswer(Str(a, "questionJson"), Str(a, "userAnswer"))));

        // ---- Practice ----
        router.Register("study.practice.snapshot", Act(a =>
            _study.PracticeSnapshot(Int(a, "sessionId"), Int(a, "setId"), Str(a, "scope"), Str(a, "query"))));
        router.Register("study.practice.newSession", Act(a =>
            _study.NewPracticeSession(Str(a, "unit"), Str(a, "topic"), Str(a, "scope"))));
        router.Register("study.practice.deleteSession", Act(a =>
            _study.DeletePracticeSession(Int(a, "sessionId"), Str(a, "scope"))));
        router.Register("study.practice.renameSession", Act(a =>
            _study.RenamePracticeSession(Int(a, "sessionId"), Str(a, "title"), Str(a, "scope"))));
        router.Register("study.practice.pinSession", Act(a =>
            _study.PinPracticeSession(Int(a, "sessionId"), Str(a, "scope"))));
        router.Register("study.practice.build", ActAsync(async (a, ct) =>
            await _study.BuildPracticeAsync(
                Int(a, "sessionId"), Str(a, "unit"), Str(a, "skill"),
                Str(a, "difficulty"), Int(a, "count"), Str(a, "topic"), Str(a, "scope"), Str(a, "mode"), ct)));
        router.Register("study.practice.openSet", Act(a =>
            _study.OpenSet(Int(a, "setId"), Str(a, "scope"))));
        router.Register("study.practice.answer", Act(a =>
            _study.Answer(Int(a, "setId"), Int(a, "questionId"), Str(a, "answer"), Bool(a, "flag"), Str(a, "scope"))));
        router.Register("study.practice.submit", Act(a => _study.SubmitSet(Int(a, "setId"), Str(a, "scope"))));
        router.Register("study.practice.explain", ActAsync(async (a, ct) =>
            await _study.ExplainAsync(Int(a, "questionId"), ct)));

        // ---- Materials / Lessons ----
        router.Register("study.materials.snapshot", Act(a =>
            _study.MaterialSnapshot(Str(a, "unit"), Str(a, "sectionId"), Str(a, "topic"), Str(a, "skill"))));

        // ---- Vocabulary ----
        router.Register("study.vocab.snapshot", Act(a =>
            _study.VocabSnapshot(Str(a, "query"), Str(a, "unit"))));

        // ---- Speaking tutor ----
        router.Register("study.speaking.snapshot", Act(a =>
            _tutor.Snapshot(Int(a, "sessionId"), Str(a, "part"), Str(a, "query"))));
        router.Register("study.speaking.newSession", Act(a =>
            _tutor.NewSession(Str(a, "part"))));
        router.Register("study.speaking.renameSession", Act(a =>
            _tutor.RenameSession(Int(a, "sessionId"), Str(a, "title"))));
        router.Register("study.speaking.pinSession", Act(a =>
            _tutor.PinSession(Int(a, "sessionId"))));
        router.Register("study.speaking.deleteSession", Act(a =>
            _tutor.DeleteSession(Int(a, "sessionId"))));
        router.Register("study.speaking.suggestTopic", Act(a =>
            _tutor.SuggestTopic(Str(a, "part"))));
        router.Register("study.speaking.generateDrill", ActAsync(async (a, ct) =>
            await _tutor.GenerateSegmentDrillAsync(Str(a, "sentence"), Str(a, "reason"), Str(a, "drillType"), ct)));
        router.Register("study.speaking.submitAnswer", ActAsync(async (a, ct) =>
            await _tutor.SubmitAnswerAsync(
                Int(a, "sessionId"), Str(a, "part"), Str(a, "cue"), Str(a, "audioBase64"), ct)));
        router.Register("study.speaking.submitReadAloud", ActAsync(async (a, ct) =>
        {
            var (snapshot, words) = await _tutor.SubmitReadAloudAsync(
                Int(a, "sessionId"), Str(a, "line"), Str(a, "audioBase64"), ct);
            return new { snapshot, words };
        }));
        router.Register("study.speaking.feedback", ActAsync(async (a, ct) =>
            await _tutor.FeedbackAsync(Int(a, "attemptId"), Int(a, "sessionId"), Str(a, "part"), ct)));
        router.Register("study.speaking.deleteAttempt", Act(a =>
            _tutor.DeleteAttempt(Int(a, "attemptId"), Int(a, "sessionId"), Str(a, "part"))));
        router.Register("study.speaking.openAttempt", ActAsync(async (a, ct) =>
        {
            var (snapshot, words) = await _tutor.OpenAttemptAsync(
                Int(a, "attemptId"), Int(a, "sessionId"), Str(a, "part"), ct);
            return new { snapshot, words };
        }));
    }

    private static Func<JsonElement?, CancellationToken, Task<object?>> Act(Func<JsonElement?, object?> handler)
        => (args, _) => Task.FromResult(handler(args));

    private static Func<JsonElement?, CancellationToken, Task<object?>> ActAsync(
        Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => handler;

    private static string Str(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;

    private static int Int(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool Bool(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            && v.GetBoolean();
}
