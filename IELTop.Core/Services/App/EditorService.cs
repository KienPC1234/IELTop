using System.IO;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Exam;
using IELTop.Services.Storage;

namespace IELTop.Services.App;

/// <summary>One question as plain, editable fields for the web UI.</summary>
public sealed class EditorQuestionState
{
    public int Number { get; set; } = 1;
    public string Kind { get; set; } = "choice";
    public string Prompt { get; set; } = string.Empty;
    public string OptionsText { get; set; } = "A. First option\nB. Second option\nC. Third option";
    public string CorrectKey { get; set; } = "A";
    public string GapAnswer { get; set; } = string.Empty;
    public string BankText { get; set; } = string.Empty;
    public string MatchRowsText { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;

    public string Summary => $"Q{Number} {Kind}: {(Prompt.Length <= 60 ? Prompt : Prompt[..60] + "...")}";

    internal static EditorQuestionState FromModel(ExamQuestion q) => new()
    {
        Number = q.Number,
        Kind = string.IsNullOrWhiteSpace(q.Kind) ? "choice" : q.Kind,
        Prompt = q.Prompt,
        OptionsText = q.Options.Count > 0
            ? string.Join("\n", q.Options.Select(o => $"{o.Key}. {o.Text}"))
            : "A. First option\nB. Second option\nC. Third option",
        CorrectKey = string.IsNullOrWhiteSpace(q.CorrectKey) ? "A" : q.CorrectKey,
        GapAnswer = q.GapAnswer,
        BankText = string.Join("\n", q.Bank),
        MatchRowsText = string.Join("\n", q.MatchRows.Select(r => $"{r.Label} => {r.Answer}")),
        Explanation = q.Explanation,
    };

    internal ExamQuestion ToModel()
    {
        var options = new List<ExamOption>();
        foreach (var line in OptionsText.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length < 3 || t[1] != '.') continue;
            options.Add(new ExamOption { Key = t[..1].ToUpperInvariant(), Text = t[2..].Trim() });
        }
        var bank = BankText.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var rows = new List<ExamMatchRow>();
        foreach (var line in MatchRowsText.Split('\n'))
        {
            var cut = line.Split("=>", StringSplitOptions.None);
            if (cut.Length != 2) continue;
            if (cut[0].Trim().Length == 0 || cut[1].Trim().Length == 0) continue;
            rows.Add(new ExamMatchRow { Label = cut[0].Trim(), Answer = cut[1].Trim() });
        }
        var kind = (Kind ?? "choice").Trim().ToLowerInvariant();
        return new ExamQuestion
        {
            Number = Number,
            Kind = kind is "gap" or "match" ? kind : "choice",
            Prompt = Prompt.Trim(),
            Options = options,
            CorrectKey = CorrectKey.Trim().ToUpperInvariant(),
            GapAnswer = GapAnswer.Trim(),
            Bank = bank,
            MatchRows = rows,
            Explanation = Explanation.Trim(),
        };
    }
}

