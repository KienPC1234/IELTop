using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    public StudyBridge(StudyService study)
    {
        _study = study;
    }

    public void Register(BridgeRouter router)
    {
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
                Str(a, "difficulty"), Int(a, "count"), Str(a, "scope"), ct)));
        router.Register("study.practice.openSet", Act(a =>
            _study.OpenSet(Int(a, "setId"), Str(a, "scope"))));
        router.Register("study.practice.answer", Act(a =>
            _study.Answer(Int(a, "setId"), Int(a, "questionId"), Str(a, "answer"), Bool(a, "flag"), Str(a, "scope"))));
        router.Register("study.practice.submit", Act(a => _study.SubmitSet(Int(a, "setId"), Str(a, "scope"))));
        router.Register("study.practice.explain", ActAsync(async (a, ct) =>
            await _study.ExplainAsync(Int(a, "questionId"), ct)));

        // ---- Vocabulary ----
        router.Register("study.vocab.snapshot", Act(a =>
            _study.VocabSnapshot(Str(a, "query"), Str(a, "unit"))));
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
