using System.Text.Json;
using System.Text.Json.Serialization;
using IELTop.Desktop.Web;
using IELTop.Services.Exam;

namespace IELTop.Desktop.Bridge;

/// <summary>
/// Exposes the exam engine to the web UI. Every action returns the fresh
/// snapshot, and the engine also pushes one whenever state changes, so the
/// clock and the auto advance show up without the page polling.
/// </summary>
public sealed class ExamBridge
{
    private static readonly JsonSerializerOptions Json = BridgeJson.Options;

    private readonly ExamEngine _engine;
    private readonly StaticFileServer _server;
    private readonly Action<string> _push;

    public ExamBridge(ExamEngine engine, StaticFileServer server, Action<string> push)
    {
        _engine = engine;
        _server = server;
        _push = push;
        _engine.StateChanged += OnStateChanged;
    }

    private void OnStateChanged()
    {
        // The engine may raise this from the timer thread. Serialize here and
        // let the sink marshal to the window thread.
        try
        {
            var run = _engine.Run;

            // A Listening clip is asked for once. Turn the file path into a
            // served URL, push a play event, and clear it so it plays once.
            if (!string.IsNullOrEmpty(run.PendingAudioUrl))
            {
                var url = _server.UrlForFile(run.PendingAudioUrl);
                run.AudioUrl = url ?? string.Empty;
                _push(JsonSerializer.Serialize(new { @event = "playAudio", url }, Json));
                run.PendingAudioUrl = string.Empty;
            }

            _push(JsonSerializer.Serialize(new { @event = "exam", exam = Snapshot() }, Json));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[exam] push failed: {ex.Message}");
        }
    }

    private object Snapshot()
    {
        var run = _engine.Run;
        FillMediaUrls(run);
        return new { setup = _engine.Setup, run };
    }

    /// <summary>Points the chart image and any clip at the loopback server.</summary>
    private void FillMediaUrls(ExamRun run)
    {
        foreach (var part in run.Parts)
        {
            part.ImageUrl = string.Empty;
            if (string.IsNullOrWhiteSpace(part.ImageFileName)) continue;
            foreach (var candidate in ImageCandidates(part.ImageFileName))
            {
                if (!File.Exists(candidate)) continue;
                var url = _server.UrlForFile(candidate);
                if (url is not null) { part.ImageUrl = url; break; }
            }
        }
    }