/// <summary>One part as plain, editable fields for the web UI.</summary>
public sealed class EditorPartState
{
    public string Id { get; set; } = "R1";
    public string Skill { get; set; } = "Reading";
    public string Title { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public int Minutes { get; set; } = 12;
    public int PrepSeconds { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string AudioFile { get; set; } = string.Empty;
    public List<EditorQuestionState> Questions { get; } = new();

    public string Summary => $"{Id} {Skill}: {Title} ({Questions.Count} questions)";

    internal static EditorPartState FromModel(ExamPart p)
    {
        var state = new EditorPartState
        {
            Id = p.Id,
            Skill = p.Skill,
            Title = p.Title,
            Topic = p.Topic,
            TaskType = p.TaskType,
            Minutes = p.Minutes <= 0 ? 10 : p.Minutes,
            PrepSeconds = p.PrepSeconds,
            Instructions = p.Instructions,
            Material = p.Material,
            AudioFile = p.AudioFile,
        };
        foreach (var q in p.Questions) state.Questions.Add(EditorQuestionState.FromModel(q));
        return state;
    }

    internal ExamPart ToModel() => new()
    {
        Id = Id.Trim(),
        Skill = Skill.Trim(),
        Title = Title.Trim(),
        Topic = Topic.Trim(),
        TaskType = TaskType.Trim(),
        Minutes = Math.Max(1, Minutes),
        PrepSeconds = Math.Max(0, PrepSeconds),
        Instructions = Instructions.Trim(),
        Material = Material,
        AudioFile = AudioFile.Trim(),
        Questions = Questions.Select(q => q.ToModel()).ToList(),
    };
}

/// <summary>The Editor screen state plus a navigation hint.</summary>
public sealed class EditorSnapshot
{
    public string PaperTitle { get; init; } = "My new paper";
    public string Source { get; init; } = string.Empty;
    public string Category { get; init; } = "Imported";
    public string Level { get; init; } = string.Empty;
    public string TagsText { get; init; } = "manual";
    public string PasteText { get; init; } = string.Empty;
    public string PasteSkill { get; init; } = "Reading";
    public string StatusMessage { get; init; } = string.Empty;
    public bool IsBusy { get; init; }
    public IReadOnlyList<EditorPartState> Parts { get; init; } = Array.Empty<EditorPartState>();
    public int SelectedPartIndex { get; init; } = -1;
    public int SelectedQuestionIndex { get; init; } = -1;
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();
    public string ValidationSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> Skills { get; init; } = new[] { "Listening", "Reading", "Writing", "Speaking" };
    public IReadOnlyList<string> Kinds { get; init; } = new[] { "choice", "gap", "match" };
    public bool HasParts { get; init; }
    public bool HasQuestions { get; init; }
    public bool CanUseAi { get; init; }
    public string AiHint { get; init; } = string.Empty;
    public bool CanUseVision { get; init; }
    public string VisionHint { get; init; } = string.Empty;
    public string NavigateTo { get; init; } = string.Empty;
}

/// <summary>
/// Structured paper editor: fields per part and question, live validation,
/// optional AI drafting, text import, save, and a test run. Saving and
/// editing never need a language model.
/// </summary>
public sealed class EditorService
{
    private readonly IExamRepository _repository;
    private readonly ExamEngine _exam;
    private readonly IIeltsAiService _ai;
    private CancellationTokenSource? _aiCts;

    private string _originalTitle = string.Empty;
    private string _paperTitle = "My new paper";
    private string _source = "Created in the Editor.";
    private string _category = "Imported";
    private string _level = string.Empty;
    private string _tagsText = "manual";
    private string _pasteText = string.Empty;
    private string _pasteSkill = "Reading";
    private string _status = "Create a paper, add parts and questions, then save.";
    private bool _busy;
    private readonly List<EditorPartState> _parts = new();
    private int _selectedPart = -1;
    private int _selectedQuestion = -1;

    public EditorService(IExamRepository repository, ExamEngine exam, IIeltsAiService ai)
    {
        _repository = repository;
        _exam = exam;
        _ai = ai;
        NewPaper();
    }

    // ---- Paper level fields ----

    public EditorSnapshot SetPaperField(string field, string value)
    {
        value ??= string.Empty;
        switch (field)
        {
            case "title": _paperTitle = value; break;
            case "source": _source = value; break;
            case "category": _category = value; break;
            case "level": _level = value; break;
            case "tags": _tagsText = value; break;
            case "paste": _pasteText = value; break;
            case "pasteSkill": _pasteSkill = value; break;
        }
        return Snapshot();
    }

    // ---- Part level fields ----

    public EditorSnapshot SetPartField(int index, string field, string value)
    {
        var part = Part(index);
        if (part is null) return Snapshot();
        value ??= string.Empty;
        switch (field)
        {
            case "id": part.Id = value; break;
            case "skill": part.Skill = value; break;
            case "title": part.Title = value; break;
            case "topic": part.Topic = value; break;
            case "taskType": part.TaskType = value; break;
            case "instructions": part.Instructions = value; break;
            case "material": part.Material = value; break;
            case "audioFile": part.AudioFile = value; break;
            case "minutes": part.Minutes = ParseInt(value, part.Minutes); break;
            case "prepSeconds": part.PrepSeconds = ParseInt(value, part.PrepSeconds); break;
        }
        Validate();
        return Snapshot();
    }

    // ---- Question level fields ----

    public EditorSnapshot SetQuestionField(int partIndex, int questionIndex, string field, string value)
    {
        var q = Question(partIndex, questionIndex);
        if (q is null) return Snapshot();
        value ??= string.Empty;
        switch (field)
        {
            case "number": q.Number = ParseInt(value, q.Number); break;
            case "kind": q.Kind = value; break;
            case "prompt": q.Prompt = value; break;
            case "options": q.OptionsText = value; break;
            case "correctKey": q.CorrectKey = value; break;
            case "gapAnswer": q.GapAnswer = value; break;
            case "bank": q.BankText = value; break;
            case "matchRows": q.MatchRowsText = value; break;
            case "explanation": q.Explanation = value; break;
        }
        Validate();
        return Snapshot();
    }

    public EditorSnapshot SelectPart(int index)
    {
        _selectedPart = index;
        _selectedQuestion = Part(index)?.Questions.Count > 0 ? 0 : -1;
        return Snapshot();
    }

    public EditorSnapshot SelectQuestion(int index)
    {
        _selectedQuestion = index;
        return Snapshot();
    }

    // ---- Paper actions ----

    public EditorSnapshot NewPaper()
    {
        _originalTitle = string.Empty;
        _paperTitle = "My new paper";
        _source = "Created in the Editor.";
        _category = "Imported";
        _level = string.Empty;
        _tagsText = "manual";
        _parts.Clear();
        AddPart("Reading");
        _status = "Blank paper ready. Edit the part, add questions, then save.";
        return Snapshot();
    }

    public EditorSnapshot OpenPaper(string title)
    {
        var paper = _repository.GetPaper(title);
        if (paper is null)
        {
            _status = "That paper is gone. Reload the Library.";
            return Snapshot();
        }
        LoadModel(paper);
        _originalTitle = paper.Title;
        _status = $"Editing {paper.Title}. Save to keep changes.";
        return Snapshot();
    }

    /// <summary>Loads an unsaved draft, for example from AI or the Library.</summary>
    public EditorSnapshot LoadDraft(ExamPaper paper)
    {
        LoadModel(paper);
        _originalTitle = string.Empty;
        _status = $"Draft ready for {paper.Title}. Check every question, then save.";
        return Snapshot();
    }

    private void LoadModel(ExamPaper paper)
    {
        _originalTitle = paper.Title;
        _paperTitle = paper.Title;
        _source = paper.Source;
        _category = paper.Category;
        _level = paper.Level;
        _tagsText = string.Join(", ", paper.Tags);
        _parts.Clear();
        foreach (var p in paper.Parts) _parts.Add(EditorPartState.FromModel(p));
        _selectedPart = _parts.Count > 0 ? 0 : -1;
        _selectedQuestion = Part(0)?.Questions.Count > 0 ? 0 : -1;
        Validate();
    }

    private ExamPaper BuildModel() => new()
    {
        Title = _paperTitle.Trim(),
        Source = _source.Trim(),
        Category = _category.Trim(),
        Level = _level.Trim(),
        Tags = _tagsText.Split(new[] { ',', ';', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Parts = _parts.Select(p => p.ToModel()).ToList(),
    };

    public EditorSnapshot Validate()
    {
        _issues = PaperValidator.Validate(BuildModel()).ToList();
        return Snapshot();
    }

    private List<string> _issues = new();

    public EditorSnapshot AddPart(string skill)
    {
        var clean = string.IsNullOrWhiteSpace(skill) ? "Reading" : skill.Trim();
        int n = _parts.Count + 1;
        var part = new EditorPartState
        {
            Id = $"{char.ToUpperInvariant(clean[0])}{n}",
            Skill = clean,
            Title = $"{clean} Part {n}",
            Minutes = clean == "Writing" ? 40 : 12,
        };
        _parts.Add(part);
        _selectedPart = _parts.Count - 1;
        _selectedQuestion = -1;
        Validate();
        _status = $"Added {part.Id}. Fill in the material and questions.";
        return Snapshot();
    }

    public EditorSnapshot RemovePart(int index)
    {
        if (index < 0 || index >= _parts.Count) return Snapshot();
        _parts.RemoveAt(index);
        _selectedPart = _parts.Count > 0 ? 0 : -1;
        _selectedQuestion = Part(_selectedPart)?.Questions.Count > 0 ? 0 : -1;
        Validate();
        return Snapshot();
    }

    public EditorSnapshot AddQuestion(int partIndex)
    {
        var part = Part(partIndex);
        if (part is null) { _status = "Add a part first."; return Snapshot(); }
        int next = part.Questions.Count == 0 ? 1 : part.Questions.Max(q => q.Number) + 1;
        part.Questions.Add(new EditorQuestionState { Number = next });
        _selectedPart = partIndex;
        _selectedQuestion = part.Questions.Count - 1;
        Validate();
        return Snapshot();
    }

    public EditorSnapshot RemoveQuestion(int partIndex, int questionIndex)
    {
        var part = Part(partIndex);
        if (part is null || questionIndex < 0 || questionIndex >= part.Questions.Count) return Snapshot();
        part.Questions.RemoveAt(questionIndex);
        _selectedQuestion = part.Questions.Count > 0 ? 0 : -1;
        Validate();
        return Snapshot();
    }

    public EditorSnapshot Save()
    {
        Validate();
        if (_issues.Count > 0) { _status = _issues[0]; return Snapshot(); }
        var paper = BuildModel();
        var result = string.IsNullOrWhiteSpace(_originalTitle)
            ? _repository.SavePaper(paper)
            : _repository.UpdatePaper(_originalTitle, paper);
        if (!result.Success) { _status = result.Error; return Snapshot(); }
        _originalTitle = result.Title;
        _status = $"Saved {result.Title}. Open Mock Test to run it.";
        return Snapshot();
    }

    public EditorSnapshot Delete()
    {
        var target = string.IsNullOrWhiteSpace(_originalTitle) ? _paperTitle.Trim() : _originalTitle;
        if (string.IsNullOrWhiteSpace(target)) { _status = "Nothing to delete."; return Snapshot(); }
        if (!_repository.IsUserPaper(target))
        {
            _status = "Built in papers cannot be deleted. Duplicate it first to make your own copy.";
            return Snapshot();
        }
        _status = _repository.DeleteUserPaper(target) ? $"Deleted {target}." : "Could not delete that paper.";
        if (_repository.IsUserPaper(target)) return Snapshot();
        NewPaper();
        return Snapshot();
    }

    public EditorSnapshot Duplicate()
    {
        var paper = BuildModel();
        paper.Title = paper.Title + " copy";
        _paperTitle = paper.Title;
        _originalTitle = string.Empty;
        Validate();
        _status = "Duplicated. Save to keep the copy.";
        return Snapshot();
    }

    /// <summary>Saves when valid, then runs the whole paper in Mock Test.</summary>
    public EditorSnapshot TestPaper()
    {
        Validate();
        if (_issues.Count > 0) { _status = _issues[0]; return Snapshot(); }
        var paper = BuildModel();
        var result = string.IsNullOrWhiteSpace(_originalTitle)
            ? _repository.SavePaper(paper)
            : _repository.UpdatePaper(_originalTitle, paper);
        if (!result.Success) { _status = result.Error; return Snapshot(); }
        _originalTitle = result.Title;
        var saved = _repository.GetPaper(result.Title);
        if (saved is null) { _status = "Saved, but the paper could not be reloaded."; return Snapshot(); }
        _exam.StartCustomTest(saved.Parts.Select(p => (saved.Title, p.Id)), $"Testing {saved.Title}.");
        return Snapshot(navigateTo: "mock");
    }

    /// <summary>Imports a picked or dropped file into the current part by its text.</summary>
    public EditorSnapshot ImportContent(string fileName, string content)
        => ImportContentCore(fileName, content, null);

    /// <summary>Imports a binary document (PDF, DOCX) into the current part.</summary>
    public EditorSnapshot ImportBinary(string fileName, string base64)
        => ImportContentCore(fileName, null, base64);

    private EditorSnapshot ImportContentCore(string fileName, string? content, string? base64)
    {
        var part = Part(_selectedPart);
        if (part is null) { _status = "Add a part first, then import text into it."; return Snapshot(); }

        if (base64 is not null)
        {
            var extracted = ExtractBinary(fileName, base64);
            if (!extracted.Success) { _status = extracted.Error; return Snapshot(); }
            content = extracted.Text;
        }
        content ??= string.Empty;

        if (Path.GetExtension(fileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (PaperValidator.TryParse(content, out var paper, out _) && paper is not null)
            {
                LoadModel(paper);
                _originalTitle = string.Empty;
                _status = "Loaded a standard paper. Save it as your own copy.";
                return Snapshot();
            }
            part.Material = content.Trim();
            _status = "That JSON is not a valid paper, loaded as raw text. Fix it or pick another file.";
            return Snapshot();
        }

        part.Material = content;
        if (string.IsNullOrWhiteSpace(part.Title) || part.Title.StartsWith("Reading Part"))
            part.Title = Path.GetFileNameWithoutExtension(fileName);
        _status = $"Imported {part.Material.Length} characters into {part.Id}. Add questions below.";
        Validate();
        return Snapshot();
    }

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

    public EditorSnapshot BuildPartFromPaste()
    {
        var part = Part(_selectedPart);
        if (part is null) { _status = "Add a part first."; return Snapshot(); }
        if (string.IsNullOrWhiteSpace(_pasteText)) { _status = "Paste some text first."; return Snapshot(); }
        part.Material = _pasteText.Trim();
        part.Skill = string.IsNullOrWhiteSpace(_pasteSkill) ? part.Skill : _pasteSkill.Trim();
        _pasteText = string.Empty;
        Validate();
        _status = $"Pasted into {part.Id}. Add questions below, then save.";
        return Snapshot();
    }

    public async Task<EditorSnapshot> AiDraftAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pasteText)) { _status = "Paste some text first."; return Snapshot(); }
        if (!_ai.IsAvailable) { _status = "Add a language model in Settings for AI drafts."; return Snapshot(); }

        _busy = true;
        _aiCts?.Cancel();
        _aiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _status = "Working. AI is drafting the paper.";
        try
        {
            var draft = await _ai.DraftPaperAsync(_pasteText.Trim(), _pasteSkill, _aiCts.Token);
            if (!draft.Success || draft.Paper is null)
            {
                if (!string.IsNullOrWhiteSpace(draft.Json)
                    && PaperValidator.TryParse(draft.Json, out var raw, out _) && raw is not null)
                {
                    LoadModel(raw);
                    _originalTitle = string.Empty;
                }
                _status = draft.Error;
                return Snapshot();
            }
            LoadModel(draft.Paper);
            _originalTitle = string.Empty;
            _pasteText = string.Empty;
            _status = $"Draft ready for {draft.Paper.Title}. Check every question, then save.";
            return Snapshot();
        }
        catch (OperationCanceledException) { _status = "Draft cancelled."; return Snapshot(); }
        catch (Exception) { _status = "The model could not be reached. Check Settings and try again."; return Snapshot(); }
        finally { _busy = false; }
    }

    public EditorSnapshot CancelAi()
    {
        _aiCts?.Cancel();
        _status = "Stopping.";
        return Snapshot();
    }

    /// <summary>
    /// Reads pictures with a vision model straight into the current part:
    /// scans, photos, charts, Word images. Check the text before saving.
    /// </summary>
    public async Task<EditorSnapshot> ReadImagesIntoPartAsync(
        IReadOnlyList<ImportedImage> images, CancellationToken ct)
    {
        var part = Part(_selectedPart);
        if (part is null) { _status = "Add a part first, then read pictures into it."; return Snapshot(); }
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
                var read = await _ai.ReadImportImageAsync(image, part.Skill, _aiCts.Token);
                texts.Add(read.Success
                    ? $"[Picture {total}: {image.Name}]\n{read.Text.Trim()}"
                    : $"[{image.Name}: {read.Error}]");
            }
            var combined = string.Join("\n\n", texts.Where(t => t.Length > 0));
            part.Material = string.IsNullOrWhiteSpace(part.Material)
                ? combined
                : part.Material.Trim() + "\n\n" + combined;
            _status = $"Read {total} picture(s) into {part.Id}. Check the text, then save.";
            Validate();
        }
        catch (OperationCanceledException) { _status = "Reading stopped."; }
        catch (Exception) { _status = "The model could not be reached. Check Settings and try again."; }
        finally { _busy = false; }
        return Snapshot();
    }

