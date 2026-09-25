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
/// A single question inside a mock test part.
/// </summary>
public sealed class ExamQuestion
{
    public int Number { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public List<ExamOption> Options { get; set; } = new();
    public string CorrectKey { get; set; } = string.Empty;
}

/// <summary>
/// A part of the test, for example Listening Part 1 or Reading Passage 1.
/// </summary>
public sealed class ExamPart
{
    public string Id { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;      // Listening, Reading, Writing
    public string Title { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;   // passage text or transcript
    public string Instructions { get; set; } = string.Empty;
    public int Minutes { get; set; } = 10;
    public List<ExamQuestion> Questions { get; set; } = new();
}

/// <summary>
/// A full mock test made of ordered parts.
/// </summary>
public sealed class ExamPaper
{
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<ExamPart> Parts { get; set; } = new();
}
