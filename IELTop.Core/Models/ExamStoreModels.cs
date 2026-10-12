using System;
using SQLite;

namespace IELTop.Models;

/// <summary>
/// Runtime paper store. Shipped JSON files under Assets/Exams seed these
/// tables on first use; user imports and edits live only here. JSON stays
/// the interchange format for import and export, never the live store.
/// </summary>
[Table("ExamPapers")]
public sealed class StoredPaper
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Title { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;

    /// <summary>JSON array of tags.</summary>
    public string TagsJson { get; set; } = "[]";

    /// <summary>shipped or user. A user row with the same title shadows shipped.</summary>
    [Indexed]
    public string Origin { get; set; } = "user";

    /// <summary>Hash of the shipped file the row was seeded from.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("ExamParts")]
public sealed class StoredPart
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PaperId { get; set; }

    public int Order { get; set; }
    public string PartCode { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public string ImageFile { get; set; } = string.Empty;
    public int Minutes { get; set; } = 10;
    public int PrepSeconds { get; set; }
}

[Table("ExamQuestions")]
public sealed class StoredQuestion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PartId { get; set; }

    public int Number { get; set; }
    public string Kind { get; set; } = "choice";
    public string Prompt { get; set; } = string.Empty;
    public string CorrectKey { get; set; } = string.Empty;
    public string GapAnswer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;

    /// <summary>JSON array of {key,text} options.</summary>
    public string OptionsJson { get; set; } = "[]";

    /// <summary>JSON array of {label,answer} match rows.</summary>
    public string MatchRowsJson { get; set; } = "[]";

    /// <summary>JSON array of bank strings for match questions.</summary>
    public string BankJson { get; set; } = "[]";
}
