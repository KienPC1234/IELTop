namespace IELTop.Models;

/// <summary>
/// One answer choice in a mock test question.
/// </summary>
public sealed class ExamOption
{
    public string Key { get; set; } = string.Empty;   // A, B, C, D
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A single question inside a mock test part. Kind is choice (options with
/// CorrectKey) or gap (type the answer, checked against GapAnswer with |
/// separated alternatives). Explanation is the reason the correct answer is
/// right, shown for Reading review. Old papers without the new fields
/// still load.
/// </summary>
public sealed class ExamQuestion
{
    public int Number { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Kind { get; set; } = "choice";
    public List<ExamOption> Options { get; set; } = new();
    public string CorrectKey { get; set; } = string.Empty;
    public string GapAnswer { get; set; } = string.Empty;
    public List<ExamMatchRow> MatchRows { get; set; } = new();
    public List<string> Bank { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// One row of a matching question: which bank item belongs to this label.
/// </summary>
public sealed class ExamMatchRow
{
    public string Label { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// A part of the test, for example Listening Part 1 or Reading Passage 1.
/// Topic and TaskType describe the subject and format, for example
/// Writing Task 2 Opinion or Speaking Part 2 Cue Card. AudioFile is the
/// Listening clip name under Assets/Audio. PrepSeconds is the thinking
/// time before a Speaking cue. Old papers without these fields still load.
/// </summary>
public sealed class ExamPart
{
    public string Id { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;      // Listening, Reading, Writing, Speaking
    public string Title { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;   // e.g. Opinion, Cue Card, Part 1
    public string Material { get; set; } = string.Empty;   // passage text or transcript
    public string Instructions { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public int Minutes { get; set; } = 10;
    public int PrepSeconds { get; set; }
    public List<ExamQuestion> Questions { get; set; } = new();
}

/// <summary>
/// A full mock test made of ordered parts. Category groups papers on
/// content servers, for example Academic or General. Level names the
/// target band range, for example 5.0-6.5. Old papers without them load.
/// </summary>
public sealed class ExamPaper
{
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<ExamPart> Parts { get; set; } = new();
}
