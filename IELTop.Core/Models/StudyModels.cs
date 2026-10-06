using System;
using System.Collections.Generic;
using System.Text.Json;
using SQLite;

namespace IELTop.Models;

/// <summary>
/// One study session: a chat thread, a practice set, or a speaking session.
/// Sessions are kept so the student can find a topic again and read the whole
/// history instead of losing it when the window closes.
/// </summary>
[Table("StudySessions")]
public sealed class StudySession
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Chat, Practice, or Speaking.</summary>
    [Indexed]
    public string Kind { get; set; } = "Chat";

    /// <summary>Lesson unit slug the session draws on, empty for free topics.</summary>
    public string Unit { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    /// <summary>Pinned sessions sort first, across chat and practice alike.</summary>
    public bool Pinned { get; set; }

    [Indexed]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One message in a chat session. Sources are the lesson citations.</summary>
[Table("ChatMessages")]
public sealed class ChatMessage
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SessionId { get; set; }

    /// <summary>user or assistant.</summary>
    public string Role { get; set; } = "user";

    public string Text { get; set; } = string.Empty;

    /// <summary>JSON array of citations, each with a unit, section and snippet.</summary>
    public string SourcesJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A saved practice set. Every question keeps the student's answer and the
/// verdict, so the detail view can be reopened exactly as it was scored.
/// </summary>
[Table("PracticeSets")]
public sealed class PracticeSet
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    [Indexed]
    public int SessionId { get; set; }

    public string Skill { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public int QuestionCount { get; set; }

    public int Score { get; set; }

    public int Total { get; set; }

    /// <summary>Open, Submitted.</summary>
    public string Status { get; set; } = "Open";

    [Indexed]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One question inside a practice set, with the answer the student gave.</summary>
[Table("PracticeQuestions")]
public sealed class PracticeQuestion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SetId { get; set; }

    public int Number { get; set; }

    /// <summary>gap, single, multiple, match, short.</summary>
    public string Kind { get; set; } = "gap";

    public string Prompt { get; set; } = string.Empty;

    /// <summary>JSON array of { key, text } for choice questions.</summary>
    public string OptionsJson { get; set; } = string.Empty;

    /// <summary>JSON array of { label, answer } for a match question.</summary>
    public string MatchRowsJson { get; set; } = string.Empty;

    public string CorrectKey { get; set; } = string.Empty;

    /// <summary>Accepted answers for a gap, separated by |.</summary>
    public string GapAnswer { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    public string UserAnswer { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    public bool IsFlagged { get; set; }

    [Ignore]
    public IReadOnlyList<PracticeOption> Options =>
        string.IsNullOrEmpty(OptionsJson)
            ? Array.Empty<PracticeOption>()
            : JsonSerializer.Deserialize<List<PracticeOption>>(OptionsJson) ?? new List<PracticeOption>();
}

public sealed record PracticeOption(string Key, string Text);

/// <summary>
/// One tutor speaking attempt: a cue answer or a read aloud turn, with the
/// measurements the app could take itself (pace, pauses, clarity or GOP) and
/// the optional AI feedback. The wav stays on disk, only its path is stored.
/// </summary>
[Table("TutorSpeakingAttempts")]
public sealed class TutorSpeakingAttempt
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SessionId { get; set; }

    /// <summary>Answer or ReadAloud.</summary>
    public string Mode { get; set; } = "Answer";

    /// <summary>Part1, Part2 or Part3 for answers; empty for read aloud.</summary>
    public string Part { get; set; } = string.Empty;

    public string Cue { get; set; } = string.Empty;

    public string Transcript { get; set; } = string.Empty;

    public double WordsPerMinute { get; set; }

    public double SpeechSeconds { get; set; }

    public int PauseCount { get; set; }

    public double MeanPauseSeconds { get; set; }

    /// <summary>
    /// Mean frame confidence of the phoneme model on free speech, 0 to 1.
    /// Empty for read aloud, which uses PronunciationAccuracy instead.
    /// </summary>
    public double Clarity { get; set; }

    public double PronunciationAccuracy { get; set; }

    public double MeanGop { get; set; }

    public string AiFeedback { get; set; } = string.Empty;

    public string AudioPath { get; set; } = string.Empty;

    [Indexed]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
