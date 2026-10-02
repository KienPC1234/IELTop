using System.IO;
using System.Text.Json;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Exam;
using IELTop.Services.Storage;

namespace IELTop.Services.App;

/// <summary>One paper row in the library list.</summary>
public sealed record LibraryRow(
    string Title,
    string Detail,
    string Category,
    string Level,
    bool IsUserPaper,
    int Parts,
    int Questions);

/// <summary>One paper waiting in the custom test basket.</summary>
public sealed record BasketEntry(string PaperTitle, string Detail);

/// <summary>The Library screen state plus a navigation hint for the shell.</summary>
public sealed class LibrarySnapshot
{
    public IReadOnlyList<LibraryRow> Rows { get; init; } = Array.Empty<LibraryRow>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DraftSkills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<BasketEntry> Basket { get; init; } = Array.Empty<BasketEntry>();
    public string SearchText { get; init; } = string.Empty;
    public string SelectedCategory { get; init; } = "All categories";
    public string SelectedSkill { get; init; } = "All skills";
    public string StatusMessage { get; init; } = string.Empty;
    public string LibrarySummary { get; init; } = string.Empty;
    public string BasketLabel { get; init; } = string.Empty;
    public string DraftTitle { get; init; } = string.Empty;
    public string DraftSkill { get; init; } = "Reading";
    public string DraftJson { get; init; } = string.Empty;
    public string PasteText { get; init; } = string.Empty;
    public bool HasRows { get; init; }
    public bool HasBasket { get; init; }
    public bool IsBusy { get; init; }
    public bool CanUseAi { get; init; }
    public string AiHint { get; init; } = string.Empty;
    public bool CanUseVision { get; init; }
    public string VisionHint { get; init; } = string.Empty;

    /// <summary>Set when the action should move the shell to another page.</summary>
    public string NavigateTo { get; init; } = string.Empty;
}

/// <summary>
/// Paper library: browse, filter, import text and JSON, build a basket, and
/// start a custom test. Nothing here needs a language model; AI drafting is an
/// optional extra that stays off when no model is set.
/// </summary>
public sealed class LibraryService
{
    private readonly IExamRepository _repository;
    private readonly ExamEngine _exam;
    private readonly IIeltsAiService _ai;

    private readonly List<ExamPaper> _all = new();
    private readonly List<BasketEntry> _basket = new();
    private CancellationTokenSource? _aiCts;

    private string _search = string.Empty;
    private string _category = "All categories";
    private string _skill = "All skills";
    private string _status = "Pick papers, drop them in the basket, then start.";
    private string _draftTitle = string.Empty;
    private string _draftSkill = "Reading";
    private string _draftJson = string.Empty;
    private string _pasteText = string.Empty;
    private bool _busy;
    private string _exportFolder = string.Empty;

    /// <summary>Raised when AI drafting produced a paper and the editor should open it.</summary>
    public event Action<ExamPaper>? DraftReady;

    public LibraryService(IExamRepository repository, ExamEngine exam, IIeltsAiService ai)
    {
        _repository = repository;
        _exam = exam;
        _ai = ai;
        Load();
    }

    public LibrarySnapshot SetSearch(string v) { _search = v ?? string.Empty; return Snapshot(); }
    public LibrarySnapshot SetCategory(string v) { _category = v ?? "All categories"; return Snapshot(); }
    public LibrarySnapshot SetSkill(string v) { _skill = v ?? "All skills"; return Snapshot(); }
    public LibrarySnapshot SetDraftTitle(string v) { _draftTitle = v ?? string.Empty; return Snapshot(); }
    public LibrarySnapshot SetDraftSkill(string v) { _draftSkill = v ?? "Reading"; return Snapshot(); }
    public LibrarySnapshot SetDraftJson(string v) { _draftJson = v ?? string.Empty; return Snapshot(); }
    public LibrarySnapshot SetPasteText(string v) { _pasteText = v ?? string.Empty; return Snapshot(); }

