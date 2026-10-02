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
    string AudioDir { get; }
    /// <summary>Deletes a user downloaded paper by title. Shipped papers stay.</summary>
    bool DeleteUserPaper(string title);
    /// <summary>True when the title comes from the user folder.</summary>
    bool IsUserPaper(string title);
    /// <summary>Validates and copies a paper JSON into the user folder.</summary>
    ImportResult ImportPaper(string json);
    /// <summary>Validates and saves a built paper object into the user folder.</summary>
    ImportResult SavePaper(ExamPaper paper);
    /// <summary>Overwrites one user paper, renaming the file when the title changed.</summary>
    ImportResult UpdatePaper(string originalTitle, ExamPaper paper);
    /// <summary>One paper by title, or null when missing.</summary>
    ExamPaper? GetPaper(string title);
    /// <summary>Writes papers plus their audio clips into a fresh folder.</summary>
    ExportResult ExportPapers(IEnumerable<string> titles, string folder);
}

/// <summary>Outcome of one paper import.</summary>
public sealed record ImportResult(bool Success, string Title, string Error)
{
    public static ImportResult Ok(string title) => new(true, title, string.Empty);
    public static ImportResult Fail(string error) => new(false, string.Empty, error);
}

/// <summary>Outcome of a bulk export.</summary>
public sealed record ExportResult(bool Success, int PaperCount, int AudioCount, string Folder, string Error)
{
    public static ExportResult Ok(int papers, int audio, string folder)
        => new(true, papers, audio, folder, string.Empty);
    public static ExportResult Fail(string error) => new(false, 0, 0, string.Empty, error);
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

    public string AudioDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content", "Audio");

    public bool DeleteUserPaper(string title)
    {
        var file = FindUserPaper(title);
        if (file is null) return false;
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool IsUserPaper(string title) => FindUserPaper(title) is not null;

    public ImportResult ImportPaper(string json)
    {
        if (!PaperValidator.TryParse(json, out var paper, out var issues) || paper is null)
            return ImportResult.Fail(issues.Count > 0 ? issues[0] : "The file holds no paper.");

        return SavePaper(paper);
    }

    public ImportResult SavePaper(ExamPaper paper)
    {
        var issues = PaperValidator.Validate(paper);
        if (issues.Count > 0)
            return ImportResult.Fail(issues[0]);
        try
        {
            Directory.CreateDirectory(UserContentDir);
            var json = JsonSerializer.Serialize(paper,
                new JsonSerializerOptions { WriteIndented = true });
            var path = Path.Combine(UserContentDir, UniqueFileName(SuggestFileName(paper.Title)) + ".json");
            File.WriteAllText(path, json);
            return ImportResult.Ok(paper.Title);
        }
        catch (Exception)
        {
            return ImportResult.Fail("Could not save the paper. Check disk space and try again.");
        }
    }

    public ExamPaper? GetPaper(string title)
        => LoadPapers().FirstOrDefault(p =>
            string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));

    public ImportResult UpdatePaper(string originalTitle, ExamPaper paper)
    {
        var issues = PaperValidator.Validate(paper);
        if (issues.Count > 0)
            return ImportResult.Fail(issues[0]);
        try
        {
            Directory.CreateDirectory(UserContentDir);
            var oldFile = FindUserPaper(originalTitle);
            if (oldFile is null)
                return SavePaper(paper);
            // Same title: overwrite the same file. New title: save new file, delete old.
            if (string.Equals(originalTitle, paper.Title, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(oldFile, JsonSerializer.Serialize(paper,
                    new JsonSerializerOptions { WriteIndented = true }));
                return ImportResult.Ok(paper.Title);
            }
            var result = SavePaper(paper);
            if (!result.Success) return result;
            try { File.Delete(oldFile); } catch (Exception) { }
            return result;
        }
        catch (Exception)
        {
            return ImportResult.Fail("Could not save the paper. Check disk space and try again.");
        }
    }

    public ExportResult ExportPapers(IEnumerable<string> titles, string folder)
    {
        var wanted = new HashSet<string>(titles, StringComparer.OrdinalIgnoreCase);
        var papers = LoadPapers().Where(p => wanted.Contains(p.Title)).ToList();
        if (papers.Count == 0)
            return ExportResult.Fail("No papers match the export list.");

        try
        {
            Directory.CreateDirectory(folder);
            var audioDir = Path.Combine(folder, "Audio");
            int audio = 0;
            foreach (var paper in papers)
            {
                var path = Path.Combine(folder, UniqueExportName(folder, SuggestFileName(paper.Title)) + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(paper,
                    new JsonSerializerOptions { WriteIndented = true }));
                foreach (var clip in paper.Parts
                    .Where(p => !string.IsNullOrWhiteSpace(p.AudioFile))
                    .Select(p => p.AudioFile!)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var source = ResolveAudio(clip);
                    if (source is null) continue;
                    Directory.CreateDirectory(audioDir);
                    File.Copy(source, Path.Combine(audioDir, Path.GetFileName(clip)), overwrite: true);
                    audio++;
                }
            }
            return ExportResult.Ok(papers.Count, audio, folder);
        }
        catch (Exception)
        {
            return ExportResult.Fail("Could not write the export folder. Check disk space and try again.");
        }
    }

    /// <summary>File stem for a paper id or title, without extension.</summary>
    public static string SuggestFileName(string idOrTitle)
    {
        var clean = new string(idOrTitle.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        clean = string.Join("-", clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return "paper";
        return clean.Length > 60 ? clean[..60] : clean;
    }

    private string UniqueFileName(string stem)
    {
        string name = stem;
        for (int n = 2; File.Exists(Path.Combine(UserContentDir, name + ".json")); n++)
            name = $"{stem}-{n}";
        return name;
    }

    private static string UniqueExportName(string folder, string stem)
    {
        string name = stem;
        for (int n = 2; File.Exists(Path.Combine(folder, name + ".json")); n++)
            name = $"{stem}-{n}";
        return name;
    }

    private static string? ResolveAudio(string clip)
    {
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "content", "Audio", clip);
        if (File.Exists(userPath)) return userPath;
        var shippedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", clip);
        return File.Exists(shippedPath) ? shippedPath : null;
    }

    private string? FindUserPaper(string title)
    {
        if (!Directory.Exists(UserContentDir)) return null;
        foreach (var file in Directory.EnumerateFiles(UserContentDir, "*.json"))
        {
            try
            {
                var paper = JsonSerializer.Deserialize<ExamPaper>(File.ReadAllText(file), _options);
                if (paper is not null && string.Equals(paper.Title, title, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            catch (Exception)
            {
                // A broken file is left alone here and skipped at load time.
            }
        }
        return null;
    }

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
