namespace IELTop.Services.App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using IELTop.Services.Learn;

public sealed record SpeakingErrorRow(string Quote, string Correction, string Reason);
public sealed record SpeakingUpgradeRow(string Original, string Upgraded, string Note);

public sealed record SpeakingRubricResult(
    double OverallBand,
    double FcBand, string FcFeedback,
    double LrBand, string LrFeedback,
    double GraBand, string GraFeedback,
    double PrBand, string PrFeedback,
    IReadOnlyList<SpeakingErrorRow> KeyErrors,
    IReadOnlyList<SpeakingUpgradeRow> Upgrades,
    IReadOnlyList<string> Drills);

public sealed record SpeakingTopicSuggestion(
    string Part, string Title, string Prompt, IReadOnlyList<string> Bullets, IReadOnlyList<string> UsefulVocab);

/// <summary>One saved tutor attempt, ready for the UI list and detail view.</summary>
public sealed record TutorAttemptRow(
    int Id, string Mode, string Part, string Cue, string Transcript,
    double WordsPerMinute, double SpeechSeconds, int PauseCount, double MeanPauseSeconds,
    double Clarity, double PronunciationAccuracy, double MeanGop,
    string AiFeedback, string TimingLabel, DateTime CreatedAt);

/// <summary>One pronunciation word with the model confidence, for read aloud.</summary>
public sealed record TutorWordRow(string Word, string Expected, string Heard, double Gop);

