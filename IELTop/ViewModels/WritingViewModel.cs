using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>
/// Writing practice with an optional AI review.
/// The band values are estimates for practice, never official IELTS scores.
/// </summary>
public sealed partial class WritingViewModel : ObservableObject
{
    private readonly IPracticeRepository _repository;
    private readonly IIeltsAiService _ai;

    [ObservableProperty] private WritingTask? _selectedTask;
    [ObservableProperty] private string _essay = string.Empty;
    [ObservableProperty] private string _statusMessage = "Pick a task and start writing.";
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _hasFeedback;
    [ObservableProperty] private double _estimatedBand;
    [ObservableProperty] private double _taskResponse;
    [ObservableProperty] private double _coherence;
    [ObservableProperty] private double _lexicalResource;
    [ObservableProperty] private double _grammar;
    [ObservableProperty] private string _feedbackSummary = string.Empty;
    [ObservableProperty] private string _correctedExcerpt = string.Empty;

    public ObservableCollection<WritingTask> Tasks { get; } = new();
    public ObservableCollection<string> Strengths { get; } = new();
    public ObservableCollection<string> Improvements { get; } = new();

    public WritingViewModel(IPracticeRepository repository, IIeltsAiService ai)
    {
        _repository = repository;
        _ai = ai;
        LoadTasks();
    }

    public bool HasTasks => Tasks.Count > 0;
    public bool ShowNoTaskWarning => !HasTasks;
    public bool CanUseAi => _ai.IsAvailable;
    public bool ShowAiHint => !_ai.IsAvailable;
    public bool HasCorrectedExcerpt => !string.IsNullOrWhiteSpace(CorrectedExcerpt);

    /// <summary>Re-reads AI availability after Settings changes.</summary>
    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(ShowAiHint));
    }

    public string WordCountLabel => $"{CountWords(Essay)} words";
    public int MinimumWords => SelectedTask?.MinimumWords ?? 250;

    public string NoTaskWarning =>
        $"No writing tasks found. Add a .json task under {_repository.WritingDir}.";

    partial void OnEssayChanged(string value) => OnPropertyChanged(nameof(WordCountLabel));

    partial void OnSelectedTaskChanged(WritingTask? value)
    {
        OnPropertyChanged(nameof(MinimumWords));
        StatusMessage = value is null
            ? "Pick a task and start writing."
            : $"{value.TaskType}, {value.Minutes} minutes, at least {value.MinimumWords} words.";
    }

    private void LoadTasks()
    {
        Tasks.Clear();
        foreach (var task in _repository.LoadWriting())
            Tasks.Add(task);

        if (Tasks.Count > 0)
            SelectedTask = Tasks[0];

        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(ShowNoTaskWarning));
    }

    [RelayCommand]
    private async Task ReviewAsync()
    {
        if (SelectedTask is null)
        {
            StatusMessage = "Pick a writing task first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Essay))
        {
            StatusMessage = "Write something before asking for feedback.";
            return;
        }
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings to get feedback.";
            return;
        }

        IsBusy = true;
        StatusMessage = "The model is reviewing your writing.";
        try
        {
            var result = await _ai.ReviewWritingAsync(
                SelectedTask.Prompt, Essay, SelectedTask.MinimumWords);

            if (!result.Success || result.Feedback is null)
            {
                StatusMessage = result.Error;
                return;
            }

            ApplyFeedback(result.Feedback);
            StatusMessage = "Review complete. Bands are practice estimates, not official results.";
        }
        catch (Exception)
        {
            StatusMessage = "The model could not be reached. Check Settings and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFeedback(WritingFeedback f)
    {
        EstimatedBand = f.EstimatedBand;
        TaskResponse = f.TaskResponse;
        Coherence = f.Coherence;
        LexicalResource = f.LexicalResource;
        Grammar = f.Grammar;
        FeedbackSummary = f.Summary;
        CorrectedExcerpt = f.CorrectedExcerpt;

        Strengths.Clear();
        foreach (var s in f.Strengths) Strengths.Add(s);
        Improvements.Clear();
        foreach (var i in f.Improvements) Improvements.Add(i);

        HasFeedback = true;
        OnPropertyChanged(nameof(HasCorrectedExcerpt));
    }

    private static int CountWords(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
