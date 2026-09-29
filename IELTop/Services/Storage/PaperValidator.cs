using System.Text.Json;
using IELTop.Models;

namespace IELTop.Services.Storage;

/// <summary>
/// Structural check for a mock test paper. Pure logic, no I/O, so import,
/// download, and tests share the same rules. Audio files are not checked
/// here because their folders differ per machine.
/// </summary>
public static class PaperValidator
{
    private static readonly string[] Skills = { "Listening", "Reading", "Writing", "Speaking" };
    private const int MaxIssues = 20;

    public static bool TryParse(string json, out ExamPaper? paper, out IReadOnlyList<string> issues)
    {
        paper = null;
        try
        {
            paper = JsonSerializer.Deserialize<ExamPaper>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            issues = new[] { $"Not valid JSON: {ex.Message.Split('.')[0]}." };
            return false;
        }
        issues = Validate(paper);
        return issues.Count == 0 && paper is not null;
    }

    public static IReadOnlyList<string> Validate(ExamPaper? paper)
    {
        var issues = new List<string>();
        if (paper is null)
        {
            issues.Add("The file holds no paper.");
            return issues;
        }
        if (string.IsNullOrWhiteSpace(paper.Title))
            issues.Add("The paper has no title.");
        if (paper.Parts.Count == 0)
            issues.Add("The paper has no parts.");
        foreach (var part in paper.Parts)
            CheckPart(part, issues);
        return issues;
    }

    private static void CheckPart(ExamPart part, List<string> issues)
    {
        string where = string.IsNullOrWhiteSpace(part.Id) ? "a part" : $"part {part.Id}";
        if (string.IsNullOrWhiteSpace(part.Id))
            Add(issues, "A part has no id.");
        if (!Skills.Contains(part.Skill, StringComparer.OrdinalIgnoreCase))
            Add(issues, $"{where} has an unknown skill '{part.Skill}'.");
        if (string.IsNullOrWhiteSpace(part.Title))
            Add(issues, $"{where} has no title.");
        if (part.Minutes <= 0)
            Add(issues, $"{where} has no positive minutes.");

        var numbers = new HashSet<int>();
        foreach (var question in part.Questions)
            CheckQuestion(question, where, numbers, issues);

        bool objective = part.Questions.Count > 0;
        if (!objective && string.IsNullOrWhiteSpace(part.Material))
            Add(issues, $"{where} has no questions and no material to answer from.");
    }

    private static void CheckQuestion(
        ExamQuestion question, string where, HashSet<int> numbers, List<string> issues)
    {
        string label = $"{where}, question {question.Number}";
        if (question.Number <= 0 || !numbers.Add(question.Number))
            Add(issues, $"{label} has a missing or repeated number.");
        if (string.IsNullOrWhiteSpace(question.Prompt))
            Add(issues, $"{label} has no prompt.");

        string kind = (question.Kind ?? "choice").Trim().ToLowerInvariant();
        if (kind == "gap")
        {
            if (string.IsNullOrWhiteSpace(question.GapAnswer))
                Add(issues, $"{label} is a gap with no accepted answer.");
        }
        else if (kind == "match")
        {
            if (question.MatchRows.Count == 0)
                Add(issues, $"{label} is a match with no rows.");
            foreach (var row in question.MatchRows)
            {
                if (string.IsNullOrWhiteSpace(row.Label) || string.IsNullOrWhiteSpace(row.Answer))
                    Add(issues, $"{label} has a match row missing a label or answer.");
                else if (question.Bank.Count > 0 && !question.Bank.Any(b =>
                    string.Equals(b, row.Answer, StringComparison.OrdinalIgnoreCase)))
                    Add(issues, $"{label} answer '{row.Answer}' is not in the bank.");
            }
        }
        else if (kind == "choice")
        {
            if (question.Options.Count < 2)
                Add(issues, $"{label} needs at least two options.");
            else if (!question.Options.Any(o =>
                string.Equals(o.Key, question.CorrectKey, StringComparison.OrdinalIgnoreCase)))
                Add(issues, $"{label} key '{question.CorrectKey}' matches no option.");
        }
        else
        {
            Add(issues, $"{label} has an unknown kind '{question.Kind}'.");
        }
    }

    private static void Add(List<string> issues, string message)
    {
        if (issues.Count < MaxIssues)
            issues.Add(message);
    }
}