    public LibrarySnapshot Load()
    {
        _all.Clear();
        try { _all.AddRange(_repository.LoadPapers()); }
        catch (Exception) { _status = "Could not read the paper list."; }

        if (_category != "All categories" && !_all.Any(p =>
            string.Equals(p.Category, _category, StringComparison.OrdinalIgnoreCase)))
            _category = "All categories";

        return Snapshot();
    }

    public LibrarySnapshot ClearFilters()
    {
        _search = string.Empty;
        _category = "All categories";
        _skill = "All skills";
        _status = $"{_all.Count} paper(s) in the library.";
        return Snapshot();
    }

    public LibrarySnapshot AddToBasket(string title)
    {
        var paper = Find(title);
        if (paper is null) return Snapshot();
        if (_basket.Any(b => string.Equals(b.PaperTitle, paper.Title, StringComparison.OrdinalIgnoreCase)))
        {
            _status = $"{paper.Title} is already in the basket.";
            return Snapshot();
        }
        _basket.Add(new BasketEntry(paper.Title,
            $"{paper.Parts.Count} parts, {paper.Parts.Sum(p => p.Questions.Count)} questions, " +
            $"{string.Join(", ", paper.Parts.Select(p => p.Skill).Distinct(StringComparer.OrdinalIgnoreCase))}"));
        _status = $"Added {paper.Title} to the basket.";
        return Snapshot();
    }

    public LibrarySnapshot RemoveFromBasket(string title)
    {
        _basket.RemoveAll(b => string.Equals(b.PaperTitle, title, StringComparison.OrdinalIgnoreCase));
        return Snapshot();
    }

    public LibrarySnapshot ClearBasket()
    {
        _basket.Clear();
        _status = "Basket cleared.";
        return Snapshot();
    }

    /// <summary>Loads one paper in Mock Test setup, without starting it.</summary>
    public LibrarySnapshot StartPaper(string title)
    {
        if (Find(title) is null)
        {
            _status = "That paper is gone. Reload the library.";
            return Snapshot();
        }
        _exam.SelectPaper(title);
        _status = $"Loaded {title}. Press Start test when ready.";
        return Snapshot(navigateTo: "mock");
    }

    /// <summary>Starts a custom test from every paper in the basket.</summary>
    public LibrarySnapshot StartBasket()
    {
        if (_basket.Count == 0)
        {
            _status = "The basket is empty. Add papers to it first.";
            return Snapshot();
        }
        var picks = new List<(string PaperTitle, string PartId)>();
        foreach (var item in _basket)
        {
            var paper = Find(item.PaperTitle);
            if (paper is null) continue;
            foreach (var part in paper.Parts)
                picks.Add((paper.Title, part.Id));
        }
        var result = _exam.StartCustomTest(picks, $"Custom test from {_basket.Count} paper(s).");
        if (!result.Success)
        {
            _status = result.Message;
            return Snapshot();
        }
        return Snapshot(navigateTo: "mock");
    }

    public LibrarySnapshot DuplicatePaper(string title)
    {
        var paper = _repository.GetPaper(title);
        if (paper is null)
        {
            _status = "That paper is gone. Reload the library.";
            return Snapshot();
        }
        paper.Title = paper.Title + " copy";
        var result = _repository.SavePaper(paper);
        if (!result.Success) { _status = result.Error; return Snapshot(); }
        Load();
        _exam.Load();
        _status = $"Saved {result.Title}. Edit it to make it yours.";
        return Snapshot();
    }

    public LibrarySnapshot DeletePaper(string title)
    {
        if (!_repository.IsUserPaper(title))
        {
            _status = "Built in papers cannot be deleted.";
            return Snapshot();
        }
        _status = _repository.DeleteUserPaper(title)
            ? $"Deleted {title}."
            : "Could not delete that paper.";
        _basket.RemoveAll(b => string.Equals(b.PaperTitle, title, StringComparison.OrdinalIgnoreCase));
        Load();
        _exam.Load();
        return Snapshot();
    }

    /// <summary>Imports one picked or dropped file by its text content.</summary>
    public LibrarySnapshot ImportContent(string fileName, string content)
        => ImportContentCore(fileName, content, null);

