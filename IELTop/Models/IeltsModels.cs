namespace IELTop.Models;

/// <summary>
/// A vocabulary entry stored in the local SQLite database.
/// </summary>
public sealed class VocabularyWord
{
    public int Id { get; set; }
    public string Word { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public string? Example { get; set; }
    public string? Topic { get; set; }
    public int Level { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Review progress for a word, using a reduced SM-2 schedule.
/// </summary>
public sealed class StudyRecord
{
    public int Id { get; set; }
    public int VocabularyWordId { get; set; }
    public VocabularyWord? Word { get; set; }
    public int Repetitions { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public int IntervalDays { get; set; }
    public DateTime NextReview { get; set; } = DateTime.UtcNow;
    public DateTime LastReviewed { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A scored mock test attempt, kept so the student can track progress.
/// Bands are stored as a range, because examiners vary. They are practice
/// estimates, never official IELTS scores. WritingBand and SpeakingBand
/// are the AI marked bands for those skills when a model was used.
/// </summary>
public sealed class ExamAttempt
{
    public int Id { get; set; }
    public string PaperTitle { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Strictness { get; set; } = string.Empty;
    public double BandLow { get; set; }
    public double BandHigh { get; set; }
    public int Correct { get; set; }
    public int Total { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int Violations { get; set; }

    /// <summary>AI marked Writing band, 0 when not marked.</summary>
    public double WritingBand { get; set; }

    /// <summary>AI marked Speaking band, 0 when not marked.</summary>
    public double SpeakingBand { get; set; }

    /// <summary>Full AI feedback text from the run, for the detail view.</summary>
    public string AiFeedback { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A scored speaking attempt, kept so the student can track progress.
/// </summary>
public sealed class SpeakingAttempt
{
    public int Id { get; set; }
    public string TargetText { get; set; } = string.Empty;
    public string HeardPhonemes { get; set; } = string.Empty;
    public int Substitutions { get; set; }
    public int Omissions { get; set; }
    public int Insertions { get; set; }
    public double Accuracy { get; set; }
    public string AudioPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
