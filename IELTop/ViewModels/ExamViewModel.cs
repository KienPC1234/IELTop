using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using IELTop.Services.Storage;
using Microsoft.EntityFrameworkCore;

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
    public string GroupName => $"{_question.GroupPrefix}_{_question.Model.Number}";

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
/// One row of a matching question: a label with a gap filled from the bank
/// by drag and drop. Answered when a bank item sits in the gap.
/// </summary>
public sealed partial class MatchRowViewModel : ObservableObject
{
    public string Label { get; }
    public string Answer { get; }

    [ObservableProperty] private string _selected = string.Empty;

    public MatchRowViewModel(ExamMatchRow row)
    {
        Label = row.Label;
        Answer = row.Answer;
    }

    public bool IsCorrect => Norm(Selected) == Norm(Answer) && Selected.Length > 0;

    private static string Norm(string text)
    {
        var parts = text.Trim().ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }
}

/// <summary>
/// One question as presented during the test, with the student's choice.
/// </summary>
public sealed partial class ExamQuestionViewModel : ObservableObject
{
    public ExamQuestion Model { get; }
    public int Index { get; }
    public string GroupPrefix { get; }

    [ObservableProperty] private string _selectedKey = string.Empty;
    [ObservableProperty] private string _answer = string.Empty;
    [ObservableProperty] private bool _isFlagged;
    [ObservableProperty] private bool _isFocused;

    public ObservableCollection<ExamOptionViewModel> Options { get; } = new();
    public ObservableCollection<MatchRowViewModel> MatchRows { get; } = new();

    public IReadOnlyList<string> Bank => Model.Bank;

    public string NumberLabel => $"Question {Model.Number}";
    public string Prompt => Model.Prompt;
    public string Explanation => Model.Explanation;
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Model.Explanation);
    public string FlagLabel => IsFlagged ? "Marked for review" : "Review";
    public bool IsGap => string.Equals(Model.Kind, "gap", StringComparison.OrdinalIgnoreCase);
    public bool IsMatch => string.Equals(Model.Kind, "match", StringComparison.OrdinalIgnoreCase);
    public bool ShowOptions => !IsGap && !IsMatch;
    public bool IsAnswered => IsMatch
        ? MatchRows.Any(r => !string.IsNullOrWhiteSpace(r.Selected))
        : IsGap ? !string.IsNullOrWhiteSpace(Answer) : !string.IsNullOrEmpty(SelectedKey);

    /// <summary>First accepted answer, shown in Reading review.</summary>
    public string DisplayAnswer => IsGap ? FirstGapAnswer : Model.CorrectKey;

    private string FirstGapAnswer =>
        Model.GapAnswer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

    public ExamQuestionViewModel(ExamQuestion model, int index, string groupPrefix)
    {
        Model = model;
        Index = index;
        GroupPrefix = groupPrefix;
        foreach (var option in model.Options)
            Options.Add(new ExamOptionViewModel(option, this));
        foreach (var row in model.MatchRows)
        {
            var rowVm = new MatchRowViewModel(row);
            rowVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MatchRowViewModel.Selected))
                    OnPropertyChanged(nameof(IsAnswered));
            };
            MatchRows.Add(rowVm);
        }
    }

    partial void OnSelectedKeyChanged(string value)
    {
        foreach (var option in Options)
            option.IsSelected = option.Key == value;
        OnPropertyChanged(nameof(IsAnswered));
    }

    partial void OnIsFlaggedChanged(bool value) => OnPropertyChanged(nameof(FlagLabel));

    partial void OnAnswerChanged(string value) => OnPropertyChanged(nameof(IsAnswered));

    public bool IsCorrect => IsMatch
        ? MatchRows.Count > 0 && MatchRows.All(r => r.IsCorrect)
        : IsGap ? MatchesGap(Answer) : SelectedKey == Model.CorrectKey;

    /// <summary>
    /// Gap answers ignore case and extra spaces. Alternatives split by |.
    /// </summary>
    private bool MatchesGap(string answer)
    {
        var given = NormGap(answer);
        if (given.Length == 0) return false;
        return Model.GapAnswer
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => NormGap(a) == given);
    }

    private static string NormGap(string text)
    {
        var parts = text.Trim().ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    public string FlagSuffix => IsFlagged ? " (flagged)" : string.Empty;

    public string ChosenLabel => IsMatch
        ? string.Join("; ", MatchRows.Select(r => $"{r.Label}={r.Selected.Trim()}"))
        : IsGap ? Answer.Trim() : SelectedKey;

    public string ReviewLabel => string.IsNullOrWhiteSpace(ChosenLabel)
        ? $"no answer, correct is {DisplayAnswer}"
        : IsCorrect ? "correct" : $"you chose {ChosenLabel}, correct is {DisplayAnswer}";

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

/// <summary>One paper row with its origin for the manage list.</summary>
public sealed partial class PaperRow : ObservableObject
{
    public string Title { get; }
    public string Detail { get; }
    public bool IsUserPaper { get; }
    public string Category { get; }
    public string Level { get; }
    public string Skills { get; }
    public int Parts { get; }
    public int Questions { get; }
    public int Minutes { get; }

    public PaperRow(ExamPaper paper, bool isUserPaper)
    {
        Title = paper.Title;
        Skills = string.Join(", ", paper.Parts
            .Select(p => p.Skill).Distinct(StringComparer.OrdinalIgnoreCase));
        Parts = paper.Parts.Count;
        Questions = paper.Parts.Sum(p => p.Questions.Count);
        Minutes = paper.Parts.Sum(p => p.Minutes);
        Category = paper.Category;
        Level = paper.Level;
        Detail = $"{Parts} parts, {Questions} questions, about {Minutes} min. {Skills}. " +
            (isUserPaper ? "Yours." : "Built in.");
        IsUserPaper = isUserPaper;
    }
}

/// <summary>Compares a server date against a local file date. Pure logic.</summary>
public static class UpdateChecker
{
    public static bool IsNewer(string? serverDate, DateTime localDate)
    {
        if (string.IsNullOrWhiteSpace(serverDate)) return false;
        if (!DateTime.TryParse(serverDate, out var server)) return false;
        return server.Date > localDate.Date;
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
    [NotifyPropertyChangedFor(nameof(IsLowTime))]
    [NotifyPropertyChangedFor(nameof(IsCriticalTime))]
    private int _remainingSeconds;
    [ObservableProperty] private bool _isTimerRunning;
    [ObservableProperty] private string _essay = string.Empty;
    [ObservableProperty] private string _transcript = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _audioStatus = string.Empty;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _aiResult = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStartNow))]
    private bool _audioPlayedOnce;
    [ObservableProperty] private bool _noAudioFallback;
    [ObservableProperty] private int _focusedIndex;
    [ObservableProperty] private int _spokenSeconds;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _timerHidden;
    [ObservableProperty] private bool _notesOpen;
    [ObservableProperty] private bool _contrastOn;
    [ObservableProperty] private string _selectedMaterialWord = string.Empty;

    /// <summary>Owning paper, set when a test mixes parts from many papers.</summary>
    public string PaperName { get; set; } = string.Empty;

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
    public bool IsReading => string.Equals(Skill, "Reading", StringComparison.OrdinalIgnoreCase);
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioFile);
    public bool ShowStartNow => IsListening && !AudioPlayedOnce;

    // Writing Task 1 often comes with a chart or diagram. The paper points
    // to a file name, the same way Listening points to its audio clip.
    public bool IsWritingTask1 => IsWriting
        && (TaskType.Contains("Task 1", StringComparison.OrdinalIgnoreCase)
            || Title.Contains("Task 1", StringComparison.OrdinalIgnoreCase));
    public bool HasWritingTask1Image => IsWritingTask1 && !string.IsNullOrWhiteSpace(WritingImageName);
    public string WritingImageName => Model.ImageFile;
    public string WritingImagePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WritingImageName)) return string.Empty;
            var userPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IELTop", "content", "Images", WritingImageName);
            if (File.Exists(userPath)) return userPath;
            return Path.Combine(AppContext.BaseDirectory, "Assets", "Images", WritingImageName);
        }
    }
    public bool HasWritingImageFile => HasWritingTask1Image && File.Exists(WritingImagePath);
    public bool ShowMissingImageHint => HasWritingTask1Image && !HasWritingImageFile;
    public bool ShowNoImageNote => IsWritingTask1 && !HasWritingTask1Image;
    public string ImageHint => HasWritingTask1Image
        ? $"Chart image: {WritingImageName}"
        : "Task 1 chart: paste or read the data from the question, no image is attached.";

    public string HeaderLine
    {
        get
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(PaperName)) bits.Add(PaperName);
            bits.Add(Skill);
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

    // Speaking helpers, so the page can guide the student step by step.
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);
    public bool IsPrepPhase => IsSpeaking && Model.PrepSeconds > 0 && !AudioPlayedOnce && !IsRecording;
    public string SpeakingCue => Model.Material;
    public string RecordingHint => $"Speak for about {SpeakingSeconds} seconds, then the recording stops on its own.";
    public string SpeakingStepLabel
    {
        get
        {
            if (!IsSpeaking) return string.Empty;
            if (IsRecording) return "Step 3: Recording. Speak now, no pause.";
            if (HasTranscript) return "Step 4: Check your transcript, then finish the part.";
            return Model.PrepSeconds > 0
                ? "Step 1: Read the cue card. Step 2: Record when you are ready."
                : "Step 1: Read the question. Step 2: Record your answer.";
        }
    }

    public ExamPartViewModel(ExamPart model)
    {
        Model = model;
        RemainingSeconds = Math.Max(60, model.Minutes * 60);
        string prefix = Guid.NewGuid().ToString("N");
        int index = 0;
        foreach (var q in model.Questions)
            Questions.Add(new ExamQuestionViewModel(q, index++, prefix));
        if (Questions.Count > 0)
            Questions[0].IsFocused = true;
    }

    partial void OnEssayChanged(string value) => OnPropertyChanged(nameof(WordCountLabel));

    partial void OnTranscriptChanged(string value)
    {
        OnPropertyChanged(nameof(TranscriptWordCount));
        OnPropertyChanged(nameof(HasTranscript));
        OnPropertyChanged(nameof(SpeakingStepLabel));
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(SpeakingStepLabel));
        OnPropertyChanged(nameof(IsPrepPhase));
    }

    public void Tick()
    {
        if (RemainingSeconds > 0)
            RemainingSeconds--;
        else
            IsTimerRunning = false;
    }

    public void ToggleTimer() => TimerHidden = !TimerHidden;

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
    public int AnsweredCount => Questions.Count(q => q.IsAnswered);
    public int FlaggedCount => Questions.Count(q => q.IsFlagged);

    public string ProgressLabel => HasQuestions
        ? $"Answered {AnsweredCount} of {ScoredCount}, marked for review {FlaggedCount}."
        : string.Empty;

    /// <summary>Orange under 10 minutes, like the real test warning.</summary>
    public bool IsLowTime => RemainingSeconds <= 600;

    /// <summary>Red under 5 minutes, like the real test warning.</summary>
    public bool IsCriticalTime => RemainingSeconds <= 300;

    public ExamQuestionViewModel? FocusedQuestion =>
        Questions.Count == 0 ? null : Questions[Math.Clamp(FocusedIndex, 0, Questions.Count - 1)];

    partial void OnFocusedIndexChanged(int value)
    {
        for (int i = 0; i < Questions.Count; i++)
            Questions[i].IsFocused = i == value;
        OnPropertyChanged(nameof(FocusedQuestion));
        OnPropertyChanged(nameof(CanQuestionBack));
        OnPropertyChanged(nameof(CanQuestionNext));
    }

    public bool CanQuestionBack => FocusedIndex > 0;
    public bool CanQuestionNext => FocusedIndex < Questions.Count - 1;

    public void GoToQuestion(ExamQuestionViewModel question)
    {
        int index = Questions.IndexOf(question);
        if (index >= 0)
            FocusedIndex = index;
    }

    public void NextQuestion()
    {
        if (FocusedIndex < Questions.Count - 1)
            FocusedIndex++;
    }

    public void PreviousQuestion()
    {
        if (FocusedIndex > 0)
            FocusedIndex--;
    }

    public void RefreshProgress() => OnPropertyChanged(nameof(ProgressLabel));

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
/// <summary>One review line with a verdict for its icon.</summary>
public sealed record ReviewItem(string Text, bool? IsGood);