    private static IEnumerable<string> ImageCandidates(string name)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "Images", name);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Images", name);
    }

    /// <summary>Registers every exam method on the router.</summary>
    public void Register(BridgeRouter router)
    {
        router.Register("exam.snapshot", Act(_ => { }));

        // Setup
        router.Register("exam.selectPaper", Act(a => _engine.SelectPaper(Str(a, "title"))));
        router.Register("exam.toggleSkill", Act(a => _engine.ToggleSkill(Str(a, "name"))));
        router.Register("exam.toggleTaskType", Act(a => _engine.ToggleTaskType(Str(a, "name"))));
        router.Register("exam.setMixAllPapers", Act(a => _engine.SetMixAllPapers(Bool(a, "value"))));
        router.Register("exam.setShuffleParts", Act(a => _engine.SetShuffleParts(Bool(a, "value"))));
        router.Register("exam.setStrictMode", Act(a => _engine.SetStrictMode(Bool(a, "value"))));
        router.Register("exam.setBuildMode", Act(a => _engine.SetBuildMode(Str(a, "value"))));
        router.Register("exam.setStrictness", Act(a => _engine.SetStrictness(Str(a, "value"))));
        router.Register("exam.setVolume", Act(a => _engine.SetVolume(Dbl(a, "value"))));

        // Run control
        router.Register("exam.start", ActAsync((_, ct) => _engine.StartExamAsync(ct)));
        router.Register("exam.startPart", Act(_ => _engine.StartPart()));
        router.Register("exam.skipPrep", Act(_ => _engine.SkipPrep()));
        router.Register("exam.nextPart", Act(_ => _engine.GoNextPart()));
        router.Register("exam.selectPart", Act(a => _engine.SelectPart(Int(a, "index"))));
        router.Register("exam.goToQuestion", Act(a => _engine.GoToQuestion(Int(a, "index"))));
        router.Register("exam.moveQuestion", Act(a => _engine.MoveQuestion(Int(a, "delta"))));

        // In-test tools: clock, text size, contrast, notes, highlights, strict.
        router.Register("exam.toggleTimer", Act(_ => _engine.ToggleTimer()));
        router.Register("exam.biggerText", Act(_ => _engine.ChangeFontScale(0.1)));
        router.Register("exam.smallerText", Act(_ => _engine.ChangeFontScale(-0.1)));
        router.Register("exam.toggleContrast", Act(_ => _engine.ToggleContrast()));
        router.Register("exam.openNotes", Act(a => _engine.OpenNotes(Str(a, "selectedText"))));
        router.Register("exam.setNotes", Act(a => _engine.SetNotes(Str(a, "text"))));
        router.Register("exam.closeNotes", Act(_ => _engine.CloseNotes()));
        router.Register("exam.addHighlight", Act(a => _engine.AddHighlight(Str(a, "text"))));
        router.Register("exam.clearHighlights", Act(_ => _engine.ClearHighlights()));
        router.Register("exam.toggleStrict", Act(_ => _engine.ToggleStrictMode()));

        // Answers
        router.Register("exam.setChoice", Act(a => _engine.SetChoice(Int(a, "questionIndex"), Str(a, "key"))));
        router.Register("exam.setGap", Act(a => _engine.SetGapAnswer(Int(a, "questionIndex"), Str(a, "text"))));
        router.Register("exam.setMatch", Act(a => _engine.SetMatchRow(
            Int(a, "questionIndex"), Int(a, "rowIndex"), Str(a, "value"))));
        router.Register("exam.toggleFlag", Act(a => _engine.ToggleFlag(Int(a, "questionIndex"))));
        router.Register("exam.setEssay", Act(a => _engine.SetEssay(Str(a, "text"))));
        router.Register("exam.setTranscript", Act(a => _engine.SetTranscript(Str(a, "text"))));

        // Submit, review, reset
        router.Register("exam.toggleReview", Act(_ => _engine.ToggleReview()));
        router.Register("exam.submit", ActAsync((_, _) => _engine.SubmitAsync(true)));
        router.Register("exam.backToSetup", Act(_ => _engine.BackToSetup()));
        router.Register("exam.cancel", Act(_ => _engine.CancelRunningTest()));

        // Speaking: the page records, the host stores the wav, the engine transcribes.
        router.Register("exam.beginRecording", Act(_ =>
        {
            _engine.BeginSpeakingRecording();
            var seconds = _engine.Run.CurrentPart?.SpeakingSeconds ?? 60;
            _push(JsonSerializer.Serialize(new { @event = "record", seconds }, Json));
        }));
        router.Register("exam.recordingDone", ActAsync(async (a, ct) =>
        {
            var path = await SaveRecordingAsync(Str(a, "base64"));
            await _engine.CompleteSpeakingRecordingAsync(path, ct);
        }));
        router.Register("exam.recordingFailed", Act(a =>
        {
            _ = _engine.CompleteSpeakingRecordingAsync(string.Empty, CancellationToken.None);
        }));
        router.Register("exam.finishSpeaking", Act(_ => _engine.FinishSpeakingPart()));
        router.Register("exam.checkPronunciation", ActAsync(async (_, ct) =>
        {
            await _engine.CheckPronunciationAsync(ct);
        }));

        // Listening clip finished playing in the page.
        router.Register("exam.audioFinished", Act(a => _engine.AudioFinished(Bool(a, "ok"))));

        // AI marking
        router.Register("exam.aiMark", ActAsync((_, ct) => _engine.BeginAiMarkingAsync(ct)));
        router.Register("exam.stopAiMark", Act(_ => _engine.CancelGrading()));
        // The page copies this to the clipboard; the host only builds the text.
        router.Register("exam.feedbackText", Act(_ => _engine.FeedbackText()));
    }

    private static async Task<string> SaveRecordingAsync(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return string.Empty;
        int comma = base64.IndexOf(',');
        if (comma >= 0) base64 = base64[(comma + 1)..];

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "recordings");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"speaking_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(base64));
        return path;
    }

    // Handlers return the fresh snapshot, so a call both acts and refreshes.
    private Func<JsonElement?, CancellationToken, Task<object?>> Act(Action<JsonElement?> action)
        => (args, _) => { action(args); return Task.FromResult<object?>(Snapshot()); };

    private Func<JsonElement?, CancellationToken, Task<object?>> ActAsync(
        Func<JsonElement?, CancellationToken, Task> action)
        => async (args, ct) => { await action(args, ct); return Snapshot(); };

    private static string Str(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;

    private static int Int(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool Bool(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            && v.GetBoolean();

    private static double Dbl(JsonElement? a, string n)
        => a is { } e && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
