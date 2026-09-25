using System.IO;
using System.Text.Json;
using IELTop.Models;

namespace IELTop.Services.Storage;

/// <summary>
/// Loads mock test papers from the user folder and the shipped Assets/Exams.
/// Imported or edited papers are kept in the user folder so they survive updates.
/// </summary>
public interface IExamRepository
{
    IReadOnlyList<ExamPaper> LoadPapers();
    string ExamsDir { get; }
}

public sealed class ExamRepository : IExamRepository
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static string ShippedDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Exams");

    private static string UserContentDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content", "Exams");

    public string ExamsDir => UserContentDir;

    public IReadOnlyList<ExamPaper> LoadPapers()
    {
        var byTitle = new Dictionary<string, ExamPaper>(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(UserContentDir);
        ReadFolder(ShippedDir, byTitle);
        ReadFolder(UserContentDir, byTitle);

        return byTitle.Values
            .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ReadFolder(string folder, Dictionary<string, ExamPaper> into)
    {
        // The shipped folder may sit under Program Files and may not exist. Never create it.
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var paper = JsonSerializer.Deserialize<ExamPaper>(File.ReadAllText(file), _options);
                if (paper is null || paper.Parts.Count == 0) continue;
                into[paper.Title] = paper;
            }
            catch (JsonException)
            {
                // A malformed paper should not stop the rest from loading.
            }
        }
    }
}
