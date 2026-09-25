namespace IELTop.Models;

/// <summary>
/// A reading practice item: a passage with comprehension questions.
/// </summary>
public sealed class ReadingPassage
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Level { get; set; } = "Intermediate";
    public string Body { get; set; } = string.Empty;
    public List<ExamQuestion> Questions { get; set; } = new();
}

/// <summary>
/// A listening practice item: an audio clip, an optional transcript, and questions.
/// </summary>
public sealed class ListeningItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Level { get; set; } = "Intermediate";

    /// <summary>File name inside Assets/Audio. May be missing, the UI says so clearly.</summary>
    public string AudioFile { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public List<ExamQuestion> Questions { get; set; } = new();
}

/// <summary>
/// A writing practice task with a prompt and a minimum word count.
/// </summary>
public sealed class WritingTask
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string TaskType { get; set; } = "Task 2";
    public int Minutes { get; set; } = 40;
    public int MinimumWords { get; set; } = 250;
    public string Prompt { get; set; } = string.Empty;
    public List<string> Tips { get; set; } = new();
}