    private EditorPartState? Part(int index) =>
        index >= 0 && index < _parts.Count ? _parts[index] : null;

    private EditorQuestionState? Question(int partIndex, int questionIndex)
    {
        var part = Part(partIndex);
        return part is not null && questionIndex >= 0 && questionIndex < part.Questions.Count
            ? part.Questions[questionIndex] : null;
    }

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value?.Trim(), out var n) ? n : fallback;

    public EditorSnapshot Snapshot(string navigateTo = "")
    {
        bool hasQuestions = Part(_selectedPart)?.Questions.Count > 0;
        return new EditorSnapshot
        {
            PaperTitle = _paperTitle,
            Source = _source,
            Category = _category,
            Level = _level,
            TagsText = _tagsText,
            PasteText = _pasteText,
            PasteSkill = _pasteSkill,
            StatusMessage = _status,
            IsBusy = _busy,
            Parts = _parts.ToList(),
            SelectedPartIndex = _selectedPart,
            SelectedQuestionIndex = _selectedQuestion,
            ValidationIssues = _issues.ToList(),
            ValidationSummary = _issues.Count == 0
                ? "No problems found. Ready to save."
                : $"{_issues.Count} problem(s) to fix before saving.",
            HasParts = _parts.Count > 0,
            HasQuestions = hasQuestions,
            CanUseAi = _ai.IsAvailable && !_busy,
            AiHint = _ai.IsAvailable
                ? "AI turns pasted text into a full draft. Check every question before saving."
                : "Add a language model in Settings to let AI draft questions.",
            CanUseVision = _ai.VisionAvailable && !_busy,
            VisionHint = !_ai.IsAvailable
                ? "Add a language model in Settings to read pictures."
                : !_ai.VisionAvailable
                    ? "Vision is turned off. Enable it in Settings to read pictures."
                    : "Reads pictures from scans, photos, charts, and Word images into the current part.",
            NavigateTo = navigateTo,
        };
    }
}
