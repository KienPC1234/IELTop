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
    private readonly object _gate = new();

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
            // Take the pending clip through the engine so the take and the
            // clear cannot race a bridge call that reads the same field.
            var pending = _engine.ConsumePendingAudioUrl();
            if (!string.IsNullOrEmpty(pending))
            {
                var url = _server.UrlForFile(pending);
                var run = _engine.Run;
                run.AudioUrl = url ?? string.Empty;
                _push(JsonSerializer.Serialize(new { @event = "playAudio", url }, Json));
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
        // One lock around the read and the media fill, so a background push and
        // a bridge call cannot both build the snapshot at the same time.
        lock (_gate)
        {
            var run = _engine.Run;
            FillMediaUrls(run);
            return new { setup = _engine.Setup, run };
        }
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
        router.Register("exam.snapshot", _ => Task.FromResult<object?>(Snapshot()));

        // Setup
        router.Register("exam.selectPaper", Act(a => _engine.SelectPaper(Str(a, "title"))));
        router.Register("exam.toggleSkill", Act(a => _engine.ToggleSkill(Str(a, "name"))));
        router.Register("exam.toggleTaskType", Act(a => _engine.ToggleTaskType(Str(a, "name"))));
        router.Register("exam.setMixAllPapers", Act(a => _engine.SetMixAllPapers(Bool(a, "value"))));
        router.Register("exam.setShuffleParts", Act(a => _engine.SetShuffleParts(Bool(a, "value"))));
        router.Register("exam.setStrictLevel", Act(a => _engine.SetStrictLevel(StrictModePolicy.ParseLevel(Str(a, "value")))));
        router.Register("exam.setBuildMode", Act(a => _engine.SetBuildMode(Str(a, "value"))));
        router.Register("exam.setStrictness", Act(a => _engine.SetStrictness(Str(a, "value"))));
        router.Register("exam.setVolume", Act(a => _engine.SetVolume(Dbl(a, "value"))));

        // Run control
        router.Register("exam.start", EngineActAsync((_, ct) => _engine.StartExamAsync(ct)));
        router.Register("exam.startPart", EngineAct(_ => _engine.StartPart()));
        router.Register("exam.skipPrep", Act(_ => _engine.SkipPrep()));
        router.Register("exam.nextPart", EngineAct(_ => _engine.GoNextPart()));
        router.Register("exam.nextSection", EngineAct(_ => _engine.GoNextSection()));
        router.Register("exam.selectPart", EngineAct(a => _engine.SelectPart(Int(a, "index"))));
        router.Register("exam.goToQuestion", EngineAct(a => _engine.GoToQuestion(Int(a, "index"))));
        router.Register("exam.moveQuestion", EngineAct(a => _engine.MoveQuestion(Int(a, "delta"))));

        // In-test tools: clock, text size, contrast, notes, highlights, strict.
        router.Register("exam.toggleTimer", EngineAct(_ => _engine.ToggleTimer()));
        router.Register("exam.biggerText", EngineAct(_ => _engine.ChangeFontScale(0.1)));
        router.Register("exam.smallerText", EngineAct(_ => _engine.ChangeFontScale(-0.1)));
        router.Register("exam.toggleContrast", EngineAct(_ => _engine.ToggleContrast()));
        router.Register("exam.openNotes", EngineAct(a => _engine.OpenNotes(Str(a, "selectedText"))));
        router.Register("exam.setNotes", EngineAct(a => _engine.SetNotes(Str(a, "text"))));
        router.Register("exam.closeNotes", EngineAct(_ => _engine.CloseNotes()));
        router.Register("exam.addHighlight", EngineAct(a => _engine.AddHighlight(Str(a, "text"))));
        router.Register("exam.clearHighlights", EngineAct(_ => _engine.ClearHighlights()));
        router.Register("exam.toggleStrict", EngineAct(_ => _engine.ToggleStrictMode()));

        // Answers
        router.Register("exam.setChoice", EngineAct(a => _engine.SetChoice(Int(a, "questionIndex"), Str(a, "key"))));
        router.Register("exam.setGap", EngineAct(a => _engine.SetGapAnswer(Int(a, "questionIndex"), Str(a, "text"))));
        router.Register("exam.setMatch", EngineAct(a => _engine.SetMatchRow(
            Int(a, "questionIndex"), Int(a, "rowIndex"), Str(a, "value"))));
        router.Register("exam.toggleFlag", EngineAct(a => _engine.ToggleFlag(Int(a, "questionIndex"))));
        router.Register("exam.setEssay", EngineAct(a => _engine.SetEssay(Str(a, "text"))));
        router.Register("exam.setTranscript", EngineAct(a => _engine.SetTranscript(Str(a, "text"))));

        // Submit, review, reset
        router.Register("exam.toggleReview", EngineAct(_ => _engine.ToggleReview()));
        router.Register("exam.submit", EngineActAsync((_, _) => _engine.SubmitAsync(true)));
        router.Register("exam.backToSetup", EngineAct(_ => _engine.BackToSetup()));
        router.Register("exam.cancel", Act(_ => _engine.CancelRunningTest()));

        // Speaking: the page records, the host stores the wav, the engine transcribes.
        router.Register("exam.beginRecording", EngineAct(_ =>
        {
            var result = _engine.BeginSpeakingRecording();
            if (result.Success)
            {
                var seconds = _engine.Run.CurrentPart?.SpeakingSeconds ?? 60;
                _push(JsonSerializer.Serialize(new { @event = "record", seconds }, Json));
            }
            return result;
        }));
        router.Register("exam.recordingDone", EngineActAsync(async (a, ct) =>
        {
            var path = await SaveRecordingAsync(Str(a, "base64"));
            return await _engine.CompleteSpeakingRecordingAsync(path, ct);
        }));
        router.Register("exam.recordingFailed", EngineActAsync((_, ct) =>
            _engine.CompleteSpeakingRecordingAsync(string.Empty, ct)));
        router.Register("exam.finishSpeaking", EngineAct(_ => _engine.FinishSpeakingPart()));
        router.Register("exam.checkPronunciation", EngineActAsync((_, ct) =>
            _engine.CheckPronunciationAsync(ct)));

        // Listening clip finished playing in the page.
        router.Register("exam.audioFinished", Act(a => _engine.AudioFinished(Bool(a, "ok"))));

        // AI marking
        router.Register("exam.aiMark", EngineActAsync((_, ct) => _engine.BeginAiMarkingAsync(ct)));
        router.Register("exam.stopAiMark", EngineAct(_ => _engine.CancelGrading()));
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

    /// <summary>
    /// Runs a synchronous engine action, then returns the snapshot. A failed
    /// result is surfaced as a short English message instead of being dropped,
    /// so a button press always answers the user one way or the other.
    /// </summary>
    private Func<JsonElement?, CancellationToken, Task<object?>> EngineAct(Func<JsonElement?, EngineResult> action)
        => (args, _) =>
        {
            var result = action(args);
            if (!result.Success) throw new BridgeException(result.Message);
            return Task.FromResult<object?>(Snapshot());
        };

    /// <summary>Runs an async engine action with the same result handling.</summary>
    private Func<JsonElement?, CancellationToken, Task<object?>> EngineActAsync(
        Func<JsonElement?, CancellationToken, Task<EngineResult>> action)
        => async (args, ct) =>
        {
            var result = await action(args, ct);
            if (!result.Success) throw new BridgeException(result.Message);
            return Snapshot();
        };

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
