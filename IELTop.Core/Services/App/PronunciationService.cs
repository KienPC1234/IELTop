using System.IO;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;

namespace IELTop.Services.App;

/// <summary>One word the model flagged, with what was expected and what it heard.</summary>
public sealed record PronunciationWord(string Word, string Expected, string Heard);

/// <summary>
/// Pronunciation check for one recording, ready for the UI. Accuracy is the
/// share of phonemes the model matched. It is a practice estimate, never an
/// official pronunciation score.
/// </summary>
public sealed record PronunciationResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public double Accuracy { get; init; }
    public int Substitutions { get; init; }
    public int Omissions { get; init; }
    public int Insertions { get; init; }
    public int Correct { get; init; }
    public IReadOnlyList<PronunciationWord> Words { get; init; } = Array.Empty<PronunciationWord>();
    public string HeardPhonemes { get; init; } = string.Empty;
    public string ExpectedPhonemes { get; init; } = string.Empty;

    public string Summary => !Success
        ? Error
        : Accuracy >= 90 ? $"Clear pronunciation, {Accuracy:0}% match."
        : Accuracy >= 75 ? $"Good, {Accuracy:0}% match. A few sounds to tighten."
        : Accuracy >= 50 ? $"Fair, {Accuracy:0}% match. Practise the marked words."
        : $"Needs work, {Accuracy:0}% match. Slow down and repeat the marked words.";
}

public interface IPronunciationService
{
    /// <summary>True when the offline pronunciation model is installed.</summary>
    bool IsAvailable { get; }

    /// <summary>Scores a recording against the words the student meant to say.</summary>
    Task<PronunciationResult> AssessAsync(string wavPath, string targetText, CancellationToken ct = default);
}

/// <summary>
/// Wraps the phoneme model in a shape the UI can show, and records the result
/// in the local database so the dashboard counts a speaking practice.
/// </summary>
public sealed class PronunciationService : IPronunciationService
{
    private readonly IMddPhonemeService _mdd;

    public PronunciationService(IMddPhonemeService mdd)
    {
        _mdd = mdd;
    }

    public bool IsAvailable => _mdd.IsModelAvailable();

    public async Task<PronunciationResult> AssessAsync(string wavPath, string targetText, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new PronunciationResult { Success = false, Error = "The pronunciation model is not installed." };
        if (string.IsNullOrWhiteSpace(targetText))
            return new PronunciationResult { Success = false, Error = "Type what you said first, so the check has a target." };
        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
            return new PronunciationResult { Success = false, Error = "Record your answer first." };

        try
        {
            var r = await _mdd.AssessAsync(wavPath, targetText, ct);
            if (!r.Success)
                return new PronunciationResult { Success = false, Error = r.Error };

            var words = r.Words
                .Where(w => w.HasErrors)
                .Select(w => new PronunciationWord(w.Word, w.Expected, w.Heard))
                .ToList();

            SaveAttempt(r, targetText, wavPath);

            return new PronunciationResult
            {
                Success = true,
                Accuracy = r.Accuracy,
                Substitutions = r.Substitutions,
                Omissions = r.Omissions,
                Insertions = r.Insertions,
                Correct = r.Correct,
                Words = words,
                HeardPhonemes = r.HeardPhonemes,
                ExpectedPhonemes = r.ExpectedPhonemes,
            };
        }
        catch (OperationCanceledException)
        {
            return new PronunciationResult { Success = false, Error = "The check was stopped." };
        }
        catch (Exception)
        {
            return new PronunciationResult { Success = false, Error = "The check could not finish. Try again." };
        }
    }

    private static void SaveAttempt(MddResult r, string target, string wavPath)
    {
        try
        {
            using var db = new AppDbContext();
            db.SpeakingAttempts.Add(new SpeakingAttempt
            {
                TargetText = target.Length > 500 ? target[..500] : target,
                HeardPhonemes = r.HeardPhonemes.Length > 2000 ? r.HeardPhonemes[..2000] : r.HeardPhonemes,
                Substitutions = r.Substitutions,
                Omissions = r.Omissions,
                Insertions = r.Insertions,
                Accuracy = r.Accuracy,
                AudioPath = wavPath,
                CreatedAt = DateTime.Now,
            });
            db.SaveChanges();
        }
        catch (Exception)
        {
            // History must never block the check result.
        }
    }
}
