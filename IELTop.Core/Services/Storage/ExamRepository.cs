using System.IO;
using System.Text.Json;
using IELTop.Data;
using IELTop.Models;
using SQLite;

namespace IELTop.Services.Storage;

/// <summary>
/// Paper library backed by SQLite. Shipped papers, user imports, and edits
/// all live in the ExamPapers/ExamParts/ExamQuestions tables.
/// </summary>
public interface IExamRepository
{
    IReadOnlyList<ExamPaper> LoadPapers();
    string ExamsDir { get; }
    string AudioDir { get; }
    /// <summary>Deletes a user paper by title. Shipped papers stay.</summary>
    bool DeleteUserPaper(string title);
    /// <summary>True when the title is a user paper.</summary>
    bool IsUserPaper(string title);
    /// <summary>Validates a paper JSON and stores it as a user paper.</summary>
    ImportResult ImportPaper(string json);
    /// <summary>Validates a built paper object and stores it as a user paper.</summary>
    ImportResult SavePaper(ExamPaper paper);
    /// <summary>Overwrites one user paper, replacing the row when the title changed.</summary>
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

/// <summary>
/// Paper library from two SQLite files. The repo template (papers-seed.db,
/// generated at build from the JSON seeds) is read-only and serves shipped
/// papers; it is never written at runtime. The user file (user-papers.db)
/// holds imports, downloads, edits, and shares, and is the only file this
/// class writes paper rows to. JSON stays the import/export format.
/// </summary>
public sealed class ExamRepository : IExamRepository
{
    private const string ShippedOrigin = "shipped";
    private const string UserOrigin = "user";
    private const string UserImportMarker = ".sqlite-imported";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly object Gate = new();
    private static readonly HashSet<string> ReadyUserPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ShippedGate = new();
    private static string? _shippedCacheKey;
    private static IReadOnlyList<ExamPaper>? _shippedCache;

    private static string ShippedDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Exams");
    private static string TemplatePath => Path.Combine(AppContext.BaseDirectory, "papers-seed.db");

    private static string UserContentDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content", "Exams");

    public string ExamsDir => UserContentDir;

    public string AudioDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "content", "Audio");

    public bool DeleteUserPaper(string title)
    {
        using var ctx = ReadyUserContext();
        var db = ctx.Connection;
        var row = FindPaperRow(db, title, UserOrigin);
        if (row is null) return false;
        try
        {
            DeletePaperRows(db, row.Id);
            // Best effort: drop the legacy JSON file too, so a stale file
            // cannot confuse anyone browsing the folder by hand.
            try
            {
                var legacy = FindUserPaperFile(title);
                if (legacy is not null) File.Delete(legacy);
            }
            catch (Exception) { }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool IsUserPaper(string title)
    {
        using var ctx = ReadyUserContext();
        return FindPaperRow(ctx.Connection, title, UserOrigin) is not null;
    }

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
            using var ctx = ReadyUserContext();
            var db = ctx.Connection;
            db.RunInTransaction(() =>
            {
                // A user row with the same title shadows shipped; replace it.
                var existing = FindPaperRow(db, paper.Title, UserOrigin);
                if (existing is not null) DeletePaperRows(db, existing.Id);
                InsertPaper(db, paper, UserOrigin, string.Empty);
            });
            return ImportResult.Ok(paper.Title);
        }
        catch (Exception)
        {
            return ImportResult.Fail("Could not save the paper. Check disk space and try again.");
        }
    }

