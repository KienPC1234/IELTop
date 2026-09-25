using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Audio;

namespace IELTop.ViewModels;

/// <summary>
/// Speaking practice: read a sample sentence, record, then score phonemes
/// and list concrete mistakes. Heavy work stays async so the UI keeps moving.
/// </summary>
public sealed partial class SpeakingViewModel : ObservableObject
{
    private readonly IAudioService _audio;
    private readonly IMddPhonemeService _mdd;
    private readonly IG2PService _g2p;
    private readonly IIeltsAiService _ai;

    [ObservableProperty] private string _targetText = "I think this is a very good idea.";
    [ObservableProperty] private string _resultSummary = "Press Record, then read the sample sentence to begin.";
    [ObservableProperty] private string _heardPhonemes = string.Empty;
    [ObservableProperty] private string _expectedPhonemes = string.Empty;
    [ObservableProperty] private double _accuracy;
    [ObservableProperty] private int _substitutions;
    [ObservableProperty] private int _omissions;
    [ObservableProperty] private int _insertions;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _aiCoaching = string.Empty;

    public ObservableCollection<string> Mistakes { get; } = new();

    public SpeakingViewModel(IAudioService audio, IMddPhonemeService mdd, IG2PService g2p, IIeltsAiService ai)
    {
        _audio = audio;
        _mdd = mdd;
        _g2p = g2p;
        _ai = ai;
    }

    public bool HasModel => _mdd.IsModelAvailable();

    public bool ShowModelWarning => !HasModel;

    public bool CanUseAi => _ai.IsAvailable;
    public bool ShowAiHint => !_ai.IsAvailable;
    public bool HasAiCoaching => !string.IsNullOrWhiteSpace(AiCoaching);

    /// <summary>Re-reads AI availability after Settings changes.</summary>
    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(ShowAiHint));
    }

    [RelayCommand]
    private async Task RecordAndAssessAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(TargetText))
        {
            ResultSummary = "Enter a sample sentence before you record.";
            return;
        }

        IsBusy = true;
        Mistakes.Clear();
        StatusMessage = "Recording for 5 seconds.";
        ResultSummary = "Recording now. Read the sample sentence aloud.";
        try
        {
            var wav = await _audio.RecordAsync(5);
            StatusMessage = "Scoring pronunciation.";
            var result = await _mdd.AssessAsync(wav, TargetText);

            if (!result.Success)
            {
                ResultSummary = result.Error;
                StatusMessage = "Scoring did not finish.";
                return;
            }

            HeardPhonemes = result.HeardPhonemes;
            ExpectedPhonemes = result.ExpectedPhonemes;
            Accuracy = result.Accuracy;
            Substitutions = result.Substitutions;
            Omissions = result.Omissions;
            Insertions = result.Insertions;
            StatusMessage = "Scoring complete.";
            ResultSummary = $"Accuracy {result.Accuracy:0.#}%, correct {result.Correct} of {result.Total} sounds.";

            AppendMistakes(result);
            SaveAttempt(TargetText, wav, result);
        }
        catch (Exception)
        {
            ResultSummary = "Something went wrong while recording. Check your microphone and try again.";
            StatusMessage = "No audio was captured.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowExpected()
    {
        var expected = _g2p.SentenceToPhonemes(TargetText);
        ExpectedPhonemes = _g2p.ToDisplay(expected);
        StatusMessage = "Target pronunciation updated.";
    }

    [RelayCommand]
    private async Task CoachWithAiAsync()
    {
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings to get spoken coaching.";
            return;
        }
        if (string.IsNullOrWhiteSpace(HeardPhonemes))
        {
            StatusMessage = "Record and score a sentence first.";
            return;
        }

        IsBusy = true;
        AiCoaching = string.Empty;
        StatusMessage = "The model is preparing tips.";
        try
        {
            var mistakes = Mistakes.Count == 0 ? "none" : string.Join("; ", Mistakes);
            await foreach (var chunk in _ai.CoachSpeakingAsync(TargetText, HeardPhonemes, mistakes))
                AiCoaching += chunk;
            StatusMessage = "Coaching ready.";
        }
        catch (Exception)
        {
            StatusMessage = "The model could not be reached. Check Settings.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasAiCoaching));
        }
    }

    private void AppendMistakes(MddResult result)
    {
        if (result.Substitutions == 0 && result.Omissions == 0 && result.Insertions == 0)
        {
            Mistakes.Add("Clear pronunciation. No errors detected.");
            return;
        }
        foreach (var edit in result.Edits.Where(e => e.Type != PhonemeErrorType.Correct))
            Mistakes.Add(Describe(edit));
    }

    private static string Describe(PhonemeEdit edit) => edit.Type switch
    {
        PhonemeErrorType.Substitution => $"Position {edit.Position + 1}: said /{edit.Heard}/ instead of /{edit.Expected}/",
        PhonemeErrorType.Omission => $"Position {edit.Position + 1}: dropped the sound /{edit.Expected}/",
        PhonemeErrorType.Insertion => $"Position {edit.Position + 1}: added the sound /{edit.Heard}/ that is not in the sentence",
        _ => string.Empty
    };

    private static void SaveAttempt(string target, string wav, MddResult result)
    {
        try
        {
            using var db = new AppDbContext();
            db.SpeakingAttempts.Add(new SpeakingAttempt
            {
                TargetText = target,
                HeardPhonemes = result.HeardPhonemes,
                Substitutions = result.Substitutions,
                Omissions = result.Omissions,
                Insertions = result.Insertions,
                Accuracy = result.Accuracy,
                AudioPath = wav
            });
            db.SaveChanges();
        }
        catch
        {
            // A history write failure must not block the scoring result the student just saw.
        }
    }
}