/// <summary>A checkbox option in the test setup, for skills and task types.</summary>
public sealed partial class SelectableOption : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Name { get; }
    public int PartCount { get; }

    public SelectableOption(string name, bool selected, int partCount)
    {
        Name = name;
        IsSelected = selected;
        PartCount = partCount;
    }

    public string CountLabel => PartCount > 0 ? $"{PartCount} part(s)" : "none in this paper";
    public bool HasParts => PartCount > 0;
}

public sealed partial class ExamViewModel : ObservableObject
{
    private readonly IExamRepository _repository;
    private readonly IIeltsAiService _ai;
    private readonly IAudioService _audio;
    private readonly ISttService _stt;
    private readonly IGecService _gec;
    private readonly IModelLoadCoordinator _models;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly List<ExamPartViewModel> _parts = new();
    private CancellationTokenSource? _speakingCts;
    private CancellationTokenSource? _listeningCts;
    private CancellationTokenSource? _prepCts;
    private CancellationTokenSource? _gradingCts;

    [ObservableProperty] private ExamPaper? _selectedPaper;
    [ObservableProperty] private ExamPartViewModel? _currentPart;
    [ObservableProperty] private int _partIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    private bool _isRunning;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    private string _resultText = string.Empty;
    [ObservableProperty] private string _bandLabel = string.Empty;
    [ObservableProperty] private bool _showReview;
    [ObservableProperty] private string _statusMessage = "Pick a paper, a scope, and a marking level, then start.";
    [ObservableProperty] private string _selectedScope = "Full test";
    [ObservableProperty] private string _selectedTaskType = "All types";
    [ObservableProperty] private MarkingStrictness _selectedStrictness = MarkingStrictness.Standard;
    [ObservableProperty] private bool _strictMode;
    [ObservableProperty] private bool _shuffleParts;
    [ObservableProperty] private bool _mixAllPapers;
    [ObservableProperty] private string _selectedBuildMode = "Paper order";
    [ObservableProperty] private bool _isGrading;
    [ObservableProperty] private string _loadingLabel = string.Empty;
    [ObservableProperty] private string _modelModeLabel = string.Empty;
    [ObservableProperty] private double _volume = 80;
    [ObservableProperty] private int _strictViolations;
    [ObservableProperty] private double _fontScale = 1.0;
    [ObservableProperty] private bool _isFullscreen;

