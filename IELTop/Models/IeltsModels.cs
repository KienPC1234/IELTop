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
