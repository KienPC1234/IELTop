using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace IELTop.ViewModels;

/// <summary>
/// One word shown in the review list.
/// </summary>
public sealed partial class VocabularyItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRevealed;

    public int Id { get; init; }
    public string Word { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public string? Example { get; init; }
    public string? Topic { get; init; }
    public int Level { get; init; }
    public string LevelLabel => $"Level {Level}";

    public bool HasExample => !string.IsNullOrWhiteSpace(Example);
}

/// <summary>
/// Vocabulary list with add, delete, and a simple spaced repetition review.
/// Every database call is short lived so the UI stays responsive.
/// </summary>
public sealed partial class VocabularyViewModel : ObservableObject
{
    private readonly IIeltsAiService _ai;

    [ObservableProperty] private string _newWord = string.Empty;
    [ObservableProperty] private string _newMeaning = string.Empty;
    [ObservableProperty] private string _newExample = string.Empty;
    [ObservableProperty] private string _newTopic = string.Empty;
    [ObservableProperty] private int _newLevel = 1;
    [ObservableProperty] private string _statusMessage = "Add a word or start a review.";
    [ObservableProperty] private string _aiExplanation = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<VocabularyItemViewModel> Words { get; } = new();

    public VocabularyViewModel(IIeltsAiService ai)
    {
        _ai = ai;
        Load();
    }

    public bool IsEmpty => Words.Count == 0;
    public bool HasAiExplanation => !string.IsNullOrWhiteSpace(AiExplanation);
    public bool CanUseAi => _ai.IsAvailable;
    public bool ShowAiHint => !_ai.IsAvailable;

    /// <summary>Re-reads AI availability after Settings changes.</summary>
    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(ShowAiHint));
    }

    [RelayCommand]
    private void Load()
    {
        Words.Clear();
        try
        {
            using var db = new AppDbContext();
            foreach (var word in db.Words.OrderBy(w => w.Word).ToList())
            {
                Words.Add(new VocabularyItemViewModel
                {
                    Id = word.Id,
                    Word = word.Word,
                    Meaning = word.Meaning,
                    Example = word.Example,
                    Topic = word.Topic,
                    Level = word.Level
                });
            }
            StatusMessage = Words.Count == 0
                ? "No words yet. Add your first word above."
                : $"{Words.Count} word(s) in your list.";
        }
        catch (Exception)
        {
            StatusMessage = "The word list could not be opened. Restart the app and try again.";
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void AddWord()
    {
        if (string.IsNullOrWhiteSpace(NewWord) || string.IsNullOrWhiteSpace(NewMeaning))
        {
            StatusMessage = "Enter both the word and its meaning.";
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var existing = db.Words.Any(w => w.Word == NewWord.Trim());
            if (existing)
            {
                StatusMessage = $"{NewWord.Trim()} is already in your list.";
                return;
            }

            db.Words.Add(new VocabularyWord
            {
                Word = NewWord.Trim(),
                Meaning = NewMeaning.Trim(),
                Example = string.IsNullOrWhiteSpace(NewExample) ? null : NewExample.Trim(),
                Topic = string.IsNullOrWhiteSpace(NewTopic) ? null : NewTopic.Trim(),
                Level = NewLevel
            });
            db.SaveChanges();

            NewWord = string.Empty;
            NewMeaning = string.Empty;
            NewExample = string.Empty;
            NewTopic = string.Empty;
            NewLevel = 1;
            Load();
        }
        catch (Exception)
        {
            StatusMessage = "The word could not be saved. Try again.";
        }
    }

    [RelayCommand]
    private void DeleteWord(VocabularyItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            using var db = new AppDbContext();
            var word = db.Words.FirstOrDefault(w => w.Id == item.Id);
            if (word is not null)
            {
                db.Words.Remove(word);
                db.SaveChanges();
            }
            Load();
        }
        catch (Exception)
        {
            StatusMessage = "The word could not be removed. Try again.";
        }
    }

    [RelayCommand]
    private void ToggleReveal(VocabularyItemViewModel? item)
    {
        if (item is null) return;
        item.IsRevealed = !item.IsRevealed;
    }

    [RelayCommand]
    private async Task ExplainWithAiAsync()
    {
        var word = string.IsNullOrWhiteSpace(NewWord) ? Words.FirstOrDefault()?.Word : NewWord;
        if (string.IsNullOrWhiteSpace(word))
        {
            StatusMessage = "Enter or pick a word first.";
            return;
        }
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings to use AI explanations.";
            return;
        }

        IsBusy = true;
        AiExplanation = string.Empty;
        StatusMessage = "Asking the model.";
        try
        {
            await foreach (var chunk in _ai.ExplainWordAsync(word))
                AiExplanation += chunk;
            StatusMessage = "Explanation ready.";
        }
        catch (Exception)
        {
            StatusMessage = "The model could not be reached. Check Settings.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasAiExplanation));
        }
    }
}