    public ExamPaper? GetPaper(string title)
    {
        using var ctx = ReadyUserContext();
        var own = FindPaperRow(ctx.Connection, title, UserOrigin);
        if (own is not null) return BuildPaper(ctx.Connection, own);
        return LoadShippedPapers()
            .FirstOrDefault(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
    }

    public ImportResult UpdatePaper(string originalTitle, ExamPaper paper)
    {
        var issues = PaperValidator.Validate(paper);
        if (issues.Count > 0)
            return ImportResult.Fail(issues[0]);
        try
        {
            using var ctx = ReadyUserContext();
            var db = ctx.Connection;
            db.RunInTransaction(() =>
            {
                var original = FindPaperRow(db, originalTitle, UserOrigin);
                if (original is not null) DeletePaperRows(db, original.Id);
                // Renamed onto another user paper: the target row goes too.
                if (!string.Equals(originalTitle, paper.Title, StringComparison.OrdinalIgnoreCase))
                {
                    var clash = FindPaperRow(db, paper.Title, UserOrigin);
                    if (clash is not null) DeletePaperRows(db, clash.Id);
                }
                InsertPaper(db, paper, UserOrigin, string.Empty);
            });
            return ImportResult.Ok(paper.Title);
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

    private static string? FindUserPaperFile(string title)
    {
        if (!Directory.Exists(UserContentDir)) return null;
        foreach (var file in Directory.EnumerateFiles(UserContentDir, "*.json"))
        {
            try
            {
                var paper = JsonSerializer.Deserialize<ExamPaper>(File.ReadAllText(file), Json);
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
        using var ctx = ReadyUserContext();
        var db = ctx.Connection;
        var own = db.Table<StoredPaper>().ToList();
        var byTitle = new Dictionary<string, ExamPaper>(StringComparer.OrdinalIgnoreCase);

        // Shipped first: a user row with the same title shadows it.
        foreach (var paper in LoadShippedPapers())
            byTitle[paper.Title] = paper;
        if (own.Count > 0)
        {
            var parts = db.Table<StoredPart>().ToList();
            var questions = db.Table<StoredQuestion>().ToList();
            var partsByPaper = parts.GroupBy(p => p.PaperId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Order).ToList());
            var questionsByPart = questions.GroupBy(q => q.PartId).ToDictionary(g => g.Key, g => g.OrderBy(q => q.Number).ToList());
            foreach (var row in own)
                byTitle[row.Title] = BuildPaper(row, partsByPaper, questionsByPart);
        }

        return byTitle.Values
            .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ------------------------------------------------------------ user file

    private static AppDbContext ReadyUserContext()
    {
        var key = AppDbContext.UserPapersDbPath();
        lock (Gate)
        {
            if (!ReadyUserPaths.Contains(key))
            {
                using var seed = new AppDbContext(key);
                seed.Connection.CreateTable<StoredPaper>();
                seed.Connection.CreateTable<StoredPart>();
                seed.Connection.CreateTable<StoredQuestion>();
                ImportUserFolderOnce(seed.Connection);
                MigrateMainDbPapers(seed.Connection);
                ReadyUserPaths.Add(key);
            }
        }
        return new AppDbContext(key);
    }

    private static void ImportUserFolderOnce(SQLiteConnection db)
    {
        Directory.CreateDirectory(UserContentDir);
        if (File.Exists(Path.Combine(UserContentDir, UserImportMarker))) return;

        foreach (var file in Directory.EnumerateFiles(UserContentDir, "*.json"))
        {
            try
            {
                var paper = JsonSerializer.Deserialize<ExamPaper>(File.ReadAllText(file), Json);
                if (paper is null || paper.Parts.Count == 0) continue;
                if (PaperValidator.Validate(paper).Count > 0) continue;
                if (FindPaperRow(db, paper.Title, UserOrigin) is not null) continue;
                InsertPaper(db, paper, UserOrigin, string.Empty);
            }
            catch (Exception)
            {
                // A broken file is left alone and skipped at load time.
            }
        }

        // The marker means the folder was absorbed; user papers now live in
        // the user database. Written even when nothing imported, so an empty
        // folder does not rescan on every start.
        try { File.WriteAllText(Path.Combine(UserContentDir, UserImportMarker), DateTime.UtcNow.ToString("o")); }
        catch (Exception) { }
    }

    /// <summary>
    /// One-time move from the single-file era: user paper rows still sitting
    /// in the main database relocate to the user file, and all paper rows
    /// leave the main file, which no longer stores papers at all.
    /// </summary>
    private static void MigrateMainDbPapers(SQLiteConnection userDb)
    {
        try
        {
            using var main = new AppDbContext();
            var mdb = main.Connection;
            List<StoredPaper> oldUser;
            try
            {
                oldUser = mdb.Table<StoredPaper>().Where(p => p.Origin == UserOrigin).ToList();
            }
            catch (SQLiteException)
            {
                return; // Fresh database without paper tables: nothing to move.
            }

            foreach (var row in oldUser)
            {
                if (FindPaperRow(userDb, row.Title, UserOrigin) is not null) continue;
                CopyPaperTree(mdb, userDb, row);
            }
            mdb.Execute("DELETE FROM ExamQuestions WHERE PartId IN (SELECT Id FROM ExamParts WHERE PaperId IN (SELECT Id FROM ExamPapers))");
            mdb.Execute("DELETE FROM ExamParts WHERE PaperId IN (SELECT Id FROM ExamPapers)");
            mdb.Execute("DELETE FROM ExamPapers");
            // Drop the empty shells so the main file holds no paper schema at all.
            mdb.Execute("DROP TABLE IF EXISTS ExamQuestions");
            mdb.Execute("DROP TABLE IF EXISTS ExamParts");
            mdb.Execute("DROP TABLE IF EXISTS ExamPapers");
        }
        catch (Exception)
        {
            // Best effort: the library still works from its two live files.
        }
    }

    private static void CopyPaperTree(SQLiteConnection from, SQLiteConnection into, StoredPaper row)
    {
        var parts = from.Table<StoredPart>().Where(p => p.PaperId == row.Id).OrderBy(p => p.Order).ToList();
        var partIds = parts.Select(p => p.Id).ToList();
        var questions = partIds.Count == 0
            ? new List<StoredQuestion>()
            : from.Table<StoredQuestion>().ToList().Where(q => partIds.Contains(q.PartId)).ToList();
        var paper = BuildPaper(row,
            new Dictionary<int, List<StoredPart>> { [row.Id] = parts },
            questions.GroupBy(q => q.PartId).ToDictionary(g => g.Key, g => g.ToList()));
        InsertPaper(into, paper, UserOrigin, row.ContentHash);
    }

    // ------------------------------------------------------------ shipped file

    private static IReadOnlyList<ExamPaper> LoadShippedPapers()
    {
        var key = ShippedCacheKey();
        lock (ShippedGate)
        {
            if (key == _shippedCacheKey && _shippedCache is not null)
                return _shippedCache;
            var papers = ReadShippedStore();
            _shippedCacheKey = key;
            _shippedCache = papers;
            return papers;
        }
    }

    private static string ShippedCacheKey()
    {
        try
        {
            if (File.Exists(TemplatePath))
            {
                var info = new FileInfo(TemplatePath);
                return $"db:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            }
            if (Directory.Exists(ShippedDir))
            {
                long stamp = 0;
                int count = 0;
                foreach (var file in Directory.EnumerateFiles(ShippedDir, "*.json"))
                {
                    stamp ^= new FileInfo(file).LastWriteTimeUtc.Ticks;
                    count++;
                }
                return $"json:{count}:{stamp}";
            }
        }
        catch (Exception) { }
        return "none";
    }

    private static IReadOnlyList<ExamPaper> ReadShippedStore()
    {
        if (File.Exists(TemplatePath))
        {
            try
            {
                // Read-only: the repo template is never written at runtime.
                using var read = new SQLiteConnection(
                    TemplatePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
                var papers = read.Table<StoredPaper>().ToList();
                var parts = read.Table<StoredPart>().ToList();
                var questions = read.Table<StoredQuestion>().ToList();
                var partsByPaper = parts.GroupBy(p => p.PaperId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Order).ToList());
                var questionsByPart = questions.GroupBy(q => q.PartId).ToDictionary(g => g.Key, g => g.OrderBy(q => q.Number).ToList());
                return papers
                    .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(p => BuildPaper(p, partsByPaper, questionsByPart))
                    .ToList();
            }
            catch (Exception)
            {
                // Fall through to the JSON seeds below.
            }
        }

        // Fallback when the build template is missing: parse the shipped
        // JSON seeds directly, still without writing anything.
        var byTitle = new Dictionary<string, ExamPaper>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(ShippedDir)) return Array.Empty<ExamPaper>();
        foreach (var file in Directory.EnumerateFiles(ShippedDir, "*.json"))
        {
            try
            {
                var paper = JsonSerializer.Deserialize<ExamPaper>(File.ReadAllText(file), Json);
                if (paper is null || paper.Parts.Count == 0) continue;
                byTitle[paper.Title] = paper;
            }
            catch (JsonException)
            {
                // A malformed paper should not stop the rest from loading.
            }
            catch (IOException)
            {
                // An unreadable file is skipped like a malformed one.
            }
        }
        return byTitle.Values
            .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ------------------------------------------------------------ mapping

    private static StoredPaper? FindPaperRow(SQLiteConnection db, string title, string origin)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        return db.Table<StoredPaper>()
            .Where(p => p.Origin == origin)
            .ToList()
            .FirstOrDefault(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
    }

    private static void InsertPaper(SQLiteConnection db, ExamPaper paper, string origin, string hash)
    {
        var row = new StoredPaper
        {
            Title = paper.Title,
            Source = paper.Source ?? string.Empty,
            Category = paper.Category ?? string.Empty,
            Level = paper.Level ?? string.Empty,
            TagsJson = JsonSerializer.Serialize(paper.Tags ?? new List<string>()),
            Origin = origin,
            ContentHash = hash,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Insert(row);

        int order = 0;
        foreach (var part in paper.Parts)
        {
            var partRow = new StoredPart
            {
                PaperId = row.Id,
                Order = order++,
                PartCode = part.Id ?? string.Empty,
                Skill = part.Skill ?? string.Empty,
                Title = part.Title ?? string.Empty,
                Topic = part.Topic ?? string.Empty,
                TaskType = part.TaskType ?? string.Empty,
                Material = part.Material ?? string.Empty,
                Instructions = part.Instructions ?? string.Empty,
                AudioFile = part.AudioFile ?? string.Empty,
                ImageFile = part.ImageFile ?? string.Empty,
                Minutes = part.Minutes,
                PrepSeconds = part.PrepSeconds,
            };
            db.Insert(partRow);
            foreach (var q in part.Questions)
            {
                db.Insert(new StoredQuestion
                {
                    PartId = partRow.Id,
                    Number = q.Number,
                    Kind = string.IsNullOrWhiteSpace(q.Kind) ? "choice" : q.Kind,
                    Prompt = q.Prompt ?? string.Empty,
                    CorrectKey = q.CorrectKey ?? string.Empty,
                    GapAnswer = q.GapAnswer ?? string.Empty,
                    Explanation = q.Explanation ?? string.Empty,
                    OptionsJson = JsonSerializer.Serialize(q.Options ?? new List<ExamOption>()),
                    MatchRowsJson = JsonSerializer.Serialize(q.MatchRows ?? new List<ExamMatchRow>()),
                    BankJson = JsonSerializer.Serialize(q.Bank ?? new List<string>()),
                });
            }
        }
    }

    private static void DeletePaperRows(SQLiteConnection db, int paperId)
    {
        var partIds = db.Table<StoredPart>()
            .Where(p => p.PaperId == paperId)
            .ToList()
            .Select(p => p.Id)
            .ToList();
        foreach (var partId in partIds)
            db.Execute("DELETE FROM ExamQuestions WHERE PartId = ?", partId);
        db.Execute("DELETE FROM ExamParts WHERE PaperId = ?", paperId);
        db.Execute("DELETE FROM ExamPapers WHERE Id = ?", paperId);
    }

    private static ExamPaper BuildPaper(SQLiteConnection db, StoredPaper row)
    {
        var parts = db.Table<StoredPart>().Where(p => p.PaperId == row.Id).OrderBy(p => p.Order).ToList();
        var partIds = parts.Select(p => p.Id).ToList();
        var questions = partIds.Count == 0
            ? new List<StoredQuestion>()
            : db.Table<StoredQuestion>().ToList().Where(q => partIds.Contains(q.PartId)).OrderBy(q => q.Number).ToList();
        return BuildPaper(row,
            new Dictionary<int, List<StoredPart>> { [row.Id] = parts },
            questions.GroupBy(q => q.PartId).ToDictionary(g => g.Key, g => g.ToList()));
    }

    private static ExamPaper BuildPaper(
        StoredPaper row,
        Dictionary<int, List<StoredPart>> partsByPaper,
        Dictionary<int, List<StoredQuestion>> questionsByPart)
    {
        var paper = new ExamPaper
        {
            Title = row.Title,
            Source = row.Source,
            Category = row.Category,
            Level = row.Level,
            Tags = ReadJson(row.TagsJson, () => new List<string>()),
        };
        if (!partsByPaper.TryGetValue(row.Id, out var parts)) return paper;
        foreach (var part in parts)
        {
            var examPart = new ExamPart
            {
                Id = part.PartCode,
                Skill = part.Skill,
                Title = part.Title,
                Topic = part.Topic,
                TaskType = part.TaskType,
                Material = part.Material,
                Instructions = part.Instructions,
                AudioFile = part.AudioFile,
                ImageFile = part.ImageFile,
                Minutes = part.Minutes,
                PrepSeconds = part.PrepSeconds,
            };
            if (questionsByPart.TryGetValue(part.Id, out var questions))
            {
                foreach (var q in questions)
                {
                    examPart.Questions.Add(new ExamQuestion
                    {
                        Number = q.Number,
                        Prompt = q.Prompt,
                        Kind = q.Kind,
                        CorrectKey = q.CorrectKey,
                        GapAnswer = q.GapAnswer,
                        Explanation = q.Explanation,
                        Options = ReadJson(q.OptionsJson, () => new List<ExamOption>()),
                        MatchRows = ReadJson(q.MatchRowsJson, () => new List<ExamMatchRow>()),
                        Bank = ReadJson(q.BankJson, () => new List<string>()),
                    });
                }
            }
            paper.Parts.Add(examPart);
        }
        return paper;
    }

    private static T ReadJson<T>(string json, Func<T> fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return fallback();
            return JsonSerializer.Deserialize<T>(json, Json) ?? fallback();
        }
        catch (JsonException)
        {
            return fallback();
        }
    }
}