public sealed record SpeakingTutorSnapshot
{
    public IReadOnlyList<StudySessionRow> Sessions { get; init; } = Array.Empty<StudySessionRow>();
    public int SelectedSessionId { get; init; }
    public string SelectedPart { get; init; } = "Part1";
    public IReadOnlyList<string> Parts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Cues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TutorAttemptRow> Attempts { get; init; } = Array.Empty<TutorAttemptRow>();
    public TutorAttemptRow? OpenAttempt { get; init; }
    public IReadOnlyList<TutorWordRow> OpenWords { get; init; } = Array.Empty<TutorWordRow>();
    public IReadOnlyList<AudioSentenceSegment> Segments { get; init; } = Array.Empty<AudioSentenceSegment>();
    public SpeakingRubricResult? RubricResult { get; init; }
    public bool SttReady { get; init; }
    public bool MddReady { get; init; }
    public bool CanUseAi { get; init; }
    public string AiHint { get; init; } = string.Empty;
    public string SttHint { get; init; } = string.Empty;
    public string MddHint { get; init; } = string.Empty;
    public bool IsBusy { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public sealed class SpeakingTutorService
{
    private static readonly string[] Parts = { "Part1", "Part2", "Part3", "ReadAloud" };

    private readonly ILlmService _llm;
    private readonly ISttService _stt;
    private readonly IMddPhonemeService _mdd;

    public SpeakingTutorService(ILlmService llm, ISttService stt, IMddPhonemeService mdd)
    {
        _llm = llm;
        _stt = stt;
        _mdd = mdd;
    }

    private bool CanUseAi => _llm.IsConfigured;

    public SpeakingTutorSnapshot Snapshot(int sessionId = 0, string part = "Part1", string query = "")
    {
        part = NormalizePart(part);
        var sessions = LoadSessions(query);
        if (sessionId <= 0)
            sessionId = sessions.Count > 0 ? sessions[0].Id : 0;

        var attempts = sessionId > 0 ? LoadAttempts(sessionId) : new List<TutorAttemptRow>();
        return new SpeakingTutorSnapshot
        {
            Sessions = sessions,
            SelectedSessionId = sessionId,
            SelectedPart = part,
            Parts = Parts,
            Cues = CuesFor(part),
            Attempts = attempts,
            OpenAttempt = attempts.Count > 0 ? attempts[0] : null,
            SttReady = _stt.IsAvailable(),
            MddReady = _mdd.IsModelAvailable(),
            CanUseAi = CanUseAi,
            AiHint = AiHint,
            SttHint = SttHint,
            MddHint = MddHint,
        };
    }

    public static IReadOnlyList<string> CuesFor(string part) => part switch
    {
        "Part2" => SpeakingBank.Part2
            .Select(c => c.Title + "\n" + c.Prompt + "\n" + string.Join("\n", c.Bullets.Select(b => "- " + b)))
            .ToList(),
        "Part3" => SpeakingBank.Part3
            .SelectMany(s => s.Questions.Select(q => s.Theme + ": " + q))
            .ToList(),
        "ReadAloud" => SpeakingBank.ReadAloud.ToList(),
        _ => SpeakingBank.Part1
            .SelectMany(t => t.Questions.Select(q => t.Topic + ": " + q))
            .ToList(),
    };

    private static string NormalizePart(string part) =>
        Parts.Contains(part ?? string.Empty) ? part! : "Part1";

    private string AiHint => CanUseAi ? string.Empty
        : "Add a language model in Settings for detailed 4-criteria feedback. Measurements work offline.";

    private string SttHint => _stt.IsAvailable() ? string.Empty
        : "Install a transcription model in Content/Assets/Models to record speech.";

    private string MddHint => _mdd.IsModelAvailable() ? string.Empty
        : "Install the pronunciation model in Content/Assets/Models for sound scores.";

    private static List<StudySessionRow> LoadSessions(string? query = null)
    {
        var rows = AppDbContext.TableAsync<StudySession>().ToListAsync().GetAwaiter().GetResult();
        IEnumerable<StudySession> filtered = rows.Where(s => s.Kind == "Speaking");
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            filtered = filtered.Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Topic.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return filtered
            .OrderByDescending(s => s.Pinned)
            .ThenByDescending(s => s.UpdatedAt)
            .Select(s => new StudySessionRow(s.Id, s.Title, s.Kind, s.Unit, s.Topic, s.ItemCount, s.Pinned, s.UpdatedAt))
            .ToList();
    }

    private static List<TutorAttemptRow> LoadAttempts(int sessionId)
    {
        var rows = AppDbContext.TableAsync<TutorSpeakingAttempt>().ToListAsync().GetAwaiter().GetResult();
        return rows
            .Where(a => a.SessionId == sessionId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(ToRow)
            .ToList();
    }

    private static TutorAttemptRow ToRow(TutorSpeakingAttempt a) => new(
        a.Id, a.Mode, a.Part, a.Cue, a.Transcript,
        a.WordsPerMinute, a.SpeechSeconds, a.PauseCount, a.MeanPauseSeconds,
        a.Clarity, a.PronunciationAccuracy, a.MeanGop,
        a.AiFeedback, TimingLabelFor(a), a.CreatedAt);

    private static string TimingLabelFor(TutorSpeakingAttempt a) =>
        a.PauseCount <= 0 && a.SpeechSeconds <= 0
            ? string.Empty
            : $"{a.SpeechSeconds:0.#}s of speech, {a.PauseCount} pause(s), avg pause {a.MeanPauseSeconds:0.0}s.";

    public SpeakingTutorSnapshot NewSession(string part)
    {
        part = NormalizePart(part);
        var session = new StudySession
        {
            Title = $"Speaking Practice ({part})",
            Kind = "Speaking",
            Topic = part,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        AppDbContext.InsertAsync(session).GetAwaiter().GetResult();
        return Snapshot(session.Id, part);
    }

    public SpeakingTutorSnapshot RenameSession(int id, string title)
    {
        var session = AppDbContext.FindAsync<StudySession>(id).GetAwaiter().GetResult();
        if (session is not null && !string.IsNullOrWhiteSpace(title))
        {
            session.Title = title.Trim();
            session.UpdatedAt = DateTime.UtcNow;
            AppDbContext.UpdateAsync(session).GetAwaiter().GetResult();
        }
        return Snapshot(id, session?.Topic ?? "Part1");
    }

    public SpeakingTutorSnapshot PinSession(int id)
    {
        var session = AppDbContext.FindAsync<StudySession>(id).GetAwaiter().GetResult();
        if (session is not null)
        {
            session.Pinned = !session.Pinned;
            session.UpdatedAt = DateTime.UtcNow;
            AppDbContext.UpdateAsync(session).GetAwaiter().GetResult();
        }
        return Snapshot(id, session?.Topic ?? "Part1");
    }

    public SpeakingTutorSnapshot DeleteSession(int id)
    {
        var session = AppDbContext.FindAsync<StudySession>(id).GetAwaiter().GetResult();
        if (session is not null)
        {
            var attempts = AppDbContext.TableAsync<TutorSpeakingAttempt>().ToListAsync().GetAwaiter().GetResult();
            foreach (var a in attempts.Where(a => a.SessionId == id))
            {
                DeleteWav(a.AudioPath);
                AppDbContext.DeleteAsync(a).GetAwaiter().GetResult();
            }
            AppDbContext.DeleteAsync(session).GetAwaiter().GetResult();
        }
        return Snapshot();
    }

    public SpeakingTutorSnapshot DeleteAttempt(int attemptId, int sessionId, string part)
    {
        var attempt = AppDbContext.FindAsync<TutorSpeakingAttempt>(attemptId).GetAwaiter().GetResult();
        if (attempt is not null)
        {
            DeleteWav(attempt.AudioPath);
            AppDbContext.DeleteAsync(attempt).GetAwaiter().GetResult();
            TouchSession(sessionId);
        }
        return Snapshot(sessionId, part);
    }

    private static void TouchSession(int sessionId)
    {
        var session = AppDbContext.FindAsync<StudySession>(sessionId).GetAwaiter().GetResult();
        if (session is null) return;
        var count = AppDbContext.TableAsync<TutorSpeakingAttempt>().ToListAsync().GetAwaiter().GetResult()
            .Count(a => a.SessionId == sessionId);
        session.ItemCount = count;
        session.UpdatedAt = DateTime.UtcNow;
        AppDbContext.UpdateAsync(session).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Suggests an authentic topic for speaking practice.
    /// </summary>
    public SpeakingTopicSuggestion SuggestTopic(string part)
    {
        part = NormalizePart(part);
        var rnd = new Random();

        if (part == "Part2")
        {
            var card = SpeakingBank.Part2[rnd.Next(SpeakingBank.Part2.Count)];
            return new SpeakingTopicSuggestion(
                "Part2",
                card.Title,
                card.Prompt,
                card.Bullets,
                new[] { "first and foremost", "vividly remember", "profound impact", "remarkable experience" });
        }
        if (part == "Part3")
        {
            var set = SpeakingBank.Part3[rnd.Next(SpeakingBank.Part3.Count)];
            var q = set.Questions[rnd.Next(set.Questions.Count)];
            return new SpeakingTopicSuggestion(
                "Part3",
                set.Theme,
                q,
                new[] { "Consider perspectives", "Give real-life example", "Weigh advantages and disadvantages" },
                new[] { "from my perspective", "on the broader scale", "inevitably leads to", "substantial factor" });
        }

        // Part 1
        var topic = SpeakingBank.Part1[rnd.Next(SpeakingBank.Part1.Count)];
        var question = topic.Questions[rnd.Next(topic.Questions.Count)];
        return new SpeakingTopicSuggestion(
            "Part1",
            topic.Topic,
            question,
            new[] { "Answer directly", "Extend with reason or frequency" },
            new[] { "generally speaking", "as a matter of fact", "on a regular basis", "appeals to me" });
    }

    /// <summary>
    /// Processes speaking audio: STT transcription, audio timing, phoneme clarity,
    /// transcript-aligned audio slicing, and official 4-criteria IELTS evaluation.
    /// </summary>
    public async Task<SpeakingTutorSnapshot> SubmitAnswerAsync(
        int sessionId, string part, string cue, string audioBase64, CancellationToken ct = default)
    {
        part = NormalizePart(part);
        if (!_stt.IsAvailable())
            return Snapshot(sessionId, part) with { StatusMessage = SttHint };
        if (sessionId <= 0)
        {
            var s = NewSession(part);
            sessionId = s.SelectedSessionId;
        }

        var wavPath = await SaveRecordingAsync(audioBase64).ConfigureAwait(false);
        if (string.IsNullOrEmpty(wavPath))
            return Snapshot(sessionId, part) with { StatusMessage = "No audio was captured. Check your microphone." };

        string transcript;
        try
        {
            var stt = await _stt.TranscribeAsync(wavPath, ct).ConfigureAwait(false);
            if (!stt.Success || string.IsNullOrWhiteSpace(stt.Text))
            {
                DeleteWav(wavPath);
                return Snapshot(sessionId, part) with
                {
                    StatusMessage = "No clear words were recognized. Speak closer to the microphone and try again.",
                };
            }
            transcript = stt.Text.Trim();
        }
        catch (OperationCanceledException) { DeleteWav(wavPath); throw; }

        var samples = WavLoader.LoadMono16k(wavPath);
        var timing = SpeechTimingAnalyzer.Analyze(samples);

        double clarity = 0;
        try { clarity = await _mdd.ClarityAsync(wavPath, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { DeleteWav(wavPath); throw; }
        catch (Exception) { /* clarity optional */ }

        // Transcript-aligned audio slicing
        var segments = AudioSegmenter.SegmentByTranscript(samples, transcript, clarity);

        int words = CountWords(transcript);
        double wpm = timing.SpeechSeconds > 0 ? Math.Round(words * 60.0 / timing.SpeechSeconds, 0) : 0;

        // Perform official 4-criteria IELTS evaluation if AI is available
        SpeakingRubricResult? rubricResult = null;
        string aiFeedbackText = string.Empty;

        if (CanUseAi)
        {
            try
            {
                var prompt = StudyPrompts.BuildSpeakingFeedback(
                    "Answer", part, cue, transcript, wpm, timing.SpeechSeconds,
                    timing.PauseCount, timing.MeanPauseSeconds, clarity, 0, 0);

                var messages = new[]
                {
                    LlmMessage.System(StudyPrompts.SpeakingFeedbackSystem),
                    LlmMessage.User(prompt),
                };

                var llmRes = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);
                if (llmRes.Success)
                {
                    aiFeedbackText = llmRes.Text.Trim();
                    rubricResult = TryParseRubric(aiFeedbackText);
                }
            }
            catch (Exception) { /* fallback */ }
        }

        var attempt = new TutorSpeakingAttempt
        {
            SessionId = sessionId,
            Mode = "Answer",
            Part = part,
            Cue = cue ?? string.Empty,
            Transcript = transcript,
            WordsPerMinute = wpm,
            SpeechSeconds = timing.SpeechSeconds,
            PauseCount = timing.PauseCount,
            MeanPauseSeconds = timing.MeanPauseSeconds,
            Clarity = clarity,
            AiFeedback = aiFeedbackText,
            AudioPath = wavPath,
            CreatedAt = DateTime.UtcNow,
        };

        await AppDbContext.InsertAsync(attempt).ConfigureAwait(false);
        TouchSession(sessionId);

        var snap = Snapshot(sessionId, part);
        var open = snap.Attempts.FirstOrDefault(a => a.Id == attempt.Id) ?? snap.OpenAttempt;

        return snap with
        {
            OpenAttempt = open,
            Segments = segments,
            RubricResult = rubricResult,
            StatusMessage = rubricResult != null ? "Evaluation complete across 4 IELTS criteria." : string.Empty,
        };
    }

    public async Task<string> GenerateSegmentDrillAsync(
        string sentence, string reason, string drillType, CancellationToken ct = default)
    {
        if (!CanUseAi)
            return $"{drillType}: Practice saying: '{sentence}' clearly with natural rhythm.";

        var prompt = StudyPrompts.BuildAudioSegmentDrill(sentence, reason, drillType);
        var messages = new[]
        {
            LlmMessage.System("You are an IELTS speaking coach creating micro-drills."),
            LlmMessage.User(prompt),
        };

        var res = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);
        return res.Success ? res.Text.Trim() : $"{drillType}: Practice saying: '{sentence}'";
    }

    public async Task<(SpeakingTutorSnapshot Snapshot, IReadOnlyList<TutorWordRow> Words)> SubmitReadAloudAsync(
        int sessionId, string line, string audioBase64, CancellationToken ct = default)
    {
        if (!_mdd.IsModelAvailable())
            return (Snapshot(sessionId, "ReadAloud") with { StatusMessage = MddHint }, Array.Empty<TutorWordRow>());
        if (sessionId <= 0)
            return (Snapshot(sessionId, "ReadAloud") with { StatusMessage = "Start a session first." }, Array.Empty<TutorWordRow>());

        var wavPath = await SaveRecordingAsync(audioBase64).ConfigureAwait(false);
        if (string.IsNullOrEmpty(wavPath))
            return (Snapshot(sessionId, "ReadAloud") with { StatusMessage = "No audio was captured. Check your microphone." }, Array.Empty<TutorWordRow>());

        MddResult scored;
        try { scored = await _mdd.AssessAsync(wavPath, line ?? string.Empty, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { DeleteWav(wavPath); throw; }
        if (!scored.Success)
        {
            DeleteWav(wavPath);
            return (Snapshot(sessionId, "ReadAloud") with { StatusMessage = scored.Error }, Array.Empty<TutorWordRow>());
        }

        string transcript = string.Empty;
        if (_stt.IsAvailable())
        {
            try
            {
                var stt = await _stt.TranscribeAsync(wavPath, ct).ConfigureAwait(false);
                if (stt.Success) transcript = stt.Text.Trim();
            }
            catch (Exception) { /* transcript optional for read aloud */ }
        }

        var samples = WavLoader.LoadMono16k(wavPath);
        var timing = SpeechTimingAnalyzer.Analyze(samples);

        var attempt = new TutorSpeakingAttempt
        {
            SessionId = sessionId,
            Mode = "ReadAloud",
            Cue = line ?? string.Empty,
            Transcript = transcript,
            WordsPerMinute = timing.SpeechSeconds > 0
                ? Math.Round(CountWords(transcript.Length > 0 ? transcript : line) * 60.0 / timing.SpeechSeconds, 0) : 0,
            SpeechSeconds = timing.SpeechSeconds,
            PauseCount = timing.PauseCount,
            MeanPauseSeconds = timing.MeanPauseSeconds,
            PronunciationAccuracy = scored.Accuracy,
            MeanGop = scored.MeanGop,
            AudioPath = wavPath,
            CreatedAt = DateTime.UtcNow,
        };
        await AppDbContext.InsertAsync(attempt).ConfigureAwait(false);
        TouchSession(sessionId);

        var words = scored.Words
            .Select(w => new TutorWordRow(w.Word, w.Expected, w.Heard, w.Gop))
            .ToList();
        var snapshot = Snapshot(sessionId, "ReadAloud");
        var open = snapshot.Attempts.Count > 0 ? snapshot.Attempts[0] : null;
        return (snapshot with { OpenAttempt = open, OpenWords = words }, words);
    }

    public async Task<SpeakingTutorSnapshot> FeedbackAsync(
        int attemptId, int sessionId, string part, CancellationToken ct = default)
    {
        if (!CanUseAi)
            return Snapshot(sessionId, part) with { StatusMessage = AiHint };
        var attempt = await AppDbContext.FindAsync<TutorSpeakingAttempt>(attemptId).ConfigureAwait(false);
        if (attempt is null)
            return Snapshot(sessionId, part) with { StatusMessage = "That attempt is gone." };

        var prompt = StudyPrompts.BuildSpeakingFeedback(
            attempt.Mode, attempt.Part, attempt.Cue, attempt.Transcript,
            attempt.WordsPerMinute, attempt.SpeechSeconds, attempt.PauseCount,
            attempt.MeanPauseSeconds, attempt.Clarity, attempt.PronunciationAccuracy, attempt.MeanGop);

        var messages = new[]
        {
            LlmMessage.System(StudyPrompts.SpeakingFeedbackSystem),
            LlmMessage.User(prompt),
        };

        var result = await _llm.CompleteAsync(messages, ct).ConfigureAwait(false);
        if (!result.Success)
            return Snapshot(sessionId, part) with { StatusMessage = result.Error };

        attempt.AiFeedback = result.Text.Trim();
        await AppDbContext.UpdateAsync(attempt).ConfigureAwait(false);
        var snapshot = Snapshot(sessionId, part);
        var open = snapshot.Attempts.FirstOrDefault(a => a.Id == attemptId);
        return snapshot with { OpenAttempt = open ?? snapshot.OpenAttempt };
    }

    public async Task<(SpeakingTutorSnapshot Snapshot, IReadOnlyList<TutorWordRow> Words)> OpenAttemptAsync(
        int attemptId, int sessionId, string part, CancellationToken ct = default)
    {
        var snapshot = Snapshot(sessionId, part);
        var open = snapshot.Attempts.FirstOrDefault(a => a.Id == attemptId) ?? snapshot.OpenAttempt;
        if (open is null) return (snapshot, Array.Empty<TutorWordRow>());
        if (open.Mode != "ReadAloud") return (snapshot with { OpenAttempt = open }, Array.Empty<TutorWordRow>());

        var attempt = await AppDbContext.FindAsync<TutorSpeakingAttempt>(attemptId).ConfigureAwait(false);
        if (attempt is null || !_mdd.IsModelAvailable() || !File.Exists(attempt.AudioPath))
            return (snapshot with { OpenAttempt = open }, Array.Empty<TutorWordRow>());
        try
        {
            var scored = await _mdd.AssessAsync(attempt.AudioPath, attempt.Cue, ct).ConfigureAwait(false);
            if (!scored.Success) return (snapshot with { OpenAttempt = open }, Array.Empty<TutorWordRow>());
            var words = scored.Words
                .Select(w => new TutorWordRow(w.Word, w.Expected, w.Heard, w.Gop))
                .ToList();
            return (snapshot with { OpenAttempt = open, OpenWords = words }, words);
        }
        catch (Exception)
        {
            return (snapshot with { OpenAttempt = open }, Array.Empty<TutorWordRow>());
        }
    }

    private static SpeakingRubricResult? TryParseRubric(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // Strip code fences if any
            int start = json.IndexOf('{');
            int end = json.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                json = json.Substring(start, end - start + 1);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            double overall = root.TryGetProperty("overallBand", out var ob) ? ob.GetDouble() : 6.0;
            double fc = root.TryGetProperty("fcBand", out var fcb) ? fcb.GetDouble() : 6.0;
            string fcNote = root.TryGetProperty("fcFeedback", out var fcn) ? fcn.GetString() ?? "" : "";
            double lr = root.TryGetProperty("lrBand", out var lrb) ? lrb.GetDouble() : 6.0;
            string lrNote = root.TryGetProperty("lrFeedback", out var lrn) ? lrn.GetString() ?? "" : "";
            double gra = root.TryGetProperty("graBand", out var grab) ? grab.GetDouble() : 6.0;
            string graNote = root.TryGetProperty("graFeedback", out var gran) ? gran.GetString() ?? "" : "";
            double pr = root.TryGetProperty("prBand", out var prb) ? prb.GetDouble() : 6.0;
            string prNote = root.TryGetProperty("prFeedback", out var prn) ? prn.GetString() ?? "" : "";

            var errors = new List<SpeakingErrorRow>();
            if (root.TryGetProperty("keyErrors", out var errArr) && errArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in errArr.EnumerateArray())
                {
                    string quote = item.TryGetProperty("quote", out var q) ? q.GetString() ?? "" : "";
                    string corr = item.TryGetProperty("correction", out var c) ? c.GetString() ?? "" : "";
                    string rsn = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(quote)) errors.Add(new SpeakingErrorRow(quote, corr, rsn));
                }
            }

            var upgrades = new List<SpeakingUpgradeRow>();
            if (root.TryGetProperty("upgrades", out var upgArr) && upgArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in upgArr.EnumerateArray())
                {
                    string orig = item.TryGetProperty("original", out var o) ? o.GetString() ?? "" : "";
                    string upg = item.TryGetProperty("upgraded", out var u) ? u.GetString() ?? "" : "";
                    string note = item.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(orig)) upgrades.Add(new SpeakingUpgradeRow(orig, upg, note));
                }
            }

            var drills = new List<string>();
            if (root.TryGetProperty("drills", out var drArr) && drArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in drArr.EnumerateArray())
                {
                    string d = item.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(d)) drills.Add(d);
                }
            }

            return new SpeakingRubricResult(
                overall, fc, fcNote, lr, lrNote, gra, graNote, pr, prNote, errors, upgrades, drills);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static async Task<string> SaveRecordingAsync(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return string.Empty;
        int comma = base64.IndexOf(',');
        if (comma >= 0) base64 = base64[(comma + 1)..];
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return string.Empty; }
        if (bytes.Length < 1000 || bytes.Length > 12 * 1024 * 1024) return string.Empty;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IELTop", "recordings");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"tutor_{DateTime.Now:yyyyMMdd_HHmmssfff}.wav");
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            return path;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static void DeleteWav(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch (Exception) { /* safe */ }
    }
}
