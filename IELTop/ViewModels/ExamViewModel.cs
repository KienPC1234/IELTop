using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
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

    public ObservableCollection<ExamOptionViewModel> Options { get; } = new();

    public string NumberLabel => $"Question {Model.Number}";
    public string Prompt => Model.Prompt;

    public ExamQuestionViewModel(ExamQuestion model)
    {
        Model = model;
        foreach (var option in model.Options)
            Options.Add(new ExamOptionViewModel(option, this));
    }

    partial void OnSelectedKeyChanged(string value)
    {
        // Keep the radio buttons in sync when scoring resets the answer.
        foreach (var option in Options)
            option.IsSelected = option.Key == value;
    }

    public bool IsCorrect => SelectedKey == Model.CorrectKey;

    public string ReviewLabel => string.IsNullOrEmpty(SelectedKey)
        ? $"no answer, correct is {Model.CorrectKey}"
        : IsCorrect ? "correct" : $"you chose {SelectedKey}, correct is {Model.CorrectKey}";
}

/// <summary>
/// One part of the test with a countdown timer and a question list.
/// Writing parts collect an essay instead of multiple choice answers.
/// </summary>
public sealed partial class ExamPartViewModel : ObservableObject
{
    public ExamPart Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingLabel))]
    private int _remainingSeconds;
    [ObservableProperty] private bool _isTimerRunning;
    [ObservableProperty] private string _essay = string.Empty;

    public string Title => Model.Title;
    public string Skill => Model.Skill;
    public string Instructions => Model.Instructions;
    public string Material => Model.Material;
    public bool HasMaterial => !string.IsNullOrWhiteSpace(Material);
    public bool IsWriting => string.Equals(Skill, "Writing", StringComparison.OrdinalIgnoreCase)
                             && Questions.Count == 0;

    public ObservableCollection<ExamQuestionViewModel> Questions { get; } = new();
    public bool HasQuestions => Questions.Count > 0;

    public string WordCountLabel => $"{CountWords(Essay)} words";

    public ExamPartViewModel(ExamPart model)
    {
        Model = model;
        RemainingSeconds = model.Minutes * 60;
        foreach (var q in model.Questions)
            Questions.Add(new ExamQuestionViewModel(q));
    }

    partial void OnEssayChanged(string value) => OnPropertyChanged(nameof(WordCountLabel));

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

    private static int CountWords(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// Drives the full IDP-style mock test: pick a paper, work through parts,
/// then submit for a band estimate. Answers live in the part view models,
/// so every part is scored, not only the one on screen.
/// </summary>
public sealed partial class ExamViewModel : ObservableObject
{
    private readonly IExamRepository _repository;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly List<ExamPartViewModel> _parts = new();

    [ObservableProperty] private ExamPaper? _selectedPaper;
    [ObservableProperty] private ExamPartViewModel? _currentPart;
    [ObservableProperty] private int _partIndex;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _showReview;

    public ObservableCollection<ExamPaper> Papers { get; } = new();
    public ObservableCollection<string> ReviewLines { get; } = new();

    public ExamViewModel(IExamRepository repository)
    {
        _repository = repository;
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public bool HasPapers => Papers.Count > 0;
    public bool ShowNoPaperWarning => !HasPapers;
    public bool CanGoBack => IsRunning && PartIndex > 0;
    public bool CanGoNext => IsRunning && SelectedPaper is not null && PartIndex < _parts.Count - 1;
    public bool ShowResult => !IsRunning && !string.IsNullOrWhiteSpace(ResultText);

    public string PaperCountLabel => $"{Papers.Count} test paper(s) available.";

    public string MaterialWarning =>
        $"No test papers found. Add a .json paper under {_repository.ExamsDir}.";

    public void Load()
    {
        Papers.Clear();
        foreach (var paper in _repository.LoadPapers())
            Papers.Add(paper);

        OnPropertyChanged(nameof(HasPapers));
        OnPropertyChanged(nameof(ShowNoPaperWarning));
        OnPropertyChanged(nameof(PaperCountLabel));
    }

    [RelayCommand]
    private void StartExam()
    {
        _parts.Clear();
        _timer.Stop();

        if (SelectedPaper is null || SelectedPaper.Parts.Count == 0)
        {
            ResultText = "Pick a test paper with at least one part.";
            IsRunning = false;
            IsFinished = false;
            return;
        }

        foreach (var part in SelectedPaper.Parts)
            _parts.Add(new ExamPartViewModel(part));

        IsRunning = true;
        IsFinished = false;
        ShowReview = false;
        ReviewLines.Clear();
        ResultText = string.Empty;
        SetPart(0);
        _timer.Start();
    }

    [RelayCommand]
    private void NextPart()
    {
        if (PartIndex < _parts.Count - 1)
            SetPart(PartIndex + 1);
    }

    [RelayCommand]
    private void PreviousPart()
    {
        if (PartIndex > 0)
            SetPart(PartIndex - 1);
    }

    [RelayCommand]
    private void SubmitExam()
    {
        _timer.Stop();
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

        double ratio = total == 0 ? 0 : (double)correct / total;
        ResultText = total == 0
            ? "This paper has no multiple choice questions to score. Writing answers are not auto scored."
            : $"Score {correct} of {total}. Estimated band {EstimateBand(ratio):0.0}. " +
              "Writing and Speaking answers need a teacher or an AI check.";

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
            foreach (var q in part.Questions)
                ReviewLines.Add($"{part.Title}, question {q.Model.Number}: {q.ReviewLabel}");
        }
    }

    /// <summary>
    /// Rough band curve for multiple choice practice only. It is not an official score.
    /// </summary>
    private static double EstimateBand(double ratio) => ratio switch
    {
        >= 0.90 => 8.5,
        >= 0.80 => 7.5,
        >= 0.70 => 6.5,
        >= 0.60 => 6.0,
        >= 0.50 => 5.5,
        >= 0.35 => 5.0,
        >= 0.20 => 4.0,
        _ => 3.5
    };

    private void SetPart(int index)
    {
        PartIndex = index;
        CurrentPart = _parts[index];
        CurrentPart.IsTimerRunning = true;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void Tick()
    {
        if (CurrentPart is null) { _timer.Stop(); return; }
        CurrentPart.Tick();
        if (CurrentPart.RemainingSeconds > 0) return;

        // Time is up for this part. Move on, or submit when it was the last part.
        CurrentPart.IsTimerRunning = false;
        if (PartIndex < _parts.Count - 1)
        {
            SetPart(PartIndex + 1);
            return;
        }

        SubmitExam();
    }
}
