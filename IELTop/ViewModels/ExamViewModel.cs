using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>
/// One answer choice. Selecting it records the key on the parent question.
/// </summary>
public sealed partial class ExamOptionViewModel : ObservableObject
{
    private readonly ExamQuestionViewModel _question;

    [ObservableProperty] private bool _isSelected;

    public string Key { get; }
    public string Text { get; }
    public string GroupName => $"q{_question.Model.Number}";

    public ExamOptionViewModel(ExamOption option, ExamQuestionViewModel question)
    {
        Key = option.Key;
        Text = option.Text;
        _question = question;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _question.SelectedKey = Key;
    }
}

/// <summary>
/// One question as presented during the test, with the student's choice.
/// </summary>
public sealed partial class ExamQuestionViewModel : ObservableObject
{
    public ExamQuestion Model { get; }

    [ObservableProperty] private string _selectedKey = string.Empty;
    [ObservableProperty] private bool _isFlagged;

    public ObservableCollection<ExamOptionViewModel> Options { get; } = new();

    public string NumberLabel => $"Question {Model.Number}";
    public string Prompt => Model.Prompt;
    public string Explanation => Model.Explanation;
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Model.Explanation);
    public string FlagLabel => IsFlagged ? "Flagged" : "Flag";

    public ExamQuestionViewModel(ExamQuestion model)
    {
        Model = model;
        foreach (var option in model.Options)
            Options.Add(new ExamOptionViewModel(option, this));
    }

    partial void OnSelectedKeyChanged(string value)
    {
        foreach (var option in Options)
            option.IsSelected = option.Key == value;
    }

    partial void OnIsFlaggedChanged(bool value) => OnPropertyChanged(nameof(FlagLabel));

    public bool IsCorrect => SelectedKey == Model.CorrectKey;

    public string FlagSuffix => IsFlagged ? " (flagged)" : string.Empty;

    public string ReviewLabel => string.IsNullOrEmpty(SelectedKey)
        ? $"no answer, correct is {Model.CorrectKey}"
        : IsCorrect ? "correct" : $"you chose {SelectedKey}, correct is {Model.CorrectKey}";

    /// <summary>Listening review names only the wrong questions, no answers.</summary>
    public string ListeningReviewLabel => $"wrong{FlagSuffix}";

    public string ReadingReviewLabel
    {
        get
        {
            var baseLine = ReviewLabel + FlagSuffix;
            return HasExplanation ? $"{baseLine}. Why: {Explanation}" : baseLine;
        }
    }
}