    public ObservableCollection<ExamPaper> Papers { get; } = new();
    public ObservableCollection<PaperRow> PaperRows { get; } = new();
    public ObservableCollection<ReviewItem> ReviewItems { get; } = new();
    public ObservableCollection<string> AiFeedbackLines { get; } = new();
    public ObservableCollection<string> TaskTypes { get; } = new();

    /// <summary>Skill checkboxes for the setup. One skill can run alone.</summary>
    public ObservableCollection<SelectableOption> SkillOptions { get; } = new();

    /// <summary>Task type checkboxes, filled from the chosen paper(s).</summary>
    public ObservableCollection<SelectableOption> TaskTypeOptions { get; } = new();

    private bool _syncingOptions;

    /// <summary>Raised after papers change, so the Library list can reload.</summary>
    public Action? PapersChanged { get; set; }

    // Listening audio cannot be split, so it only runs inside a full test.
    // Reading, Writing, and Speaking can each run alone.
    public IReadOnlyList<string> Scopes { get; } = new[] { "Full test", "Reading", "Writing", "Speaking" };
    public IReadOnlyList<MarkingStrictness> StrictnessOptions { get; } =
        new[] { MarkingStrictness.Lenient, MarkingStrictness.Standard, MarkingStrictness.Strict };

    public IReadOnlyList<string> BuildModes { get; } =
        new[] { "Paper order", "Random", "AI pick" };

    public bool HasViolations => StrictViolations > 0;
    public string ViolationLabel => $"Stay in the test. Focus left {StrictViolations} time(s).";

    /// <summary>Questions with no answer yet, for the submit warning.</summary>
    public int UnansweredCount => _parts.Sum(p => p.Questions.Count(q => !q.IsAnswered));

    public ExamViewModel(IExamRepository repository, IIeltsAiService ai, IAudioService audio, ISttService stt, IGecService gec, IModelLoadCoordinator models)
    {
        _repository = repository;
        _ai = ai;
        _audio = audio;
        _stt = stt;
        _gec = gec;
        _models = models;
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

    /// <summary>Summary of the checked skills, shown next to Start test.</summary>
    public string SelectedSkillsLabel
    {
        get
        {
            var picked = SkillOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
            if (picked.Count == 0) return "No skill selected. Tick at least one.";
            if (picked.Count == SkillOptions.Count) return "Full test, all four skills.";
            return string.Join(" + ", picked);
        }
    }

    public string SelectedTaskTypesLabel
    {
        get
        {
            var all = TaskTypeOptions.Count;
            var picked = TaskTypeOptions.Count(o => o.IsSelected);
            if (all == 0) return "No task type filter.";
            if (picked == 0 || picked == all) return "All task types.";
            return $"{picked} of {all} task types.";
        }
    }

    /// <summary>True when the chosen skills can actually start a test.</summary>
    public bool CanStartSetup => SkillOptions.Any(o => o.IsSelected && o.HasParts);

    public string MaterialWarning =>
        $"No test papers found. Add a .json paper under {_repository.ExamsDir}.";

    public string StrictModeHint => StrictMode
        ? "Strict mode is on. Full screen, stay on this test."
        : "Strict mode off. Turn it on for a real exam feel.";

    public string CriteriaHint =>
        "Listening and Reading use the official raw to band table. " +
        "Writing and Speaking average four criteria to half bands. " +
        "Grade with AI also explains each wrong Listening and Reading answer. " +
        "Bands are ranges, because examiners vary. Practice estimates only.";

    partial void OnStrictModeChanged(bool value) => OnPropertyChanged(nameof(StrictModeHint));

    partial void OnStrictViolationsChanged(int value) => OnPropertyChanged(nameof(HasViolations));

    partial void OnVolumeChanged(double value) => _audio.Volume = value / 100.0;

    public IReadOnlyList<ExamPartViewModel> Parts => _parts;

    public void Load()
    {
        Papers.Clear();
        foreach (var paper in _repository.LoadPapers())
            Papers.Add(paper);
        SelectedPaper ??= Papers.FirstOrDefault();

        RebuildSkillOptions();
        RebuildTaskTypes();
        RebuildPaperRows();
        OnPropertyChanged(nameof(HasPapers));
        OnPropertyChanged(nameof(ShowNoPaperWarning));
        OnPropertyChanged(nameof(PaperCountLabel));
        OnPropertyChanged(nameof(CanStartSetup));
    }

    /// <summary>Every paper with its origin, so downloaded ones can be deleted.</summary>
    private void RebuildPaperRows()
    {
        PaperRows.Clear();
        foreach (var paper in Papers)
            PaperRows.Add(new PaperRow(paper, _repository.IsUserPaper(paper.Title)));
    }

    [RelayCommand]
    private void DeletePaper(PaperRow? row)
    {
        if (row is null || !row.IsUserPaper) return;
        var ask = System.Windows.MessageBox.Show(
            $"Delete {row.Title} from this computer?",
            "Delete paper", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (ask != System.Windows.MessageBoxResult.Yes)
            return;
        if (_repository.DeleteUserPaper(row.Title))
        {
            if (SelectedPaper is not null && string.Equals(
                SelectedPaper.Title, row.Title, StringComparison.OrdinalIgnoreCase))
                SelectedPaper = null;
            Load();
            PapersChanged?.Invoke();
            StatusMessage = $"Deleted {row.Title}.";
        }
        else
        {
            StatusMessage = "Could not delete that paper.";
        }
    }

    /// <summary>Picks one paper from the Your papers list for the setup above.</summary>
    [RelayCommand]
    private void UsePaper(PaperRow? row)
    {
        if (row is null) return;
        var paper = Papers.FirstOrDefault(p =>
            string.Equals(p.Title, row.Title, StringComparison.OrdinalIgnoreCase));
        if (paper is null)
        {
            StatusMessage = "That paper is gone. The list will refresh.";
            Load();
            return;
        }
        SelectedPaper = paper;
        StatusMessage = $"Using {paper.Title}. Tick a skill and press Start test.";
    }

    partial void OnSelectedPaperChanged(ExamPaper? value) => RebuildTaskTypes();
    partial void OnMixAllPapersChanged(bool value) => RebuildTaskTypes();

    /// <summary>
    /// Fills the skill checkboxes with the part count of the chosen paper(s),
    /// so the user sees which skills have work. Keeps current ticks when the
    /// skill still exists, so switching paper stays predictable.
    /// </summary>
    private void RebuildSkillOptions()
    {
        var source = MixAllPapers ? Papers.SelectMany(p => p.Parts) : SelectedPaper?.Parts ?? Enumerable.Empty<ExamPart>();
        var counts = source
            .Where(p => !string.IsNullOrWhiteSpace(p.Skill))
            .GroupBy(p => p.Skill, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var previous = SkillOptions.Where(o => o.IsSelected).Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool firstFill = SkillOptions.Count == 0;

        _syncingOptions = true;
        try
        {
            SkillOptions.Clear();
            foreach (var skill in new[] { "Listening", "Reading", "Writing", "Speaking" })
            {
                counts.TryGetValue(skill, out int count);
                // Listening cannot run alone, so never tick it by itself.
                bool selected = firstFill
                    ? skill != "Listening" && count > 0
                    : previous.Contains(skill) && count > 0;
                SkillOptions.Add(new SelectableOption(skill, selected, count));
            }
        }
        finally
        {
            _syncingOptions = false;
        }
        foreach (var option in SkillOptions)
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectableOption.IsSelected)) OnSkillToggled();
            };
        OnPropertyChanged(nameof(SelectedSkillsLabel));
        OnPropertyChanged(nameof(CanStartSetup));
    }

