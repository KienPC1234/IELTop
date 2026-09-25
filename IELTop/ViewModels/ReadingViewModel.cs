using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>
/// A selectable answer choice. Selecting it records the key on the question.
/// </summary>
public sealed partial class PracticeOptionViewModel : ObservableObject
{
    private readonly PracticeQuestionViewModel _question;

    [ObservableProperty] private bool _isSelected;

    public string Key { get; }
    public string Text { get; }
    public string GroupName => $"pq{_question.Number}";

    public PracticeOptionViewModel(ExamOption option, PracticeQuestionViewModel question)
    {
        Key = option.Key;
        Text = option.Text;
        _question = question;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _question.SelectedKey = Key;
    }
}

/// <summary>
/// One question in a practice set with its chosen answer and result state.
/// </summary>
public sealed partial class PracticeQuestionViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCorrect))]
    [NotifyPropertyChangedFor(nameof(IsWrong))]
    [NotifyPropertyChangedFor(nameof(ResultLabel))]
    private string _selectedKey = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCorrect))]
    [NotifyPropertyChangedFor(nameof(IsWrong))]
    [NotifyPropertyChangedFor(nameof(ResultLabel))]
    private bool _isChecked;

    public int Number { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public string CorrectKey { get; init; } = string.Empty;
    public List<ExamOption> Options { get; init; } = new();
    public ObservableCollection<PracticeOptionViewModel> Choices { get; } = new();

    public string NumberLabel => $"Question {Number}";
    public bool IsCorrect => IsChecked && SelectedKey == CorrectKey;
    public bool IsWrong => IsChecked && SelectedKey != CorrectKey && !string.IsNullOrEmpty(SelectedKey);

    public string ResultLabel => !IsChecked
        ? string.Empty
        : IsCorrect ? "Correct" : IsWrong ? $"Incorrect, answer is {CorrectKey}" : $"No answer, correct is {CorrectKey}";

    public void BuildChoices()
    {
        Choices.Clear();
        foreach (var option in Options)
            Choices.Add(new PracticeOptionViewModel(option, this));
    }

    partial void OnSelectedKeyChanged(string value)
    {
        foreach (var choice in Choices)
            choice.IsSelected = choice.Key == value;
    }

    public void Check() => IsChecked = true;

    public void Clear()
    {
        SelectedKey = string.Empty;
        IsChecked = false;
    }
}

/// <summary>
/// Reading practice: read a passage, answer, then check. Feedback is local
/// so it works fully offline.
/// </summary>
public sealed partial class ReadingViewModel : ObservableObject
{
    private readonly IPracticeRepository _repository;

    [ObservableProperty] private ReadingPassage? _selectedPassage;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<ReadingPassage> Passages { get; } = new();
    public ObservableCollection<PracticeQuestionViewModel> Questions { get; } = new();

    public ReadingViewModel(IPracticeRepository repository)
    {
        _repository = repository;
        LoadPassages();
    }

    public bool HasPassages => Passages.Count > 0;
    public bool ShowNoPassageWarning => !HasPassages;
    public bool HasQuestions => Questions.Count > 0;

    public string Body => SelectedPassage?.Body ?? string.Empty;
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public string SourceLabel => SelectedPassage is null
        ? string.Empty
        : $"{SelectedPassage.Level}. Source: {SelectedPassage.Source}";

    public string NoPassageWarning =>
        $"No reading passages found. Add a .json file under {_repository.ReadingDir}.";

    partial void OnSelectedPassageChanged(ReadingPassage? value) => Rebuild();

    private void LoadPassages()
    {
        Passages.Clear();
        foreach (var passage in _repository.LoadReading())
            Passages.Add(passage);

        if (Passages.Count > 0)
            SelectedPassage = Passages[0];

        OnPropertyChanged(nameof(HasPassages));
        OnPropertyChanged(nameof(ShowNoPassageWarning));
    }

    private void Rebuild()
    {
        Questions.Clear();
        if (SelectedPassage is not null)
        {
            foreach (var q in SelectedPassage.Questions)
            {
                var vm = new PracticeQuestionViewModel
                {
                    Number = q.Number,
                    Prompt = q.Prompt,
                    CorrectKey = q.CorrectKey,
                    Options = q.Options
                };
                vm.BuildChoices();
                Questions.Add(vm);
            }
        }

        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(HasBody));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(HasQuestions));
    }

    [RelayCommand]
    private void CheckAnswers()
    {
        int correct = 0;
        foreach (var q in Questions)
        {
            q.Check();
            if (q.IsCorrect) correct++;
        }

        StatusMessage = Questions.Count == 0
            ? "This passage has no questions."
            : $"You got {correct} of {Questions.Count} correct.";
    }

    [RelayCommand]
    private void ResetAnswers()
    {
        foreach (var q in Questions)
            q.Clear();
        StatusMessage = "Answers cleared.";
    }
}