/// <summary>
/// One part of the test with a countdown timer. Listening parts can play a
/// clip, Writing parts collect an essay, Speaking parts collect a recording
/// plus a typed transcript so AI marking works even without a local model.
/// </summary>
public sealed partial class ExamPartViewModel : ObservableObject
{
    public ExamPart Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingLabel))]
    private int _remainingSeconds;
    [ObservableProperty] private bool _isTimerRunning;
    [ObservableProperty] private string _essay = string.Empty;
    [ObservableProperty] private string _transcript = string.Empty;
    [ObservableProperty] private string _audioStatus = string.Empty;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _aiResult = string.Empty;
    [ObservableProperty] private bool _audioPlayedOnce;

    public string Title => Model.Title;
    public string Skill => Model.Skill;
    public string Topic => Model.Topic;
    public string TaskType => Model.TaskType;
    public string Instructions => Model.Instructions;
    public string Material => Model.Material;
    public string AudioFile => Model.AudioFile;
    public bool HasMaterial => !string.IsNullOrWhiteSpace(Material);
    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);
    public bool HasTaskType => !string.IsNullOrWhiteSpace(TaskType);

    public bool IsWriting => string.Equals(Skill, "Writing", StringComparison.OrdinalIgnoreCase)
                             && Questions.Count == 0;
    public bool IsSpeaking => string.Equals(Skill, "Speaking", StringComparison.OrdinalIgnoreCase);
    public bool IsListening => string.Equals(Skill, "Listening", StringComparison.OrdinalIgnoreCase);
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioFile);

    public string HeaderLine
    {
        get
        {
            var bits = new List<string> { Skill };
            if (HasTaskType) bits.Add(TaskType);
            if (HasTopic) bits.Add(Topic);
            return string.Join(" | ", bits);
        }
    }

    /// <summary>Short label for the part tab bar. Falls back to skill order.</summary>
    public string TabLabel => string.IsNullOrWhiteSpace(Model.Id)
        ? Skill
        : Model.Id;

    public ObservableCollection<ExamQuestionViewModel> Questions { get; } = new();
    public bool HasQuestions => Questions.Count > 0;

    public string WordCountLabel => $"{CountWords(Essay)} words";
    public string TranscriptWordCount => $"{CountWords(Transcript)} words said";

    public ExamPartViewModel(ExamPart model)
    {
        Model = model;
        RemainingSeconds = Math.Max(60, model.Minutes * 60);
        foreach (var q in model.Questions)
            Questions.Add(new ExamQuestionViewModel(q));
    }

    partial void OnEssayChanged(string value) => OnPropertyChanged(nameof(WordCountLabel));
    partial void OnTranscriptChanged(string value) => OnPropertyChanged(nameof(TranscriptWordCount));

    public void Tick()
    {
        if (RemainingSeconds > 0)
            RemainingSeconds--;
        else
            IsTimerRunning = false;
    }

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

    public int CorrectCount => Questions.Count(q => q.IsCorrect);
    public int ScoredCount => Questions.Count;

    /// <summary>Recording length for a speaking cue. Part 2 gets longer.</summary>
    public int SpeakingSeconds
    {
        get
        {
            if (Model.PrepSeconds > 0) return Math.Clamp(Model.Minutes * 60, 60, 180);
            if (Title.Contains("Part 2", StringComparison.OrdinalIgnoreCase)) return 120;
            return 60;
        }
    }

    private static int CountWords(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// Drives the mock test: full paper or one skill, IDP style, with an
/// optional strict mode. Objective parts score offline. Writing and
/// Speaking get AI band ranges when a model is set, and stay honestly
/// unmarked when it is not. Speaking runs without pause, like the real test.
/// </summary>
public sealed partial class ExamViewModel : ObservableObject
{
    private readonly IExamRepository _repository;
    private readonly IIeltsAiService _ai;
    private readonly IAudioService _audio;
    private readonly ITtsService _tts;
    private readonly ISttService _stt;
    private readonly IGecService _gec;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly List<ExamPartViewModel> _parts = new();
    private CancellationTokenSource? _speakingCts;
    private CancellationTokenSource? _listeningCts;

    [ObservableProperty] private ExamPaper? _selectedPaper;
    [ObservableProperty] private ExamPartViewModel? _currentPart;
    [ObservableProperty] private int _partIndex;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private string _bandLabel = string.Empty;
    [ObservableProperty] private bool _showReview;
    [ObservableProperty] private string _statusMessage = "Pick a paper, a scope, and a marking level, then start.";
    [ObservableProperty] private string _selectedScope = "Full test";
    [ObservableProperty] private MarkingStrictness _selectedStrictness = MarkingStrictness.Standard;
    [ObservableProperty] private bool _strictMode;
    [ObservableProperty] private bool _shuffleParts;
    [ObservableProperty] private bool _isGrading;
    [ObservableProperty] private double _volume = 80;

    public ObservableCollection<ExamPaper> Papers { get; } = new();
    public ObservableCollection<string> ReviewLines { get; } = new();
    public ObservableCollection<string> AiFeedbackLines { get; } = new();

    // Listening audio cannot be split, so it only runs inside a full test.
    // Reading, Writing, and Speaking can each run alone.
    public IReadOnlyList<string> Scopes { get; } = new[] { "Full test", "Reading", "Writing", "Speaking" };
    public IReadOnlyList<MarkingStrictness> StrictnessOptions { get; } =
        new[] { MarkingStrictness.Lenient, MarkingStrictness.Standard, MarkingStrictness.Strict };

    public ExamViewModel(IExamRepository repository, IIeltsAiService ai, IAudioService audio, ITtsService tts, ISttService stt, IGecService gec)
    {
        _repository = repository;
        _ai = ai;
        _audio = audio;
        _tts = tts;
        _stt = stt;
        _gec = gec;
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public bool HasPapers => Papers.Count > 0;
    public bool ShowNoPaperWarning => !HasPapers;
    public bool CanUseAi => _ai.IsAvailable;
    public bool ShowAiHint => !_ai.IsAvailable;
    public bool HasBand => !string.IsNullOrWhiteSpace(BandLabel);
    public bool HasAiFeedback => AiFeedbackLines.Count > 0;

    // Speaking parts run straight through, so manual navigation is hidden there.
    public bool CanGoBack => IsRunning && PartIndex > 0 && CurrentPart?.IsSpeaking != true;
    public bool CanGoNext => IsRunning && PartIndex < _parts.Count - 1 && CurrentPart?.IsSpeaking != true;
    public bool ShowResult => !IsRunning && !string.IsNullOrWhiteSpace(ResultText);

    public string PaperCountLabel => $"{Papers.Count} test paper(s) available.";

    public string MaterialWarning =>
        $"No test papers found. Add a .json paper under {_repository.ExamsDir}.";

    public string StrictModeHint => StrictMode
        ? "Strict mode is on. Full screen, stay on this test."
        : "Strict mode off. Turn it on for a real exam feel.";

    public string CriteriaHint =>
        "Listening and Reading use the official raw to band table. " +
        "Writing and Speaking average four criteria to half bands. " +
        "Bands are ranges, because examiners vary. Practice estimates only.";

    partial void OnStrictModeChanged(bool value) => OnPropertyChanged(nameof(StrictModeHint));

    partial void OnVolumeChanged(double value) => _audio.Volume = value / 100.0;

    public IReadOnlyList<ExamPartViewModel> Parts => _parts;

    public void Load()
    {
        Papers.Clear();
        foreach (var paper in _repository.LoadPapers())
            Papers.Add(paper);
        SelectedPaper ??= Papers.FirstOrDefault();

        OnPropertyChanged(nameof(HasPapers));
        OnPropertyChanged(nameof(ShowNoPaperWarning));
        OnPropertyChanged(nameof(PaperCountLabel));
    }

    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(ShowAiHint));
    }

    private IEnumerable<ExamPart> FilteredParts()
    {
        if (SelectedPaper is null) return Enumerable.Empty<ExamPart>();
        if (SelectedScope == "Full test") return SelectedPaper.Parts;
        return SelectedPaper.Parts.Where(p =>
            string.Equals(p.Skill, SelectedScope, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void StartExam()
    {
        _parts.Clear();
        _timer.Stop();
        _speakingCts?.Cancel();
        _listeningCts?.Cancel();
        _audio.StopPlayback();
        _audio.StopRecording();

        var chosen = FilteredParts().ToList();
        if (SelectedPaper is null || chosen.Count == 0)
        {
            ResultText = SelectedScope == "Full test"
                ? "Pick a test paper with at least one part."
                : $"This paper has no {SelectedScope} part. Pick another scope or paper.";
            BandLabel = string.Empty;
            IsRunning = false;
            IsFinished = false;
            return;
        }

        if (ShuffleParts)
        {
            var rng = new Random();
            chosen = chosen.OrderBy(_ => rng.Next()).ToList();
        }

        foreach (var part in chosen)
            _parts.Add(new ExamPartViewModel(part));

        IsRunning = true;
        IsFinished = false;
        ShowReview = false;
        ReviewLines.Clear();
        AiFeedbackLines.Clear();
        OnPropertyChanged(nameof(HasAiFeedback));
        OnPropertyChanged(nameof(Parts));
        ResultText = string.Empty;
        BandLabel = string.Empty;
        StatusMessage = StrictMode
            ? "Strict mode is on. Full screen, no other apps, finish the test."
            : "Test started. Answer every part, then submit.";
        SetPart(0);
        _timer.Start();
    }

    [RelayCommand]
    private void NextPart()
    {
        if (CurrentPart?.IsSpeaking == true) return;
        if (PartIndex < _parts.Count - 1)
            SetPart(PartIndex + 1);
    }

    [RelayCommand]
    private void PreviousPart()
    {
        if (CurrentPart?.IsSpeaking == true) return;
        if (PartIndex > 0)
            SetPart(PartIndex - 1);
    }

    [RelayCommand]
    private void GoToPart(ExamPartViewModel part)
    {
        if (!IsRunning || part is null) return;
        if (CurrentPart?.IsSpeaking == true) return;
        int index = _parts.IndexOf(part);
        if (index >= 0)
            SetPart(index);
    }

    /// <summary>
    /// Plays the Listening clip exactly once when its part opens, like the
    /// real test. Uses the shipped audio file when present, otherwise the
    /// Windows voice reads the transcript, so a part never stays silent.
    /// </summary>
    private async Task PlayListeningOnceAsync(ExamPartViewModel part, CancellationToken ct)
    {
        if (part.AudioPlayedOnce) return;
        part.AudioPlayedOnce = true;

        string? path = string.IsNullOrWhiteSpace(part.AudioFile)
            ? null
            : ResolveAudio(part.AudioFile);
        if (path is not null && !File.Exists(path))
            path = null;

        if (path is null)
        {
            if (!_tts.IsAvailable || string.IsNullOrWhiteSpace(part.Material))
            {
                part.AudioStatus = "No audio for this part. Read the transcript and answer.";
                return;
            }
            part.AudioStatus = "Reading the transcript aloud.";
            path = await _tts.SpeakToFileAsync(part.Material, ct);
            if (string.IsNullOrWhiteSpace(path))
            {
                part.AudioStatus = "Voice playback failed. Read the transcript and answer.";
                return;
            }
        }

        try
        {
            part.AudioStatus = "Playing. The clip plays once, like the real test.";
            _audio.Volume = Volume / 100.0;
            await _audio.PlayAsync(path, ct);
            if (!ct.IsCancellationRequested)
                part.AudioStatus = "Clip finished. It does not replay.";
        }
        catch (OperationCanceledException)
        {
            part.AudioStatus = "Clip stopped.";
        }
        catch (Exception)
        {
            part.AudioStatus = "Could not play this clip. Check your speakers.";
        }
    }

    [RelayCommand]
    private async Task RecordSpeakingAsync()
    {
        if (CurrentPart is null || !CurrentPart.IsSpeaking || CurrentPart.IsRecording) return;
        _speakingCts?.Cancel();
        _speakingCts = new CancellationTokenSource();
        CurrentPart.IsRecording = true;
        CurrentPart.AudioStatus = $"Recording for {CurrentPart.SpeakingSeconds} seconds. Speak now, no pause.";
        try
        {
            var wav = await _audio.RecordAsync(CurrentPart.SpeakingSeconds, _speakingCts.Token);
            if (string.IsNullOrWhiteSpace(wav))
            {
                CurrentPart.AudioStatus = "No audio was captured. Check your microphone.";
                StatusMessage = "No audio was captured.";
                return;
            }
            if (_stt.IsAvailable())
            {
                CurrentPart.AudioStatus = "Transcribing your speech.";
                StatusMessage = "The transcription model is reading your speech.";
                var transcript = await _stt.TranscribeAsync(wav, _speakingCts.Token);
                if (transcript.Success && !string.IsNullOrWhiteSpace(transcript.Text))
                {
                    CurrentPart.Transcript = transcript.Text;
                    CurrentPart.AudioStatus = "Transcript ready. Fix any wrong words below, then continue.";
                    StatusMessage = "Transcript ready. Fix any wrong words, then continue.";
                }
                else
                {
                    CurrentPart.AudioStatus = "Saved recording. Type what you said below so AI marking can work. File kept locally.";
                    StatusMessage = "Recording done. Type your transcript, then continue.";
                }
            }
            else
            {
                CurrentPart.AudioStatus = $"Saved recording. Type what you said below so AI marking can work. File kept locally.";
                StatusMessage = "Recording done. Type your transcript, then continue.";
            }
        }
        catch (OperationCanceledException)
        {
            CurrentPart.AudioStatus = "Stopped.";
        }
        catch (Exception)
        {
            CurrentPart.AudioStatus = "Recording failed. Check your microphone and try again.";
        }
        finally
        {
            CurrentPart.IsRecording = false;
        }
    }

    [RelayCommand]
    private void FinishSpeakingPart()
    {
        if (CurrentPart is null || !CurrentPart.IsSpeaking) return;
        _speakingCts?.Cancel();
        _audio.StopRecording();
        if (PartIndex < _parts.Count - 1)
            SetPart(PartIndex + 1);
        else
            SubmitExam();
    }

    [RelayCommand]
    private async Task GradeWithAiAsync()
    {
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings to get AI bands.";
            return;
        }
        if (_parts.Count == 0)
        {
            StatusMessage = "Start a test first.";
            return;
        }
        IsGrading = true;
        AiFeedbackLines.Clear();
        StatusMessage = $"The model is marking ({IeltsBanding.StrictnessLabel(SelectedStrictness)}).";
        try
        {
            foreach (var part in _parts.Where(p => p.IsWriting))
            {
                if (string.IsNullOrWhiteSpace(part.Essay))
                {
                    AiFeedbackLines.Add($"{part.Title}: no essay, skipped.");
                    continue;
                }
                var r = await _ai.ReviewWritingAsync(
                    part.Material + "\n" + part.Instructions, part.Essay, 150, SelectedStrictness);
                if (!r.Success || r.Feedback is null)
                {
                    AiFeedbackLines.Add($"{part.Title}: {r.Error}");
                    continue;
                }
                var f = r.Feedback;
                part.AiResult = $"Band {f.BandLabel}. {f.Summary}";
                AiFeedbackLines.Add($"{part.Title}: band {f.BandLabel} " +
                    $"(Task {f.TaskResponse:0.0}, Cohesion {f.Coherence:0.0}, Words {f.LexicalResource:0.0}, Grammar {f.Grammar:0.0}). {f.Summary}");
                foreach (var s in f.Strengths.Take(2)) AiFeedbackLines.Add($"  Good: {s}");
                foreach (var s in f.Improvements.Take(3)) AiFeedbackLines.Add($"  Fix: {s}");
                if (!string.IsNullOrWhiteSpace(f.CorrectedExcerpt))
                    AiFeedbackLines.Add($"  Rewrite: {f.CorrectedExcerpt}");
            }
            foreach (var part in _parts.Where(p => p.IsSpeaking))
            {
                if (string.IsNullOrWhiteSpace(part.Transcript))
                {
                    AiFeedbackLines.Add($"{part.Title}: no transcript, skipped. Type what you said to get a band.");
                    continue;
                }
                var r = await _ai.AssessSpeakingAsync(part.Material + "\n" + part.Title, part.Transcript, SelectedStrictness);
                if (!r.Success || r.Feedback is null)
                {
                    AiFeedbackLines.Add($"{part.Title}: {r.Error}");
                    continue;
                }
                var f = r.Feedback;
                part.AiResult = $"Band {f.BandLabel}. {f.Summary}";
                AiFeedbackLines.Add($"{part.Title}: band {f.BandLabel} " +
                    $"(Fluency {f.Fluency:0.0}, Words {f.LexicalResource:0.0}, Grammar {f.Grammar:0.0}, Sound {f.Pronunciation:0.0}). {f.Summary}");
                foreach (var s in f.Improvements.Take(3)) AiFeedbackLines.Add($"  Fix: {s}");
            }
            foreach (var part in _parts.Where(p => !p.IsListening && !p.IsWriting && !p.IsSpeaking))
            {
                // Reading mistakes get a short AI explanation each.
                foreach (var q in part.Questions.Where(q => !q.IsCorrect))
                {
                    var r = await _ai.ExplainReadingAsync(
                        part.Material, $"Q{q.Model.Number}: {q.Prompt}", q.SelectedKey, q.Model.CorrectKey);
                    AiFeedbackLines.Add(r.Success
                        ? $"{part.Title}, question {q.Model.Number}: {OneLine(r.Text)}"
                        : $"{part.Title}, question {q.Model.Number}: {r.Error}");
                }
            }
            OnPropertyChanged(nameof(HasAiFeedback));
            StatusMessage = AiFeedbackLines.Count == 0
                ? "Nothing to mark. Write an essay or type a speaking transcript first."
                : "AI marking done. Bands are ranges for practice, not official scores.";
        }
        finally
        {
            IsGrading = false;
        }
    }

    /// <summary>
    /// Grounds the LLM grammar band with a deterministic grammar check, so
    /// generous AI marking cannot hide real errors. The grammar band is
    /// capped by measured errors per 100 words, then the overall band is
    /// recomputed from the four criteria like an examiner would.
    /// </summary>
    private async Task<WritingFeedback> ApplyGrammarCheckAsync(
        string title, string essay, WritingFeedback f)
    {
        if (!_gec.IsAvailable())
            return f;

        StatusMessage = $"Checking grammar in {title}.";
        GecResult? g = null;
        try
        {
            g = await _gec.CheckAsync(essay);
        }
        catch (OperationCanceledException)
        {
            return f;
        }
        catch (Exception)
        {
            AiFeedbackLines.Add($"{title}: grammar check skipped.");
            return f;
        }
        if (!g.Success)
        {
            AiFeedbackLines.Add($"{title}: grammar check skipped.");
            return f;
        }

        double cap = g.ErrorsPer100Words switch
        {
            > 10 => 4.5,
            > 6 => 5.5,
            > 3 => 6.5,
            > 1 => 7.5,
            _ => 9.0
        };
        double grammar = Math.Min(f.Grammar, cap);
        double overall = IeltsBanding.RoundHalf(
            (f.TaskResponse + f.Coherence + f.LexicalResource + grammar) / 4.0);
        var range = IeltsBanding.ToRange(overall, SelectedStrictness);

        AiFeedbackLines.Add($"{title}: grammar check found {g.ErrorCount} errors " +
            $"({g.ErrorsPer100Words:0.0} per 100 words), grammar capped at {grammar:0.0}.");
        if (g.Edits.Count > 0)
            AiFeedbackLines.Add($"  Example: '{OneLine(g.Edits[0].Original)}' becomes '{OneLine(g.Edits[0].Corrected)}'.");

        return new WritingFeedback
        {
            EstimatedBand = overall,
            BandLow = range.Low,
            BandHigh = range.High,
            TaskResponse = f.TaskResponse,
            Coherence = f.Coherence,
            LexicalResource = f.LexicalResource,
            Grammar = grammar,
            Summary = f.Summary,
            Strengths = f.Strengths,
            Improvements = f.Improvements,
            CorrectedExcerpt = f.CorrectedExcerpt
        };
    }

    [RelayCommand]
    private async Task SuggestTopicAsync()
    {
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings for AI topics. Or enable Shuffle for a random order.";
            return;
        }
        StatusMessage = "Asking the model for a topic.";
        try
        {
            var skill = SelectedScope == "Full test" ? "Writing" : SelectedScope;
            var r = await _ai.SuggestTopicAsync(skill);
            StatusMessage = r.Success ? $"Topic idea: {OneLine(r.Text)}" : r.Error;
        }
        catch (Exception)
        {
            StatusMessage = "Could not reach the model. Check Settings.";
        }
    }

    [RelayCommand]
    private void SubmitExam()
    {
        _timer.Stop();
        _speakingCts?.Cancel();
        _listeningCts?.Cancel();
        _audio.StopPlayback();
        _audio.StopRecording();
        IsRunning = false;
        IsFinished = true;
        ShowReview = false;
        ReviewLines.Clear();

        int correct = 0, total = 0;
        foreach (var part in _parts)
        {
            correct += part.CorrectCount;
            total += part.ScoredCount;
        }

        double mid = total == 0 ? 0 : IeltsBanding.RawToBand(correct, total);
        var range = IeltsBanding.ToRange(mid, SelectedStrictness);
        BandLabel = total == 0 ? string.Empty : range.Label;

        ResultText = total == 0
            ? "This test has no multiple choice questions to score. " +
              "Use AI marking for Writing and Speaking, or ask a teacher."
            : $"Score {correct} of {total}. Objective band {range.Label} " +
              $"({IeltsBanding.StrictnessLabel(SelectedStrictness)}). " +
              "Writing and Speaking need AI marking or a teacher. " +
              "All bands are practice estimates, not official IELTS scores.";

        SaveAttempt(correct, total, range);
        OnPropertyChanged(nameof(HasBand));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private void ToggleReview()
    {
        ShowReview = !ShowReview;
        if (!ShowReview) return;

        ReviewLines.Clear();
        foreach (var part in _parts)
        {
            if (part.IsWriting)
            {
                ReviewLines.Add($"{part.Title}: {part.WordCountLabel} written, not auto scored.");
                continue;
            }
            if (part.IsSpeaking)
            {
                ReviewLines.Add($"{part.Title}: {part.TranscriptWordCount}, not auto scored.");
                continue;
            }
            if (part.IsListening)
            {
                // Listening gives no answers back, only the wrong questions.
                var wrong = part.Questions.Where(q => !q.IsCorrect).ToList();
                if (wrong.Count == 0)
                    ReviewLines.Add($"{part.Title}: all correct.");
                foreach (var q in wrong)
                    ReviewLines.Add($"{part.Title}, question {q.Model.Number}: {q.ListeningReviewLabel}");
                continue;
            }
            foreach (var q in part.Questions)
                ReviewLines.Add($"{part.Title}, question {q.Model.Number}: {q.ReadingReviewLabel}");
        }
    }

    private void SaveAttempt(int correct, int total, BandRange range)
    {
        try
        {
            using var db = new AppDbContext();
            db.ExamAttempts.Add(new ExamAttempt
            {
                PaperTitle = SelectedPaper?.Title ?? SelectedScope,
                Scope = SelectedScope,
                Strictness = IeltsBanding.StrictnessLabel(SelectedStrictness),
                BandLow = range.Low,
                BandHigh = range.High,
                Correct = correct,
                Total = total,
                Summary = ResultText.Length > 500 ? ResultText[..500] : ResultText
            });
            db.SaveChanges();
        }
        catch
        {
            // History must never block the result screen.
        }
    }

    private static string ResolveAudio(string file)
    {
        if (Path.IsPathRooted(file) && File.Exists(file)) return file;
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Audio", file);
        if (File.Exists(userPath)) return userPath;
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", file);
    }

    private static string OneLine(string text)
    {
        var clean = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length <= 220 ? clean : clean[..220] + "...";
    }

    private void SetPart(int index)
    {
        _listeningCts?.Cancel();
        _audio.StopPlayback();
        PartIndex = index;
        CurrentPart = _parts[index];
        CurrentPart.IsTimerRunning = true;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        if (CurrentPart.IsListening)
        {
            _listeningCts = new CancellationTokenSource();
            _ = PlayListeningOnceAsync(CurrentPart, _listeningCts.Token);
        }
    }

    private void Tick()
    {
        if (CurrentPart is null) { _timer.Stop(); return; }
        CurrentPart.Tick();
        if (CurrentPart.RemainingSeconds > 0) return;

        CurrentPart.IsTimerRunning = false;
        if (PartIndex < _parts.Count - 1)
        {
            SetPart(PartIndex + 1);
            return;
        }

        SubmitExam();
    }
}