    /// <summary>Every task type on the candidate papers, for the type filter.</summary>
    private void RebuildTaskTypes()
    {
        var previous = TaskTypeOptions.Where(o => o.IsSelected).Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool firstFill = TaskTypeOptions.Count == 0;

        var types = (MixAllPapers ? Papers.SelectMany(p => p.Parts) : SelectedPaper?.Parts ?? Enumerable.Empty<ExamPart>())
            .Where(p => SkillOptions.Count == 0 || SkillOptions.Any(o => o.IsSelected
                && string.Equals(o.Name, p.Skill, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.TaskType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _syncingOptions = true;
        try
        {
            TaskTypeOptions.Clear();
            foreach (var type in types)
            {
                bool selected = firstFill || previous.Count == 0 || previous.Contains(type);
                TaskTypeOptions.Add(new SelectableOption(type, selected, 0));
            }
        }
        finally
        {
            _syncingOptions = false;
        }

        // Legacy single-select list stays in sync for any remaining binding.
        var keep = SelectedTaskType;
        TaskTypes.Clear();
        TaskTypes.Add("All types");
        foreach (var type in types) TaskTypes.Add(type);

        OnPropertyChanged(nameof(SelectedTaskTypesLabel));
        RebuildSkillOptions();
    }

    /// <summary>Called when any skill checkbox flips, to refresh the counts.</summary>
    private void OnSkillToggled()
    {
        if (_syncingOptions) return;
        OnPropertyChanged(nameof(SelectedSkillsLabel));
        OnPropertyChanged(nameof(CanStartSetup));
        // Task types follow the chosen skills, so the list stays relevant.
        RebuildTaskTypeOptionsKeepingTicks();
    }

    private void RebuildTaskTypeOptionsKeepingTicks()
    {
        var types = (MixAllPapers ? Papers.SelectMany(p => p.Parts) : SelectedPaper?.Parts ?? Enumerable.Empty<ExamPart>())
            .Where(p => SkillOptions.Count == 0 || SkillOptions.Any(o => o.IsSelected
                && string.Equals(o.Name, p.Skill, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.TaskType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _syncingOptions = true;
        try
        {
            var keep = TaskTypeOptions.Where(o => o.IsSelected).Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            TaskTypeOptions.Clear();
            foreach (var type in types)
                TaskTypeOptions.Add(new SelectableOption(type, keep.Count == 0 || keep.Contains(type), 0));
        }
        finally
        {
            _syncingOptions = false;
        }
        OnPropertyChanged(nameof(SelectedTaskTypesLabel));
    }

    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(ShowAiHint));
    }

    /// <summary>
    /// Counts leaving the test window during strict mode. Called from the
    /// window Deactivated event. Never blocks, only records honestly.
    /// </summary>
    public void RegisterFocusLost()
    {
        if (!IsRunning || !StrictMode) return;
        StrictViolations++;
        StatusMessage = ViolationLabel;
    }

    private IEnumerable<(ExamPaper Paper, ExamPart Part)> FilteredParts()
    {
        IEnumerable<ExamPaper> papers = MixAllPapers
            ? Papers
            : SelectedPaper is null ? Enumerable.Empty<ExamPaper>() : new[] { SelectedPaper };

        // Tick boxes drive the filter. No tick means every skill with work.
        var wantedSkills = SkillOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        var wantedTypes = TaskTypeOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        bool filterTypes = TaskTypeOptions.Count > 0 && wantedTypes.Count > 0
            && wantedTypes.Count < TaskTypeOptions.Count;

        foreach (var paper in papers)
        {
            foreach (var part in paper.Parts)
            {
                if (wantedSkills.Count > 0 && !wantedSkills.Any(s =>
                    string.Equals(s, part.Skill, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (filterTypes && !wantedTypes.Any(t =>
                    string.Equals(t, part.TaskType, StringComparison.OrdinalIgnoreCase)))
                    continue;
                yield return (paper, part);
            }
        }
    }

    [RelayCommand]
    private async Task StartExamAsync()
    {
        if (IsRunning)
        {
            var ask = System.Windows.MessageBox.Show(
                "A test is already running. Start a new one and drop the current answers?",
                "Start new test",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (ask != System.Windows.MessageBoxResult.Yes)
                return;
        }
        _parts.Clear();
        _timer.Stop();
        _speakingCts?.Cancel();
        _listeningCts?.Cancel();
        _prepCts?.Cancel();
        _audio.StopPlayback();
        _audio.StopRecording();
        StrictViolations = 0;

        var chosen = FilteredParts().ToList();
        if (chosen.Count == 0)
        {
            ResultText = "No parts match this setup. Tick a skill that has parts, or loosen the task types.";
            BandLabel = string.Empty;
            IsRunning = false;
            IsFinished = false;
            return;
        }
        if (chosen.All(c => string.Equals(c.Part.Skill, "Listening", StringComparison.OrdinalIgnoreCase)))
        {
            ResultText = "Listening cannot run alone. Tick Reading, Writing, or Speaking too, then start again.";
            BandLabel = string.Empty;
            IsRunning = false;
            IsFinished = false;
            StatusMessage = ResultText;
            return;
        }

        string buildNote = string.Empty;
        if (SelectedBuildMode == "Random" || ShuffleParts)
        {
            var rng = new Random();
            chosen = chosen.OrderBy(_ => rng.Next()).ToList();
            buildNote = "Random order.";
        }
        else if (SelectedBuildMode == "AI pick")
        {
            var picked = await PickBalancedAsync(chosen);
            chosen = picked.Parts;
            buildNote = picked.Note;
        }

        foreach (var (paper, part) in chosen)
        {
            var vm = new ExamPartViewModel(part);
            if (MixAllPapers || chosen.Select(c => c.Paper.Title).Distinct().Count() > 1)
                vm.PaperName = paper.Title;
            _parts.Add(vm);
        }

        BeginRun(buildNote);
    }

    /// <summary>
    /// Starts a custom test assembled elsewhere, for example the Library
    /// basket. Unknown paper or part names are skipped quietly. Needs at
    /// least one part and two skills when listening is inside.
    /// </summary>
    public void StartCustomTest(IEnumerable<(string PaperTitle, string PartId)> picks, string note)
    {
        _parts.Clear();
        _timer.Stop();
        _speakingCts?.Cancel();
        _listeningCts?.Cancel();
        _prepCts?.Cancel();
        _audio.StopPlayback();
        _audio.StopRecording();
        StrictViolations = 0;

        var fresh = _repository.LoadPapers();
        var chosen = new List<(ExamPaper Paper, ExamPart Part)>();
        foreach (var (title, id) in picks)
        {
            var paper = fresh.FirstOrDefault(p =>
                string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
            var part = paper?.Parts.FirstOrDefault(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (paper is not null && part is not null
                && !chosen.Any(c => ReferenceEquals(c.Part, part)))
                chosen.Add((paper, part));
        }

        if (chosen.Count == 0)
        {
            ResultText = "The basket is empty. Drag papers into it first.";
            BandLabel = string.Empty;
            IsRunning = false;
            IsFinished = false;
            return;
        }

        Papers.Clear();
        foreach (var paper in fresh)
            Papers.Add(paper);
        SelectedPaper = Papers.FirstOrDefault();
        RebuildSkillOptions();
        RebuildTaskTypes();
        RebuildPaperRows();

        foreach (var (paper, part) in chosen)
        {
            var vm = new ExamPartViewModel(part);
            vm.PaperName = paper.Title;
            _parts.Add(vm);
        }

        BeginRun(note);
    }

    private void BeginRun(string buildNote)
    {
        IsRunning = true;
        IsFinished = false;
        ShowReview = false;
        ReviewItems.Clear();
        AiFeedbackLines.Clear();
        OnPropertyChanged(nameof(HasAiFeedback));
        OnPropertyChanged(nameof(Parts));
        ResultText = string.Empty;
        BandLabel = string.Empty;
        foreach (var part in _parts)
        {
            part.TimerHidden = false;
            part.NotesOpen = false;
            part.ContrastOn = false;
        }
        // Full screen is decided per run by the window, so a stale toggle from
        // a past test does not carry over.
        IsFullscreen = false;
        var intro = StrictMode
            ? "Strict mode is on. Full screen, no other apps, finish the test."
            : "Test started. Answer every part, then submit.";
        StatusMessage = string.IsNullOrWhiteSpace(buildNote) ? intro : $"{intro} {buildNote}";
        SetPart(0);
        _timer.Start();
    }

    /// <summary>
    /// Builds the closest thing to a real full test: AI picks the parts
    /// and explains why, or a balanced deterministic set when no model
    /// answers. Listening only survives inside a multi skill set.
    /// </summary>
    private async Task<(List<(ExamPaper Paper, ExamPart Part)> Parts, string Note)> PickBalancedAsync(
        List<(ExamPaper Paper, ExamPart Part)> candidates)
    {
        var catalog = candidates.Select(c =>
            $"{c.Paper.Title}|{c.Part.Id}, {c.Part.Skill}, {c.Part.TaskType}, {c.Part.Minutes}min");

        if (_ai.IsAvailable)
        {
            StatusMessage = "AI is building your full test.";
            try
            {
                var pick = await _ai.PickTestAsync(
                    string.Join("\n", catalog), HistorySummary());
                if (pick.Success)
                {
                    var ordered = new List<(ExamPaper Paper, ExamPart Part)>();
                    foreach (var id in pick.Ids)
                    {
                        var cut = id.Split('|', 2);
                        if (cut.Length != 2) continue;
                        var found = candidates.FirstOrDefault(c =>
                            string.Equals(c.Paper.Title, cut[0], StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.Part.Id, cut[1], StringComparison.OrdinalIgnoreCase));
                        if (found.Part is not null
                            && !ordered.Any(o => ReferenceEquals(o.Part, found.Part)))
                            ordered.Add(found);
                    }
                    if (ordered.Count >= 2 && !IsListeningOnly(ordered))
                        return (ordered, $"AI built this test: {pick.Reason}");
                }
            }
            catch (Exception)
            {
                // A model failure falls through to the balanced set below.
            }
        }

        var balanced = new List<(ExamPaper Paper, ExamPart Part)>();
        foreach (var skill in new[] { "Listening", "Reading", "Writing", "Speaking" })
        {
            balanced.AddRange(candidates
                .Where(c => string.Equals(c.Part.Skill, skill, StringComparison.OrdinalIgnoreCase)
                    && !balanced.Any(o => ReferenceEquals(o.Part, c.Part)))
                .Take(2));
        }
        if (balanced.Count == 0)
            balanced.AddRange(candidates.Take(8));
        balanced = balanced.Take(8).ToList();
        if (IsListeningOnly(balanced))
            balanced = candidates.Take(8).ToList();
        return (balanced, "Balanced set across the four skills.");
    }

    private static bool IsListeningOnly(List<(ExamPaper Paper, ExamPart Part)> parts) =>
        parts.Count > 0 && parts.All(c =>
            string.Equals(c.Part.Skill, "Listening", StringComparison.OrdinalIgnoreCase));

    /// <summary>Past bands per scope, so the picker can favor weak skills.</summary>
    private static string HistorySummary()
    {
        try
        {
            using var db = new AppDbContext();
            var rows = db.ExamAttempts
                .GroupBy(a => a.Scope)
                .Select(g => new { Scope = g.Key, Avg = g.Average(a => (a.BandLow + a.BandHigh) / 2.0), Count = g.Count() })
                .ToList();
            if (rows.Count == 0) return "no past tests";
            return string.Join("; ", rows.Select(r => $"{r.Scope}: {r.Avg:0.0} over {r.Count} test(s)"));
        }
        catch
        {
            return "history unavailable";
        }
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
    private void GoToQuestion(ExamQuestionViewModel question)
    {
        CurrentPart?.GoToQuestion(question);
    }

    /// <summary>Jump to any question of any part, like the bottom palette.</summary>
    [RelayCommand]
    private void GoToGlobal(ExamQuestionViewModel question)
    {
        if (!IsRunning || question is null) return;
        if (CurrentPart?.IsSpeaking == true) return;
        for (int i = 0; i < _parts.Count; i++)
        {
            if (_parts[i].Questions.Contains(question))
            {
                SetPart(i);
                _parts[i].GoToQuestion(question);
                return;
            }
        }
    }

    [RelayCommand]
    private void NextQuestion()
    {
        CurrentPart?.NextQuestion();
    }

    [RelayCommand]
    private void PreviousQuestion()
    {
        CurrentPart?.PreviousQuestion();
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
    /// Timer click, like the real test: hide the clock when it causes stress,
    /// click again to bring it back.
    /// </summary>
    [RelayCommand]
    private void ToggleTimer() => CurrentPart?.ToggleTimer();

    /// <summary>
    /// Raised when the student wants full screen on or off. The window is the
    /// only place that can change its own window state, so it listens.
    /// </summary>
    public event EventHandler? FullscreenToggleRequested;

    public event EventHandler? StrictToggleRequested;

    /// <summary>
    /// Full screen without the strict rules. It is a focus aid, not an exam
    /// lock, so it can be left at any time.
    /// </summary>
    [RelayCommand]
    private void ToggleFullscreen() => FullscreenToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Turns strict mode off and on from inside the test window.</summary>
    [RelayCommand]
    private void ToggleStrict() => StrictToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Text size buttons in the exam settings strip.</summary>
    [RelayCommand]
    private void BiggerText()
    {
        FontScale = Math.Min(1.6, Math.Round(FontScale + 0.1, 2));
    }

    [RelayCommand]
    private void SmallerText()
    {
        FontScale = Math.Max(0.9, Math.Round(FontScale - 0.1, 2));
    }

    /// <summary>
    /// Yellow on black, the high contrast mode the real test offers. Applies
    /// to the reading passage and the writing answer box.
    /// </summary>
    [RelayCommand]
    private void ToggleContrast()
    {
        if (CurrentPart is null) return;
        CurrentPart.ContrastOn = !CurrentPart.ContrastOn;
    }

    /// <summary>
    /// Opens the side notes panel. When the student has selected text in the
    /// passage, that text is quoted at the top of the notes box, so a note
    /// stays attached to the sentence it belongs to.
    /// </summary>
    [RelayCommand]
    private void OpenNotes(string? selectedText)
    {
        if (CurrentPart is null) return;
        CurrentPart.SelectedMaterialWord = (selectedText ?? string.Empty).Trim();
        CurrentPart.NotesOpen = true;
    }

    /// <summary>
    /// Plays the Listening clip exactly once when its part opens, like the
    /// real test. The audio must be a real exam recording in the part file,
    /// so a missing clip shows a clear message instead of a synthetic voice.
    /// </summary>
    private async Task PlayListeningOnceAsync(ExamPartViewModel part, CancellationToken ct)
    {
        if (part.AudioPlayedOnce) return;

        // Reading time before the clip, like the real test. Skippable.
        part.AudioStatus = "Read the questions. The clip starts in 15 seconds, or press Start now.";
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _prepCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) return;
        }

        part.AudioPlayedOnce = true;
        part.NoAudioFallback = false;

        string? path = string.IsNullOrWhiteSpace(part.AudioFile)
            ? null
            : ResolveAudio(part.AudioFile);
        if (path is not null && !File.Exists(path))
            path = null;

        if (path is null)
        {
            // No clip in the paper. Show the transcript to read, never a
            // synthesized voice, so Listening stays real exam audio only.
            part.AudioStatus = "No audio file for this part. Read the transcript and answer.";
            part.NoAudioFallback = true;
            return;
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
            part.AudioStatus = "Could not play this clip. Check the file and your speakers.";
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
            CurrentPart.SpokenSeconds = CurrentPart.SpeakingSeconds;
            if (_stt.IsAvailable())
            {
                // Keep ready mode loads the transcription model here, so the
                // first transcript does not stall after the recording ends.
                if (_models.KeepReady)
                {
                    CurrentPart.AudioStatus = "Getting the transcription model ready.";
                    await _models.PrepareAsync(new[] { "stt-whisper-tiny-en" }, _speakingCts.Token);
                }
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
                CurrentPart.AudioStatus = $"Saved recording. No transcription model, type what you said below so AI marking can work. File kept locally.";
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
        if (IsGrading) return;
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
        _gradingCts?.Cancel();
        _gradingCts = new CancellationTokenSource();
        var ct = _gradingCts.Token;
        IsGrading = true;
        AiFeedbackLines.Clear();
        LoadingLabel = "Preparing the writing and speaking models.";
        StatusMessage = $"The model is marking ({IeltsBanding.StrictnessLabel(SelectedStrictness)}). Press Stop to cancel.";
        try
        {
            // Preload only when the user asked for it. On demand mode leaves
            // each service to load lazily on its first real call.
            var wanted = new List<string>();
            if (_parts.Any(p => p.IsWriting)) wanted.AddRange(_models.WritingSlots);
            if (_parts.Any(p => p.IsSpeaking)) wanted.AddRange(_models.SpeakingSlots);
            var prep = await _models.PrepareAsync(wanted, ct);
            ModelModeLabel = prep.Message;
            LoadingLabel = "Marking with the language model.";
            foreach (var part in _parts.Where(p => p.IsWriting))
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(part.Essay))
                {
                    AiFeedbackLines.Add($"{part.Title}: no essay, skipped.");
                    continue;
                }
                int minimum = LlmPrompts.IsTask1(part.Material + " " + part.Instructions + " " + part.Title)
                    ? 150 : 250;
                var r = await _ai.ReviewWritingAsync(
                    part.Material + "\n" + part.Instructions, part.Essay, minimum, SelectedStrictness, ct);
                if (!r.Success || r.Feedback is null)
                {
                    AiFeedbackLines.Add($"{part.Title}: {r.Error}");
                    continue;
                }
                var f = r.Feedback;
                f = await ApplyGrammarCheckAsync(part.Title, part.Essay, f);
                part.AiResult = $"Band {f.BandLabel}. {f.Summary}";
                string firstName = part.Title.Contains("Task 1", StringComparison.OrdinalIgnoreCase)
                    ? "Task Achievement" : "Task Response";
                AiFeedbackLines.Add($"{part.Title}: band {f.BandLabel}. {f.Summary}");
                AiFeedbackLines.Add($"  Band table: {firstName} {f.TaskResponse:0.0}, " +
                    $"Cohesion {f.Coherence:0.0}, Words {f.LexicalResource:0.0}, Grammar {f.Grammar:0.0}.");
                AddWhy(AiFeedbackLines, $"  {firstName}", f.TaskResponseWhy);
                AddWhy(AiFeedbackLines, "  Cohesion", f.CoherenceWhy);
                AddWhy(AiFeedbackLines, "  Words", f.LexicalWhy);
                AddWhy(AiFeedbackLines, "  Grammar", f.GrammarWhy);
                foreach (var s in f.Strengths.Take(2)) AiFeedbackLines.Add($"  Good: {s}");
                foreach (var s in f.Improvements.Take(3)) AiFeedbackLines.Add($"  Fix: {s}");
                if (!string.IsNullOrWhiteSpace(f.CorrectedExcerpt))
                    AiFeedbackLines.Add($"  Rewrite: {f.CorrectedExcerpt}");
                AiFeedbackLines.Add($"  Stats: {WritingStats(part.Essay)}");
            }
            foreach (var part in _parts.Where(p => p.IsSpeaking))
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(part.Transcript))
                {
                    AiFeedbackLines.Add($"{part.Title}: no transcript, skipped. Type what you said to get a band.");
                    continue;
                }
                var r = await _ai.AssessSpeakingAsync(
                    part.Material + "\n" + part.Title, part.Transcript,
                    $"{part.Skill} {part.TaskType}", part.SpokenSeconds, SelectedStrictness, ct);
                if (!r.Success || r.Feedback is null)
                {
                    AiFeedbackLines.Add($"{part.Title}: {r.Error}");
                    continue;
                }
                var f = r.Feedback;
                f = ApplyPaceCap(part, f);
                part.AiResult = $"Band {f.BandLabel}. {f.Summary}";
                AiFeedbackLines.Add($"{part.Title}: band {f.BandLabel}. {f.Summary}");
                AiFeedbackLines.Add($"  Band table: Fluency {f.Fluency:0.0}, " +
                    $"Words {f.LexicalResource:0.0}, Grammar {f.Grammar:0.0}, Sound {f.Pronunciation:0.0}.");
                AddWhy(AiFeedbackLines, "  Fluency", f.FluencyWhy);
                AddWhy(AiFeedbackLines, "  Words", f.LexicalWhy);
                AddWhy(AiFeedbackLines, "  Grammar", f.GrammarWhy);
                AddWhy(AiFeedbackLines, "  Sound", f.PronunciationWhy);
                foreach (var s in f.Strengths.Take(2)) AiFeedbackLines.Add($"  Good: {s}");
                foreach (var s in f.Improvements.Take(3)) AiFeedbackLines.Add($"  Fix: {s}");
            }
            foreach (var part in _parts.Where(p => p.IsListening))
            {
                ct.ThrowIfCancellationRequested();
                foreach (var q in part.Questions.Where(q => !q.IsCorrect))
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await _ai.ExplainListeningAsync(
                        part.Material, QuestionDetail(q), q.ChosenLabel, q.DisplayAnswer, ct);
                    AiFeedbackLines.Add(r.Success
                        ? $"{part.Title}, question {q.Model.Number}: {OneLine(r.Text)}"
                        : $"{part.Title}, question {q.Model.Number}: {r.Error}");
                }
            }
            foreach (var part in _parts.Where(p => !p.IsListening && !p.IsWriting && !p.IsSpeaking))
            {
                // Reading mistakes get a short AI explanation each.
                foreach (var q in part.Questions.Where(q => !q.IsCorrect))
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await _ai.ExplainReadingAsync(
                        part.Material, QuestionDetail(q), q.ChosenLabel, q.DisplayAnswer, ct);
                    AiFeedbackLines.Add(r.Success
                        ? $"{part.Title}, question {q.Model.Number}: {OneLine(r.Text)}"
                        : $"{part.Title}, question {q.Model.Number}: {r.Error}");
                }
            }
            OnPropertyChanged(nameof(HasAiFeedback));
            StatusMessage = AiFeedbackLines.Count == 0
                ? "Nothing to mark. Write an essay or type a speaking transcript first."
                : "AI marking done. Listening and Reading lines explain each wrong answer. Bands are ranges for practice, not official scores.";
            UpdateLastAttemptWithAi();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "AI marking stopped.";
        }
        finally
        {
            // The models a single run pulled in are freed when it ends, so
            // RAM is not held while the student reads results. Manual loads
            // that were already in memory stay.
            _models.ReleaseAfterUse(_models.WritingSlots.Concat(_models.SpeakingSlots));
            IsGrading = false;
            LoadingLabel = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelGrading()
    {
        _gradingCts?.Cancel();
        StatusMessage = "Stopping AI marking.";
    }

    [RelayCommand]
    private void CopyFeedback()
    {
        if (AiFeedbackLines.Count == 0)
        {
            StatusMessage = "Nothing to copy yet. Grade with AI first.";
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(string.Join("\n", AiFeedbackLines));
            StatusMessage = "Feedback copied to the clipboard.";
        }
        catch (Exception)
        {
            StatusMessage = "Could not copy. Select the text by hand.";
        }
    }

    /// <summary>
    /// Stores the AI bands and feedback on the newest attempt, so the
    /// Results page can show the detail of what the model said.
    /// </summary>
    private void UpdateLastAttemptWithAi()
    {
        if (AiFeedbackLines.Count == 0) return;
        try
        {
            using var db = new AppDbContext();
            var last = db.ExamAttempts.OrderByDescending(a => a.Id).FirstOrDefault();
            if (last is null) return;

            double writing = _parts.Where(p => p.IsWriting && p.AiResult.Length > 0)
                .Select(p => ParseBand(p.AiResult)).DefaultIfEmpty(0).Max();
            double speaking = _parts.Where(p => p.IsSpeaking && p.AiResult.Length > 0)
                .Select(p => ParseBand(p.AiResult)).DefaultIfEmpty(0).Max();
            if (writing > 0) last.WritingBand = writing;
            if (speaking > 0) last.SpeakingBand = speaking;

            var feedback = string.Join("\n", AiFeedbackLines);
            last.AiFeedback = feedback.Length > 8000 ? feedback[..8000] : feedback;
            db.SaveChanges();
        }
        catch
        {
            // Result history must never block the feedback screen.
        }
    }

    /// <summary>Reads the first band number out of "Band 6.0 to 7.0. ...".</summary>
    private static double ParseBand(string aiResult)
    {
        var match = System.Text.RegularExpressions.Regex.Match(aiResult, @"(\d+(?:\.\d+)?)");
        return match.Success && double.TryParse(match.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value : 0;
    }

    /// <summary>
    /// Full question detail for the model: prompt plus options, bank, or
    /// accepted answers. Keys alone tell the model nothing.
    /// </summary>
    private static string QuestionDetail(ExamQuestionViewModel q)
    {
        var sb = new StringBuilder($"Q{q.Model.Number}: {q.Prompt}");
        if (q.ShowOptions && q.Options.Count > 0)
            sb.Append(" Options: " + string.Join("; ",
                q.Options.Select(o => $"{o.Key}) {o.Text}")));
        if (q.IsGap)
            sb.Append($" Accepted answers: {q.Model.GapAnswer}");
        if (q.IsMatch)
        {
            if (q.Bank.Count > 0)
                sb.Append(" Bank: " + string.Join("; ", q.Bank));
            sb.Append(" Rows: " + string.Join("; ",
                q.MatchRows.Select(r => $"{r.Label} is {r.Answer}")));
        }
        if (!string.IsNullOrWhiteSpace(q.Model.Explanation))
            sb.Append($" Note: {q.Model.Explanation}");
        return sb.ToString();
    }

    /// <summary>
    /// Offline writing stats: words, sentences, pace, and long word share.
    /// Informational only, never a band.
    /// </summary>
    private static string WritingStats(string essay)
    {
        var words = essay.Split(new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "no words";
        int sentences = Math.Max(1, System.Text.RegularExpressions.Regex
            .Matches(essay, @"[.!?]+").Count);
        int distinct = new HashSet<string>(
            words.Select(w => w.Trim('.', ',', '!', '?', ';', ':').ToLowerInvariant()))
            .Count;
        int longWords = words.Count(w => w.Length >= 7);
        return $"{words.Length} words, {sentences} sentences, " +
            $"{words.Length / (double)sentences:0.0} words per sentence, " +
            $"{distinct} different words, {longWords * 100 / words.Length}% long words.";
    }

    /// <summary>
    /// Grounds the LLM fluency band with measured speech pace from the
    /// transcript and the recorded seconds. Slow speech caps fluency,
    /// then the overall band is recomputed like an examiner would.
    /// </summary>
    private SpeakingFeedback ApplyPaceCap(ExamPartViewModel part, SpeakingFeedback f)
    {
        int words = CountWords(part.Transcript);
        double minutes = Math.Max(1, part.SpokenSeconds) / 60.0;
        double wpm = words / minutes;
        AiFeedbackLines.Add($"{part.Title}: speech pace about {wpm:0} words per minute.");

        double cap = wpm switch
        {
            < 60 => 5.0,
            < 90 => 6.0,
            < 110 => 7.0,
            _ => 9.0
        };
        if (f.Fluency <= cap)
            return f;

        double fluency = cap;
        double overall = IeltsBanding.RoundHalf(
            (fluency + f.LexicalResource + f.Grammar + f.Pronunciation) / 4.0);
        var range = IeltsBanding.ToRange(overall, SelectedStrictness);
        AiFeedbackLines.Add($"{part.Title}: slow pace caps fluency at {fluency:0.0}.");
        return new SpeakingFeedback
        {
            EstimatedBand = overall,
            BandLow = range.Low,
            BandHigh = range.High,
            Fluency = fluency,
            LexicalResource = f.LexicalResource,
            Grammar = f.Grammar,
            Pronunciation = f.Pronunciation,
            FluencyWhy = f.FluencyWhy,
            LexicalWhy = f.LexicalWhy,
            GrammarWhy = f.GrammarWhy,
            PronunciationWhy = f.PronunciationWhy,
            Summary = f.Summary,
            Strengths = f.Strengths,
            Improvements = f.Improvements
        };
    }

    private static int CountWords(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

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
        {
            AiFeedbackLines.Add($"{title}: no grammar model, grammar band is an AI estimate only.");
            return f;
        }

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
            TaskAchievement = f.TaskAchievement,
            Coherence = f.Coherence,
            LexicalResource = f.LexicalResource,
            Grammar = grammar,
            TaskResponseWhy = f.TaskResponseWhy,
            TaskAchievementWhy = f.TaskAchievementWhy,
            CoherenceWhy = f.CoherenceWhy,
            LexicalWhy = f.LexicalWhy,
            GrammarWhy = f.GrammarWhy,
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
    private void SubmitExam() => SubmitExamCore(confirm: true);

    /// <summary>
    /// Manual submit asks first when questions are blank, like the real
    /// test. Timer auto submit never asks.
    /// </summary>
    private void SubmitExamCore(bool confirm)
    {
        if (!IsRunning) return;
        if (confirm)
        {
            int blank = UnansweredCount;
            var ask = System.Windows.MessageBox.Show(
                blank == 0
                    ? "Submit the test now?"
                    : $"Submit now? {blank} question(s) have no answer.",
                "Submit test",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (ask != System.Windows.MessageBoxResult.Yes)
                return;
        }
        _timer.Stop();
        _speakingCts?.Cancel();
        _listeningCts?.Cancel();
        _prepCts?.Cancel();
        _audio.StopPlayback();
        _audio.StopRecording();
        IsRunning = false;
        IsFinished = true;
        ShowReview = false;
        ReviewItems.Clear();

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
        StatusMessage = ResultText;

        SaveAttempt(correct, total, range);
        OnPropertyChanged(nameof(HasBand));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>
    /// Raised when the student asks to leave the exam window, for example
    /// Back to setup or Close. The shell listens and closes the window.
    /// </summary>
    public event EventHandler? ExamWindowCloseRequested;

    /// <summary>Clears the result screen so a new test can be set up.</summary>
    [RelayCommand]
    private void BackToSetup()
    {
        IsFinished = false;
        ShowReview = false;
        ResultText = string.Empty;
        BandLabel = string.Empty;
        ReviewItems.Clear();
        AiFeedbackLines.Clear();
        OnPropertyChanged(nameof(ShowResult));
        OnPropertyChanged(nameof(HasBand));
        OnPropertyChanged(nameof(HasAiFeedback));
        ExamWindowCloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the exam window without touching a running test.</summary>
    [RelayCommand]
    private void CloseExam()
    {
        ExamWindowCloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops a running test without saving it, used when the student closes
    /// the exam window. Answers in the current run are dropped.
    /// </summary>
    public void CancelRunningTest()
    {
        _timer.Stop();
        _gradingCts?.Cancel();
        _speakingCts?.Cancel();
        _listeningCts?.Cancel();
        _prepCts?.Cancel();
        _audio.StopPlayback();
        _audio.StopRecording();
        IsRunning = false;
        IsFinished = false;
        ResultText = string.Empty;
        BandLabel = string.Empty;
        OnPropertyChanged(nameof(ShowResult));
        OnPropertyChanged(nameof(HasBand));
    }

    [RelayCommand]
    private void ToggleReview()
    {
        ShowReview = !ShowReview;
        if (!ShowReview) return;

        ReviewItems.Clear();
        foreach (var part in _parts)
        {
            if (part.IsWriting)
            {
                AddReview($"{part.Title}: {part.WordCountLabel} written, not auto scored.");
                continue;
            }
            if (part.IsSpeaking)
            {
                AddReview($"{part.Title}: {part.TranscriptWordCount}, not auto scored.");
                continue;
            }
            if (part.IsListening)
            {
                // Listening gives no answers back, only the wrong questions.
                // The transcript is revealed here, after the test.
                var wrong = part.Questions.Where(q => !q.IsCorrect).ToList();
                if (wrong.Count == 0)
                    AddReview($"{part.Title}: all correct.", true);
                foreach (var q in wrong)
                    AddReview($"{part.Title}, question {q.Model.Number}: {q.ListeningReviewLabel}", false);
                if (part.HasMaterial)
                    AddReview($"{part.Title} transcript: {part.Material}");
                continue;
            }
            foreach (var q in part.Questions)
            {
                AddReview($"{part.Title}, question {q.Model.Number}: {q.ReadingReviewLabel}", q.IsCorrect);
                if (q.IsMatch)
                {
                    foreach (var row in q.MatchRows)
                    {
                        var mark = row.IsCorrect ? "correct"
                            : string.IsNullOrWhiteSpace(row.Selected) ? $"no answer, correct is {row.Answer}"
                            : $"you chose {row.Selected.Trim()}, correct is {row.Answer}";
                        AddReview($"    {row.Label}: {mark}", row.IsCorrect);
                    }
                }
            }
        }
    }

    private void AddReview(string text, bool? isGood = null)
        => ReviewItems.Add(new ReviewItem(text, isGood));

    private void SaveAttempt(int correct, int total, BandRange range)
    {
        try
        {
            using var db = new AppDbContext();
            var summary = ResultText.Length > 500 ? ResultText[..500] : ResultText;
            if (HasViolations)
                summary += $" Focus left {StrictViolations} time(s) during strict mode.";
            db.ExamAttempts.Add(new ExamAttempt
            {
                PaperTitle = SelectedBuildMode == "AI pick" ? "AI built test"
                    : MixAllPapers ? "Mixed papers" : SelectedPaper?.Title ?? SelectedScope,
                Scope = SelectedScope,
                Strictness = IeltsBanding.StrictnessLabel(SelectedStrictness),
                BandLow = range.Low,
                BandHigh = range.High,
                Correct = correct,
                Total = total,
                Summary = summary,
                Violations = StrictViolations,
                CreatedAt = DateTime.Now
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
        return clean.Length <= 320 ? clean : clean[..320] + "...";
    }

    /// <summary>Adds one band table row, skipping empty model reasons.</summary>
    private static void AddWhy(ObservableCollection<string> lines, string label, string why)
    {
        if (string.IsNullOrWhiteSpace(why)) return;
        lines.Add($"{label}: {OneLine(why)}");
    }

    private void SetPart(int index)
    {
        _listeningCts?.Cancel();
        _prepCts?.Cancel();
        _audio.StopPlayback();
        foreach (var part in _parts)
            part.IsCurrent = false;
        PartIndex = index;
        CurrentPart = _parts[index];
        CurrentPart.IsTimerRunning = true;
        CurrentPart.IsCurrent = true;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        if (CurrentPart.IsListening)
        {
            _listeningCts = new CancellationTokenSource();
            _prepCts = new CancellationTokenSource();
            _ = PlayListeningOnceAsync(CurrentPart, _listeningCts.Token);
        }
    }

    [RelayCommand]
    private void SkipPrep()
    {
        _prepCts?.Cancel();
    }

    private void Tick()
    {
        if (CurrentPart is null) { _timer.Stop(); return; }
        CurrentPart.Tick();
        CurrentPart.RefreshProgress();
        if (CurrentPart.RemainingSeconds > 0) return;

        CurrentPart.IsTimerRunning = false;
        if (PartIndex < _parts.Count - 1)
        {
            SetPart(PartIndex + 1);
            return;
        }

        SubmitExamCore(confirm: false);
    }
}
