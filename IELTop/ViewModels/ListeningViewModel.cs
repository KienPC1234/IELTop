using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
using IELTop.Services.Audio;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>
/// Listening practice: play the clip, answer questions, check, then read the
/// transcript. Missing audio is reported clearly instead of failing silently.
/// </summary>
public sealed partial class ListeningViewModel : ObservableObject
{
    private readonly IPracticeRepository _repository;
    private readonly IAudioService _audio;

    [ObservableProperty] private ListeningItem? _selectedItem;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _showTranscript;

    public ObservableCollection<ListeningItem> Items { get; } = new();
    public ObservableCollection<PracticeQuestionViewModel> Questions { get; } = new();

    public ListeningViewModel(IPracticeRepository repository, IAudioService audio)
    {
        _repository = repository;
        _audio = audio;
        LoadItems();
    }

    public bool HasItems => Items.Count > 0;
    public bool ShowNoItemWarning => !HasItems;
    public bool HasQuestions => Questions.Count > 0;
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);
    public string Transcript => SelectedItem?.Transcript ?? string.Empty;

    public string SourceLabel => SelectedItem is null
        ? string.Empty
        : $"{SelectedItem.Level}. Source: {SelectedItem.Source}";

    public string NoItemWarning =>
        $"No listening items found. Add a .json file and audio under {_repository.ListeningDir} and {_repository.AudioDir}.";

    public string AudioPath => SelectedItem is null
        ? string.Empty
        : Path.Combine(_repository.AudioDir, SelectedItem.AudioFile);

    public bool AudioExists => !string.IsNullOrWhiteSpace(AudioPath) && File.Exists(AudioPath);

    public string AudioStatus => AudioExists
        ? $"Audio: {SelectedItem!.AudioFile}"
        : $"Audio file missing: {SelectedItem?.AudioFile}. Add it under Assets/Audio.";

    partial void OnSelectedItemChanged(ListeningItem? value) => Rebuild();

    private void LoadItems()
    {
        Items.Clear();
        foreach (var item in _repository.LoadListening())
            Items.Add(item);

        if (Items.Count > 0)
            SelectedItem = Items[0];

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowNoItemWarning));
    }

    private void Rebuild()
    {
        Questions.Clear();
        ShowTranscript = false;

        if (SelectedItem is not null)
        {
            foreach (var q in SelectedItem.Questions)
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
        OnPropertyChanged(nameof(HasQuestions));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(Transcript));
        OnPropertyChanged(nameof(HasTranscript));
        OnPropertyChanged(nameof(AudioPath));
        OnPropertyChanged(nameof(AudioExists));
        OnPropertyChanged(nameof(AudioStatus));
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (SelectedItem is null) return;
        if (!AudioExists)
        {
            StatusMessage = "The audio file for this item is missing. Add it under Assets/Audio.";
            return;
        }

        IsPlaying = true;
        StatusMessage = "Playing.";
        try
        {
            await _audio.PlayAsync(AudioPath);
            StatusMessage = "Playback finished.";
        }
        catch (Exception)
        {
            StatusMessage = "The audio file could not be played.";
        }
        finally
        {
            IsPlaying = false;
        }
    }

    [RelayCommand]
    private void ToggleTranscript()
    {
        ShowTranscript = !ShowTranscript;
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
            ? "This item has no questions."
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