    /// <summary>
    /// Imports a binary document (PDF, DOCX) by its base64 payload. The host
    /// writes it to a temp file and runs the text extractor, so the web UI
    /// never needs a document parser of its own.
    /// </summary>
    public LibrarySnapshot ImportBinary(string fileName, string base64)
        => ImportContentCore(fileName, null, base64);

    private LibrarySnapshot ImportContentCore(string fileName, string? content, string? base64)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "imported" : fileName;
        var ext = Path.GetExtension(name).ToLowerInvariant();

        // An image is not a paper. Save it next to the app so a Writing Task 1
        // chart can point at it, then tell the user where it went.
        if (ext is ".png" or ".jpg" or ".jpeg")
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IELTop", "content", "Images");
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, Path.GetFileName(name));
                File.WriteAllBytes(target, Convert.FromBase64String(content ?? base64 ?? string.Empty));
                _status = $"Saved image {Path.GetFileName(name)}. Set it as a part ImageFile to use it.";
            }
            catch (Exception)
            {
                _status = $"Could not save {name}.";
            }
            return Snapshot();
        }

        // A binary document is decoded on this side, then treated as text.
        if (base64 is not null)
        {
            var extracted = ExtractBinary(name, base64);
            if (!extracted.Success) { _status = extracted.Error; return Snapshot(); }
            content = extracted.Text;
        }
        content ??= string.Empty;

        if (ext == ".json")
        {
            var direct = _repository.ImportPaper(content ?? string.Empty);
            _status = direct.Success ? $"Imported {direct.Title}." : $"{name}: {direct.Error}";
        }
        else
        {
            var paper = BuildOfflinePaper(Path.GetFileNameWithoutExtension(name), content ?? string.Empty, _draftSkill);
            var result = _repository.SavePaper(paper);
            _status = result.Success
                ? $"Imported {result.Title} as an offline draft. Open the Editor to add questions."
                : $"{name}: {result.Error}";
        }
        Load();
        _exam.Load();
        return Snapshot();
    }

    /// <summary>Saves pasted text as an offline draft part. No model needed.</summary>
    public LibrarySnapshot BuildDraftFromPaste()
    {
        if (string.IsNullOrWhiteSpace(_pasteText))
        {
            _status = "Paste some text first, then build a draft.";
            return Snapshot();
        }
        var title = string.IsNullOrWhiteSpace(_draftTitle)
            ? $"Pasted {_draftSkill} {DateTime.Now:yyyyMMdd-HHmm}"
            : _draftTitle.Trim();
        var result = _repository.SavePaper(BuildOfflinePaper(title, _pasteText.Trim(), _draftSkill));
        if (!result.Success) { _status = result.Error; return Snapshot(); }
        _pasteText = string.Empty;
        Load();
        _exam.Load();
        _status = $"Saved {result.Title} as an offline draft. Add questions in the Editor.";
        return Snapshot();
    }

    /// <summary>Asks the model to turn pasted text into questions.</summary>
    public async Task<LibrarySnapshot> AiFormatPasteAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pasteText)) { _status = "Paste some text first."; return Snapshot(); }
        if (!_ai.IsAvailable) { _status = "Add a language model in Settings for AI drafts."; return Snapshot(); }

        _busy = true;
        _aiCts?.Cancel();
        _aiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _status = "Working. AI is drafting questions.";
        try
        {
            var draft = await _ai.DraftPaperAsync(_pasteText.Trim(), _draftSkill, _aiCts.Token);
            if (!draft.Success || draft.Paper is null)
            {
                _draftJson = draft.Json ?? string.Empty;
                _status = draft.Json is { Length: > 0 }
                    ? $"{draft.Error} The raw reply is in the JSON box for hand fixes."
                    : draft.Error;
                return Snapshot();
            }
            if (!string.IsNullOrWhiteSpace(_draftTitle)) draft.Paper.Title = _draftTitle.Trim();
            _draftJson = draft.Json ?? string.Empty;
            DraftReady?.Invoke(draft.Paper);
            _status = $"Draft ready for {draft.Paper.Title}. Check every question, then save.";
            return Snapshot(navigateTo: "editor");
        }
        catch (OperationCanceledException) { _status = "Draft cancelled."; return Snapshot(); }
        catch (Exception) { _status = "The model could not be reached. Check Settings and try again."; return Snapshot(); }
        finally { _busy = false; }
    }

    public LibrarySnapshot CancelAi()
    {
        _aiCts?.Cancel();
        _status = "Stopping.";
        return Snapshot();
    }

    /// <summary>
    /// Reads pictures with a vision model: scans, photos, charts, Word images.
    /// The text lands in the paste box for review, never auto saved.
    /// </summary>
    public async Task<LibrarySnapshot> ReadImagesAsync(
        IReadOnlyList<ImportedImage> images, CancellationToken ct)
    {
        if (!_ai.VisionAvailable)
        {
            _status = !_ai.IsAvailable
                ? "Add a language model in Settings to read pictures."
                : "Vision is turned off. Enable it in Settings to read pictures.";
            return Snapshot();
        }
        if (images.Count == 0) { _status = "No pictures to read."; return Snapshot(); }

        _busy = true;
        _aiCts?.Cancel();
        _aiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _status = "Working. Reading pictures with the vision model.";
        var texts = new List<string>();
        try
        {
            int total = 0;
            foreach (var image in images)
            {
                _aiCts.Token.ThrowIfCancellationRequested();
                total++;
                _status = $"Working. Reading picture {total}: {image.Name}.";
                var read = await _ai.ReadImportImageAsync(image, _draftSkill, _aiCts.Token);
                texts.Add(read.Success
                    ? $"[Picture {total}: {image.Name}]\n{read.Text.Trim()}"
                    : $"[{image.Name}: {read.Error}]");
            }
            var combined = string.Join("\n\n", texts.Where(t => t.Length > 0));
            _pasteText = string.IsNullOrWhiteSpace(_pasteText)
                ? combined
                : _pasteText.Trim() + "\n\n" + combined;
            _status = $"Read {total} picture(s) into the paste box. Check the text, then build a draft.";
        }
        catch (OperationCanceledException) { _status = "Reading stopped."; }
        catch (Exception) { _status = "The model could not be reached. Check Settings and try again."; }
        finally { _busy = false; }
        return Snapshot();
    }

    /// <summary>Asks the model to rate one paper and report what to fix.</summary>
    public async Task<LibrarySnapshot> AiCheckPaperAsync(string title, CancellationToken ct)
    {
        var paper = Find(title);
        if (paper is null) { _status = "That paper is gone."; return Snapshot(); }
        if (!_ai.IsAvailable) { _status = "Add a language model in Settings for a paper check."; return Snapshot(); }

        _busy = true;
        _status = $"Asking the model to check {paper.Title}.";
        try
        {
            var review = await _ai.ReviewPaperAsync(paper, ct);
            if (!review.Success)
            {
                _status = review.Error;
            }
            else
            {
                var text = $"Paper check for {paper.Title}: score {review.Score:0.0}. ";
                if (review.Strengths.Count > 0) text += "Good: " + string.Join("; ", review.Strengths.Take(3)) + ". ";
                if (review.Fixes.Count > 0) text += "Fix: " + string.Join("; ", review.Fixes.Take(3)) + ".";
                _status = text;
            }
        }
        catch (Exception) { _status = "The model could not be reached. Check Settings and try again."; }
        finally { _busy = false; }
        return Snapshot();
    }

    /// <summary>Puts a blank, hand editable template in the JSON box.</summary>
    public LibrarySnapshot NewManualTemplate()
    {
        var title = string.IsNullOrWhiteSpace(_draftTitle) ? "My manual paper" : _draftTitle.Trim();
        var template = new ExamPaper
        {
            Title = title,
            Source = "Created by hand in the Library. Edit before sharing.",
            Category = "Imported",
            Tags = new List<string> { "manual" },
            Parts = new List<ExamPart>
            {
                new()
                {
                    Id = "R1",
                    Skill = _draftSkill,
                    Title = $"{_draftSkill} Part 1",
                    TaskType = _draftSkill == "Writing" ? "Task 2 Opinion" : "Passage 1",
                    Material = "Paste your passage or task here.",
                    Instructions = _draftSkill == "Writing"
                        ? "Write at least 250 words."
                        : "Read the text, then answer the questions.",
                    Minutes = _draftSkill == "Writing" ? 40 : 12,
                    Questions = _draftSkill is "Writing" or "Speaking"
                        ? new List<ExamQuestion>()
                        : new List<ExamQuestion>
                        {
                            new()
                            {
                                Number = 1,
                                Kind = "choice",
                                Prompt = "What is the main idea?",
                                Options = new List<ExamOption>
                                {
                                    new() { Key = "A", Text = "First option" },
                                    new() { Key = "B", Text = "Second option" },
                                    new() { Key = "C", Text = "Third option" },
                                },
                                CorrectKey = "A",
                                Explanation = "Why A is right, in one sentence.",
                            },
                        },
                },
            },
        };
        _draftJson = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
        _status = "Blank template ready. Edit the JSON, then press Save draft.";
        return Snapshot();
    }

    /// <summary>Saves the edited JSON draft after validation. Never overwrites.</summary>
    public LibrarySnapshot SaveDraftJson()
    {
        if (string.IsNullOrWhiteSpace(_draftJson))
        {
            _status = "The draft box is empty. Make a template or paste JSON first.";
            return Snapshot();
        }
        var result = _repository.ImportPaper(_draftJson);
        if (!result.Success) { _status = result.Error; return Snapshot(); }
        _draftJson = string.Empty;
        Load();
        _exam.Load();
        _status = $"Saved {result.Title}. It is ready in the list above.";
        return Snapshot();
    }

    /// <summary>Exports every paper matching the current filter to a new folder.</summary>
    public LibrarySnapshot ExportShown()
    {
        var titles = Filtered().Select(p => p.Title).ToList();
        if (titles.Count == 0) { _status = "Nothing matches the current filter."; return Snapshot(); }
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "IELTop-Export", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var result = _repository.ExportPapers(titles, folder);
        if (result.Success) _exportFolder = result.Folder;
        _status = result.Success
            ? $"Exported {result.PaperCount} paper(s) and {result.AudioCount} clip(s) to {result.Folder}."
            : result.Error;
        return Snapshot();
    }

    /// <summary>The last export folder, opened by the shell. Empty until an export runs.</summary>
    public string ExportFolder => string.IsNullOrEmpty(_exportFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IELTop-Export")
        : _exportFolder;

    /// <summary>
    /// Writes a base64 document to a temp file, then runs the shared text
    /// extractor. The temp file is removed no matter what.
    /// </summary>
    private static (bool Success, string Text, string Error) ExtractBinary(string fileName, string base64)
    {
        string? temp = null;
        try
        {
            var safe = Path.GetFileName(fileName);
            temp = Path.Combine(Path.GetTempPath(), $"ieltop-{Guid.NewGuid():N}{Path.GetExtension(safe)}");
            File.WriteAllBytes(temp, Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return (false, string.Empty, $"{fileName}: the file data was not readable.");
        }
        catch (Exception)
        {
            return (false, string.Empty, $"{fileName}: could not be read.");
        }

        try
        {
            return FileTextExtractor.TryExtract(temp, out var text, out var error)
                ? (true, text, string.Empty)
                : (false, string.Empty, $"{fileName}: {error}");
        }
        finally
        {
            try { if (temp is not null) File.Delete(temp); } catch { /* temp cleaned by the OS later */ }
        }
    }

    private ExamPaper? Find(string? title) => string.IsNullOrWhiteSpace(title) ? null
        : _all.FirstOrDefault(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<ExamPaper> Filtered()
    {
        var query = _search.Trim();
        foreach (var paper in _all)
        {
            if (_category != "All categories"
                && !string.Equals(paper.Category, _category, StringComparison.OrdinalIgnoreCase)) continue;
            if (_skill != "All skills"
                && !paper.Parts.Any(p => string.Equals(p.Skill, _skill, StringComparison.OrdinalIgnoreCase))) continue;
            if (query.Length > 0
                && !paper.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !paper.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))) continue;
            yield return paper;
        }
    }

    private static ExamPaper BuildOfflinePaper(string name, string text, string skill)
    {
        var cleanSkill = string.IsNullOrWhiteSpace(skill) ? "Reading" : skill.Trim();
        var title = string.IsNullOrWhiteSpace(name) ? "Imported draft" : name.Trim();
        bool productive = cleanSkill.Equals("Writing", StringComparison.OrdinalIgnoreCase)
            || cleanSkill.Equals("Speaking", StringComparison.OrdinalIgnoreCase);
        return new ExamPaper
        {
            Title = title,
            Source = "Imported text, saved offline before questions were added.",
            Category = "Imported",
            Tags = new List<string> { "imported" },
            Parts = new List<ExamPart>
            {
                new()
                {
                    Id = "P1",
                    Skill = cleanSkill,
                    Title = $"{cleanSkill} draft: {title}",
                    TaskType = cleanSkill,
                    Material = text.Trim(),
                    Instructions = productive
                        ? "Answer in your own words. Add a teacher or AI check later."
                        : "Draft saved offline. Edit this paper and add questions.",
                    Minutes = cleanSkill.Equals("Writing", StringComparison.OrdinalIgnoreCase) ? 40 : 12,
                    Questions = new List<ExamQuestion>(),
                },
            },
        };
    }

    public LibrarySnapshot Snapshot(string navigateTo = "")
    {
        var rows = Filtered().Select(paper => new LibraryRow(
            paper.Title,
            $"{paper.Parts.Count} parts, {paper.Parts.Sum(p => p.Questions.Count)} questions, " +
            $"about {paper.Parts.Sum(p => p.Minutes)} min. " +
            $"{string.Join(", ", paper.Parts.Select(p => p.Skill).Distinct(StringComparer.OrdinalIgnoreCase))}. " +
            (_repository.IsUserPaper(paper.Title) ? "Yours." : "Built in."),
            paper.Category, paper.Level, _repository.IsUserPaper(paper.Title),
            paper.Parts.Count, paper.Parts.Sum(p => p.Questions.Count))).ToList();

        var categories = new List<string> { "All categories" };
        categories.AddRange(_all.Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

        bool hasRows = rows.Count > 0;
        string summary = _all.Count == 0
            ? "No papers yet."
            : rows.Count == _all.Count
                ? $"{_all.Count} paper(s) in the library."
                : $"Showing {rows.Count} of {_all.Count} paper(s). Filters hide the rest.";

        return new LibrarySnapshot
        {
            Rows = rows,
            Categories = categories,
            Skills = new[] { "All skills", "Listening", "Reading", "Writing", "Speaking" },
            DraftSkills = new[] { "Listening", "Reading", "Writing", "Speaking" },
            Basket = _basket.ToList(),
            SearchText = _search,
            SelectedCategory = _category,
            SelectedSkill = _skill,
            StatusMessage = _status,
            LibrarySummary = summary,
            BasketLabel = _basket.Count > 0
                ? $"{_basket.Count} paper(s) in the basket."
                : "The basket is empty. Add papers to build a custom test.",
            DraftTitle = _draftTitle,
            DraftSkill = _draftSkill,
            DraftJson = _draftJson,
            PasteText = _pasteText,
            HasRows = hasRows,
            HasBasket = _basket.Count > 0,
            IsBusy = _busy,
            CanUseAi = _ai.IsAvailable && !_busy,
            AiHint = _ai.IsAvailable
                ? "AI formats pasted text into questions. Review the draft before saving."
                : "Add a language model in Settings to let AI draft questions.",
            CanUseVision = _ai.VisionAvailable && !_busy,
            VisionHint = !_ai.IsAvailable
                ? "Add a language model in Settings to read pictures."
                : !_ai.VisionAvailable
                    ? "Vision is turned off. Enable it in Settings to read pictures."
                    : "Reads pictures from scans, photos, charts, and Word images into editable text.",
            NavigateTo = navigateTo,
        };
    }
}
