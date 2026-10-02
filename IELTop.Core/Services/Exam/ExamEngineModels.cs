using System.Globalization;
using IELTop.Models;
using IELTop.Services.App;

namespace IELTop.Services.Exam;

/// <summary>Where a run is: still setting up, on a part intro, running, or done.</summary>
public enum ExamPhase
{
    Setup,
    PartIntro,
    Running,
    Finished,
}

/// <summary>One answer choice inside a run.</summary>
public sealed class ExamRunOption
{
    public string Key { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
}

/// <summary>One matching row inside a run, answered with a bank value.</summary>
public sealed class ExamRunMatchRow
{
    public string Label { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string Selected { get; set; } = string.Empty;

    public bool IsCorrect => Norm(Selected) == Norm(Answer) && Selected.Length > 0;

    internal static string Norm(string text)
    {
        var parts = text.Trim().ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }
}

/// <summary>
/// One question inside a run, with the student's answer. All the labels the
/// UI needs are computed here, so both clients show the same text.
/// </summary>
public sealed class ExamRunQuestion
{
    public int Number { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public string Kind { get; init; } = "choice";
    public string CorrectKey { get; init; } = string.Empty;
    public string GapAnswer { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<ExamRunOption> Options { get; init; } = Array.Empty<ExamRunOption>();
    public IReadOnlyList<string> Bank { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ExamRunMatchRow> MatchRows { get; init; } = Array.Empty<ExamRunMatchRow>();

    public string SelectedKey { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool IsFlagged { get; set; }

    public bool IsGap => string.Equals(Kind, "gap", StringComparison.OrdinalIgnoreCase);
    public bool IsMatch => string.Equals(Kind, "match", StringComparison.OrdinalIgnoreCase);
    public bool ShowOptions => !IsGap && !IsMatch;

    /// <summary>Text before the blank of a gap sentence.</summary>
    public string GapBefore { get; init; } = string.Empty;

    /// <summary>Text after the blank of a gap sentence.</summary>
    public string GapAfter { get; init; } = string.Empty;

    /// <summary>The question number shown inside an empty gap box.</summary>
    public string GapBoxNumber => Number.ToString(CultureInfo.InvariantCulture);

    public bool IsAnswered => IsMatch
        ? MatchRows.Any(r => !string.IsNullOrWhiteSpace(r.Selected))
        : IsGap ? !string.IsNullOrWhiteSpace(Answer) : !string.IsNullOrEmpty(SelectedKey);

    public bool IsCorrect => IsMatch
        ? MatchRows.Count > 0 && MatchRows.All(r => r.IsCorrect)
        : IsGap ? MatchesGap(Answer) : SelectedKey == CorrectKey;

    /// <summary>First accepted answer, shown in Reading review.</summary>
    public string DisplayAnswer => IsGap ? FirstGapAnswer : CorrectKey;

    private string FirstGapAnswer =>
        GapAnswer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

    private bool MatchesGap(string answer)
    {
        var given = ExamRunMatchRow.Norm(answer);
        if (given.Length == 0) return false;
        return GapAnswer
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => ExamRunMatchRow.Norm(a) == given);
    }

    public string ChosenLabel => IsMatch
        ? string.Join("; ", MatchRows.Select(r => $"{r.Label}={r.Selected.Trim()}"))
        : IsGap ? Answer.Trim() : SelectedKey;

    public string ReviewLabel => string.IsNullOrWhiteSpace(ChosenLabel)
        ? $"no answer, correct is {DisplayAnswer}"
        : IsCorrect ? "correct" : $"you chose {ChosenLabel}, correct is {DisplayAnswer}";

    public string ReadingReviewLabel
    {
        get
        {
            var baseLine = ReviewLabel + (IsFlagged ? " (flagged)" : string.Empty);
            return string.IsNullOrWhiteSpace(Explanation) ? baseLine : $"{baseLine}. Why: {Explanation}";
        }
    }

    /// <summary>Listening review names only the wrong questions, no answers.</summary>
    public string ListeningReviewLabel => IsFlagged ? "wrong (flagged)" : "wrong";
}

/// <summary>
/// One part inside a run, with its own countdown, answer text, and audio state.
/// </summary>
public sealed class ExamRunPart
{
    public int Index { get; internal set; }
    public string PaperName { get; internal set; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Skill { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string TaskType { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public string Material { get; init; } = string.Empty;
    public int Minutes { get; init; }
    public int PrepSeconds { get; init; }
    public string AudioFileName { get; init; } = string.Empty;
    public string ImageFileName { get; init; } = string.Empty;

    public IReadOnlyList<ExamRunQuestion> Questions { get; init; } = Array.Empty<ExamRunQuestion>();

    public int RemainingSeconds { get; internal set; }
    public bool IsTimerRunning { get; internal set; }
    public string Essay { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public string AudioStatus { get; internal set; } = string.Empty;
    public bool IsRecording { get; internal set; }
    public bool AudioPlayedOnce { get; internal set; }
    public bool NoAudioFallback { get; internal set; }
    public int SpokenSeconds { get; internal set; }
    public bool IsCurrent { get; internal set; }
    public bool IsPassed { get; internal set; }
    public string AiResult { get; internal set; } = string.Empty;

    /// <summary>Clock hidden while the student focuses; click Show time to bring it back.</summary>
    public bool IsTimerHidden { get; set; }

    /// <summary>High contrast reading: yellow on black for the passage and essay box.</summary>
    public bool ContrastOn { get; set; }

    /// <summary>Personal notes for this part, shown in the side panel.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Passage text the student selected before opening notes.</summary>
    public string SelectedMaterialWord { get; set; } = string.Empty;

    /// <summary>Notes panel open state, and the highlight list for the passage.</summary>
    public bool NotesOpen { get; set; }
    public List<string> Highlights { get; set; } = new();

    /// <summary>Path of the last saved recording, for the pronunciation check.</summary>
    public string LastRecordingPath { get; set; } = string.Empty;

    /// <summary>Short pronunciation verdict, empty until the check runs.</summary>
    public string PronunciationSummary { get; set; } = string.Empty;

    /// <summary>Words the pronunciation model flagged, for the UI list.</summary>
    public IReadOnlyList<PronunciationWord> PronunciationWords { get; set; } = Array.Empty<PronunciationWord>();

    public bool HasMaterial => !string.IsNullOrWhiteSpace(Material);
    public bool IsWriting => string.Equals(Skill, "Writing", StringComparison.OrdinalIgnoreCase)
                             && Questions.Count == 0;
    public bool IsSpeaking => string.Equals(Skill, "Speaking", StringComparison.OrdinalIgnoreCase);
    public bool IsListening => string.Equals(Skill, "Listening", StringComparison.OrdinalIgnoreCase);
    public bool IsReading => string.Equals(Skill, "Reading", StringComparison.OrdinalIgnoreCase);
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioFileName);
    public bool HasQuestions => Questions.Count > 0;

    /// <summary>Resolved file URL for the Writing Task 1 chart, set by the host.</summary>
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsWritingTask1 => IsWriting
        && (TaskType.Contains("Task 1", StringComparison.OrdinalIgnoreCase)
            || Title.Contains("Task 1", StringComparison.OrdinalIgnoreCase));
    public bool HasWritingTask1Image => IsWritingTask1 && !string.IsNullOrWhiteSpace(ImageFileName);
    public bool HasWritingImageFile => HasWritingTask1Image && !string.IsNullOrEmpty(ImageUrl);
    public bool ShowMissingImageHint => HasWritingTask1Image && !HasWritingImageFile;
    public bool ShowNoImageNote => IsWritingTask1 && !HasWritingTask1Image;
    public string ImageHint => HasWritingTask1Image
        ? $"Chart image: {ImageFileName}"
        : "Task 1 chart: paste or read the data from the question, no image is attached.";

    public string HeaderLine
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(PaperName)) bits.Add(PaperName);
            bits.Add(Skill);
            if (!string.IsNullOrWhiteSpace(TaskType)) bits.Add(TaskType);
            if (!string.IsNullOrWhiteSpace(Topic)) bits.Add(Topic);
            return string.Join(" | ", bits);
        }
    }

    public string InstructionHeading => $"Part {Index + 1}";

    public string QuestionGroupHeading
    {
        get
        {
            if (Questions.Count == 0) return Skill;
            int first = Questions[0].Number;
            int last = Questions[^1].Number;
            return first == last ? $"Question {first}" : $"Questions {first}-{last}";
        }
    }

    public string QuestionGroupHint
    {
        get
        {
            if (Questions.Count == 0) return string.Empty;
            if (Questions.Any(q => q.IsGap)) return "Write the answer in the blank. Spelling must be correct.";
            if (Questions.Any(q => q.IsMatch)) return "Match each item with the correct choice.";
            if (Questions.Any(q => q.ShowOptions)) return "Choose the correct answer for each question.";
            return string.Empty;
        }
    }

    public string WordCountLabel => $"{CountWords(Essay)} words";
    public string TranscriptWordCount => $"{CountWords(Transcript)} words said";
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);
    public bool IsPrepPhase => IsSpeaking && PrepSeconds > 0 && !AudioPlayedOnce && !IsRecording;
    public string SpeakingCue => Material;
    public string RecordingHint =>
        $"Speak for about {SpeakingSeconds} seconds, then the recording stops on its own.";

    public string SpeakingStepLabel
    {
        get
        {
            if (!IsSpeaking) return string.Empty;
            if (IsRecording) return "Step 3: Recording. Speak now, no pause.";
            if (HasTranscript) return "Step 4: Check your transcript, then finish the part.";
            return PrepSeconds > 0
                ? "Step 1: Read the cue card. Step 2: Record when you are ready."
                : "Step 1: Read the question. Step 2: Record your answer.";
        }
    }

    /// <summary>Recording length for a speaking cue. Part 2 gets longer.</summary>
    public int SpeakingSeconds
    {
        get
        {
            if (PrepSeconds > 0) return Math.Clamp(Minutes * 60, 60, 180);
            if (Title.Contains("Part 2", StringComparison.OrdinalIgnoreCase)) return 120;
            return 60;
        }
    }

    public int CorrectCount => Questions.Count(q => q.IsCorrect);
    public int ScoredCount => Questions.Count;
    public int AnsweredCount => Questions.Count(q => q.IsAnswered);
    public int FlaggedCount => Questions.Count(q => q.IsFlagged);

    public string AnsweredProgressLabel => HasQuestions ? $"{AnsweredCount}/{ScoredCount}" : string.Empty;
    public string PartTabLabel => $"Part {Index + 1}";

    /// <summary>Full progress line, for example "Answered 3 of 13, marked for review 2."</summary>
    public string ProgressLabel => HasQuestions
        ? $"Answered {AnsweredCount} of {ScoredCount}, marked for review {FlaggedCount}."
        : string.Empty;

    public string RemainingLabel
    {
        get
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, RemainingSeconds));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}";
        }
    }

    /// <summary>Plain minutes line for the header, for example "54 minutes remaining".</summary>
    public string RemainingMinutesLabel
    {
        get
        {
            int minutes = (int)Math.Ceiling(Math.Max(0, RemainingSeconds) / 60.0);
            return minutes == 1 ? "1 minute remaining" : $"{minutes} minutes remaining";
        }
    }

    public bool IsLowTime => RemainingSeconds <= 600;
    public bool IsCriticalTime => RemainingSeconds <= 300;

    /// <summary>Progress segment color for the strip.</summary>
    public string ProgressFill => IsCurrent ? "#111827" : IsPassed ? "#9CA3AF" : "#E5E7EB";

    public int FocusedIndex { get; internal set; }

    internal void Tick()
    {
        if (RemainingSeconds > 0) RemainingSeconds--;
        else IsTimerRunning = false;
    }

    internal static int CountWords(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>One review line with a verdict for its icon.</summary>
public sealed record ReviewItem(string Text, bool? IsGood);

/// <summary>
/// The whole run, ready to serialize for the web UI. Built from the paper's
/// parts and the student's answers, and scored the same way on both clients.
/// </summary>
public sealed class ExamRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>Paper or test name shown in the top bar, for example "[Demo] Test 4".</summary>
    public string Title { get; internal set; } = string.Empty;
    public ExamPhase Phase { get; internal set; } = ExamPhase.Setup;
    public int PartIndex { get; internal set; }
    public IReadOnlyList<ExamRunPart> Parts { get; internal set; } = Array.Empty<ExamRunPart>();
    public string IntroTitle { get; internal set; } = string.Empty;
    public string IntroSkill { get; internal set; } = string.Empty;
    public string IntroDetail { get; internal set; } = string.Empty;
    public string IntroHint { get; internal set; } = string.Empty;
    public string ResultText { get; internal set; } = string.Empty;
    public string BandLabel { get; internal set; } = string.Empty;
    public bool ShowReview { get; internal set; }
    /// <summary>Short public criteria note shown next to the AI scores.</summary>
    public string CriteriaHint { get; internal set; } = string.Empty;
    public IReadOnlyList<ReviewItem> ReviewItems { get; internal set; } = Array.Empty<ReviewItem>();
    public IReadOnlyList<string> AiFeedbackLines { get; internal set; } = Array.Empty<string>();
    public bool IsGrading { get; internal set; }
    public string LoadingLabel { get; internal set; } = string.Empty;
    public string StatusMessage { get; internal set; } = string.Empty;
    public string ListeningPrepLabel { get; internal set; } = string.Empty;
    public int StrictViolations { get; internal set; }
    public bool StrictMode { get; internal set; }
    public bool IsFullscreen { get; internal set; }
    public double FontScale { get; internal set; } = 1.0;
    public double Volume { get; internal set; } = 80;

    /// <summary>Set while the WebView should play a Listening clip once. This is
    /// an absolute file path; the host turns it into a served URL in AudioUrl.</summary>
    public string PendingAudioUrl { get; set; } = string.Empty;

    /// <summary>Served URL for the clip to play, filled by the host.</summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>What the bottom strip and header read from.</summary>
    public string PartStepLabel => Parts.Count == 0 ? string.Empty : $"Part {PartIndex + 1} of {Parts.Count}";
    public bool IsRunning => Phase is ExamPhase.PartIntro or ExamPhase.Running;
    public bool ShowPartBody => Phase == ExamPhase.Running;
    public bool IsPartIntro => Phase == ExamPhase.PartIntro;
    public bool IsFinished => Phase == ExamPhase.Finished;
    public bool IsLastPart => Parts.Count > 0 && PartIndex == Parts.Count - 1;
    public bool HasNextPart => PartIndex < Parts.Count - 1;
    public bool HasBand => !string.IsNullOrWhiteSpace(BandLabel);
    public bool HasAiFeedback => AiFeedbackLines.Count > 0;
    public bool HasViolations => StrictViolations > 0;
    public string ViolationLabel => $"Stay in the test. Focus left {StrictViolations} time(s).";

    public ExamRunPart? CurrentPart =>
        Parts.Count == 0 ? null : Parts[Math.Clamp(PartIndex, 0, Parts.Count - 1)];
}
