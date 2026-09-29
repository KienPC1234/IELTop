using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.Storage;
using Microsoft.Win32;

namespace IELTop.ViewModels;

/// <summary>One question being edited, with text boxes per field.</summary>
public sealed partial class EditorQuestion : ObservableObject
{
    [ObservableProperty] private int _number = 1;
    [ObservableProperty] private string _kind = "choice";
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _optionsText = "A. First option\nB. Second option\nC. Third option";
    [ObservableProperty] private string _correctKey = "A";
    [ObservableProperty] private string _gapAnswer = string.Empty;
    [ObservableProperty] private string _bankText = string.Empty;
    [ObservableProperty] private string _matchRowsText = string.Empty;
    [ObservableProperty] private string _explanation = string.Empty;

    public string Summary => $"Q{Number} {Kind}: {PromptLine}";
    private string PromptLine => Prompt.Length <= 60 ? Prompt : Prompt[..60] + "...";

    public static EditorQuestion FromModel(ExamQuestion q) => new()
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
        Explanation = q.Explanation
    };

    public ExamQuestion ToModel()
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
            var cut = line.Split(new[] { "=>" }, StringSplitOptions.None);
            if (cut.Length != 2) continue;
            if (cut[0].Trim().Length == 0 || cut[1].Trim().Length == 0) continue;
            rows.Add(new ExamMatchRow { Label = cut[0].Trim(), Answer = cut[1].Trim() });
        }
        return new ExamQuestion
        {
            Number = Number,
            Kind = (Kind ?? "choice").Trim().ToLowerInvariant() is string k && (k == "gap" || k == "match") ? k : "choice",
            Prompt = Prompt.Trim(),
            Options = options,
            CorrectKey = CorrectKey.Trim().ToUpperInvariant(),
            GapAnswer = GapAnswer.Trim(),
            Bank = bank,
            MatchRows = rows,
            Explanation = Explanation.Trim()
        };
    }
}

/// <summary>One part being edited, with its question list.</summary>
public sealed partial class EditorPart : ObservableObject
{
    [ObservableProperty] private string _id = "R1";
    [ObservableProperty] private string _skill = "Reading";
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _topic = string.Empty;
    [ObservableProperty] private string _taskType = string.Empty;
    [ObservableProperty] private int _minutes = 12;
    [ObservableProperty] private int _prepSeconds;
    [ObservableProperty] private string _instructions = string.Empty;
    [ObservableProperty] private string _material = string.Empty;
    [ObservableProperty] private string _audioFile = string.Empty;

    public ObservableCollection<EditorQuestion> Questions { get; } = new();

    public string Summary => $"{Id} {Skill}: {Title} ({Questions.Count} questions)";

    public static EditorPart FromModel(ExamPart p)
    {
        var vm = new EditorPart
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
            AudioFile = p.AudioFile
        };
        foreach (var q in p.Questions)
            vm.Questions.Add(EditorQuestion.FromModel(q));
        return vm;
    }

    public ExamPart ToModel() => new()
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
        Questions = Questions.Select(q => q.ToModel()).ToList()
    };
}

/// <summary>
/// Dedicated paper editor: structured fields per part and question,
/// live validation, AI draft import, file text import, save and test run.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly IExamRepository _repository;
    private readonly ExamViewModel _exam;
    private readonly IIeltsAiService _ai;
    private CancellationTokenSource? _aiCts;
    private string _originalTitle = string.Empty;

    [ObservableProperty] private string _paperTitle = "My new paper";
    [ObservableProperty] private string _source = "Created in the Editor.";
    [ObservableProperty] private string _category = "Imported";
    [ObservableProperty] private string _level = string.Empty;
    [ObservableProperty] private string _tagsText = "manual";
    [ObservableProperty] private string _pasteText = string.Empty;
    [ObservableProperty] private string _pasteSkill = "Reading";
    [ObservableProperty] private string _statusMessage = "Create a paper, add parts and questions, then save.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private EditorPart? _selectedPart;
    [ObservableProperty] private EditorQuestion? _selectedQuestion;

    public ObservableCollection<EditorPart> Parts { get; } = new();
    public ObservableCollection<string> ValidationIssues { get; } = new();
    public ObservableCollection<string> Skills { get; } =
        new() { "Listening", "Reading", "Writing", "Speaking" };
    public ObservableCollection<string> Kinds { get; } =
        new() { "choice", "gap", "match" };

    public Action<string>? NavigateTo { get; set; }
    public Action? PapersChanged { get; set; }

    public EditorViewModel(IExamRepository repository, ExamViewModel exam, IIeltsAiService ai)
    {
        _repository = repository;
        _exam = exam;
        _ai = ai;
        NewPaper();
    }

    public bool HasParts => Parts.Count > 0;
    public bool HasQuestions => SelectedPart is not null && SelectedPart.Questions.Count > 0;
    public bool CanUseAi => _ai.IsAvailable && !IsBusy;
    public string AiHint => _ai.IsAvailable
        ? "AI turns pasted text into a full draft. Check every question before saving."
        : "Add a language model in Settings to let AI draft questions.";
    public bool CanUseVision => _ai.VisionAvailable && !IsBusy;
    public string VisionHint => !_ai.IsAvailable
        ? "Add a language model in Settings to read pictures."
        : !_ai.VisionAvailable
            ? "Vision is turned off. Enable it in Settings to read pictures from scans, photos, charts, and Word images."
            : "Reads pictures from scans, photos, charts, and Word images into the current part.";
    public string ValidationSummary => ValidationIssues.Count == 0
        ? "No problems found. Ready to save."
        : $"{ValidationIssues.Count} problem(s) to fix before saving.";

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(CanUseVision));
    }

    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(AiHint));
        OnPropertyChanged(nameof(CanUseVision));
        OnPropertyChanged(nameof(VisionHint));
    }

    [RelayCommand]
    public void NewPaper()
    {
        _originalTitle = string.Empty;
        PaperTitle = "My new paper";
        Source = "Created in the Editor.";
        Category = "Imported";
        Level = string.Empty;
        TagsText = "manual";
        Parts.Clear();
        AddPart("Reading");
        StatusMessage = "Blank paper ready. Edit the part, add questions, then save.";
        Validate();
    }

    /// <summary>Opens one paper for editing, by title.</summary>
    public void OpenPaper(string title)
    {
        var paper = _repository.GetPaper(title);
        if (paper is null)
        {
            StatusMessage = "That paper is gone. Press Reload in the Library.";
            return;
        }
        LoadModel(paper);
        StatusMessage = $"Editing {paper.Title}. Save to keep changes.";
    }

    /// <summary>Loads an unsaved draft model, for example from AI.</summary>
    public void LoadDraft(ExamPaper paper)
    {
        LoadModel(paper);
        _originalTitle = string.Empty;
        StatusMessage = $"Draft ready for {paper.Title}. Check every question, then save.";
    }

    private void LoadModel(ExamPaper paper)
    {
        _originalTitle = paper.Title;
        PaperTitle = paper.Title;
        Source = paper.Source;
        Category = paper.Category;
        Level = paper.Level;
        TagsText = string.Join(", ", paper.Tags);
        Parts.Clear();
        foreach (var p in paper.Parts)
            Parts.Add(EditorPart.FromModel(p));
        SelectedPart = Parts.FirstOrDefault();
        SelectedQuestion = SelectedPart?.Questions.FirstOrDefault();
        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(HasQuestions));
        Validate();
    }

    private ExamPaper BuildModel() => new()
    {
        Title = PaperTitle.Trim(),
        Source = Source.Trim(),
        Category = Category.Trim(),
        Level = Level.Trim(),
        Tags = TagsText.Split(new[] { ',', ';', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Parts = Parts.Select(p => p.ToModel()).ToList()
    };

    [RelayCommand]
    private void Validate()
    {
        ValidationIssues.Clear();
        foreach (var issue in Services.Storage.PaperValidator.Validate(BuildModel()))
            ValidationIssues.Add(issue);
        OnPropertyChanged(nameof(ValidationSummary));
    }

    [RelayCommand]
    private void AddPart(string? skill)
    {
        var clean = string.IsNullOrWhiteSpace(skill) ? "Reading" : skill.Trim();
        int n = Parts.Count + 1;
        var part = new EditorPart
        {
            Id = $"{char.ToUpperInvariant(clean[0])}{n}",
            Skill = clean,
            Title = $"{clean} Part {n}",
            Minutes = clean == "Writing" ? 40 : 12
        };
        Parts.Add(part);
        SelectedPart = part;
        OnPropertyChanged(nameof(HasParts));
        Validate();
        StatusMessage = $"Added {part.Id}. Fill in the material and questions.";
    }

    [RelayCommand]
    private void RemovePart(EditorPart? part)
    {
        if (part is null) return;
        Parts.Remove(part);
        SelectedPart = Parts.FirstOrDefault();
        SelectedQuestion = SelectedPart?.Questions.FirstOrDefault();
        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(HasQuestions));
        Validate();
    }

    [RelayCommand]
    private void AddQuestion()
    {
        if (SelectedPart is null)
        {
            StatusMessage = "Add a part first.";
            return;
        }
        int next = SelectedPart.Questions.Count == 0
            ? 1 : SelectedPart.Questions.Max(q => q.Number) + 1;
        var q = new EditorQuestion { Number = next };
        SelectedPart.Questions.Add(q);
        SelectedQuestion = q;
        OnPropertyChanged(nameof(HasQuestions));
        Validate();
    }

    [RelayCommand]
    private void RemoveQuestion(EditorQuestion? question)
    {
        if (SelectedPart is null || question is null) return;
        SelectedPart.Questions.Remove(question);
        SelectedQuestion = SelectedPart.Questions.FirstOrDefault();
        OnPropertyChanged(nameof(HasQuestions));
        Validate();
    }

    partial void OnSelectedPartChanged(EditorPart? value)
    {
        SelectedQuestion = value?.Questions.FirstOrDefault();
        OnPropertyChanged(nameof(HasQuestions));
    }

    [RelayCommand]
    private void Save()
    {
        Validate();
        if (ValidationIssues.Count > 0)
        {
            StatusMessage = ValidationIssues[0];
            return;
        }
        var paper = BuildModel();
        ImportResult result = string.IsNullOrWhiteSpace(_originalTitle)
            ? _repository.SavePaper(paper)
            : _repository.UpdatePaper(_originalTitle, paper);
        if (!result.Success)
        {
            StatusMessage = result.Error;
            return;
        }
        _originalTitle = result.Title;
        PapersChanged?.Invoke();
        StatusMessage = $"Saved {result.Title}. Open Mock Test to run it.";
    }

    [RelayCommand]
    private void Delete()
    {
        string target = string.IsNullOrWhiteSpace(_originalTitle) ? PaperTitle.Trim() : _originalTitle;
        if (string.IsNullOrWhiteSpace(target))
        {
            StatusMessage = "Nothing to delete.";
            return;
        }
        if (!_repository.IsUserPaper(target))
        {
            StatusMessage = "Built in papers cannot be deleted. Duplicate it first to make your own copy.";
            return;
        }
        var ask = System.Windows.MessageBox.Show(
            $"Delete {target} from this computer?",
            "Delete paper", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (ask != System.Windows.MessageBoxResult.Yes) return;
        if (_repository.DeleteUserPaper(target))
        {
            PapersChanged?.Invoke();
            NewPaper();
            StatusMessage = $"Deleted {target}.";
        }
        else StatusMessage = "Could not delete that paper.";
    }

    [RelayCommand]
    private void Duplicate()
    {
        var paper = BuildModel();
        paper.Title = paper.Title + " copy";
        PaperTitle = paper.Title;
        _originalTitle = string.Empty;
        Validate();
        StatusMessage = "Duplicated. Save to keep the copy.";
    }

    /// <summary>Saves when valid, then runs the whole paper in Mock Test.</summary>
    [RelayCommand]
    private void TestPaper()
    {
        Validate();
        if (ValidationIssues.Count > 0)
        {
            StatusMessage = ValidationIssues[0];
            return;
        }
        var paper = BuildModel();
        ImportResult result = string.IsNullOrWhiteSpace(_originalTitle)
            ? _repository.SavePaper(paper)
            : _repository.UpdatePaper(_originalTitle, paper);
        if (!result.Success)
        {
            StatusMessage = result.Error;
            return;
        }
        _originalTitle = result.Title;
        PapersChanged?.Invoke();
        var saved = _repository.GetPaper(result.Title);
        if (saved is null)
        {
            StatusMessage = "Saved, but the paper could not be reloaded.";
            return;
        }
        _exam.StartCustomTest(
            saved.Parts.Select(p => (saved.Title, p.Id)), $"Testing {saved.Title}.");
        NavigateTo?.Invoke("Exam");
    }

    [RelayCommand]
    private void ImportFileIntoPart()
    {
        if (SelectedPart is null)
        {
            StatusMessage = "Add a part first, then import text into it.";
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Choose a text file",
            Filter = FileTextExtractor.SupportedFilter,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;
        if (Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(dialog.FileName);
                if (Services.Storage.PaperValidator.TryParse(
                    json, out var paper, out _) && paper is not null)
                {
                    LoadModel(paper);
                    _originalTitle = string.Empty;
                    StatusMessage = "Loaded a standard paper. Save it as your own copy.";
                    return;
                }
                SelectedPart.Material = json.Trim();
                StatusMessage = "That JSON is not a valid paper, loaded as raw text. Fix it or pick another file.";
            }
            catch (Exception) { StatusMessage = "Could not read that file."; }
            return;
        }
        if (!FileTextExtractor.TryExtract(dialog.FileName, out var text, out var error))
        {
            StatusMessage = error;
            return;
        }
        SelectedPart.Material = text;
        if (string.IsNullOrWhiteSpace(SelectedPart.Title) || SelectedPart.Title.StartsWith("Reading Part"))
            SelectedPart.Title = Path.GetFileNameWithoutExtension(dialog.FileName);
        StatusMessage = $"Imported {text.Length} characters into {SelectedPart.Id}. Add questions below.";
    }

    /// <summary>
    /// Reads pictures with a vision model straight into the current part:
    /// scans, photos, charts, Word images. Check the text before saving.
    /// </summary>
    [RelayCommand]
    private async Task ReadImageIntoPartAsync()
    {
        if (IsBusy) return;
        if (SelectedPart is null)
        {
            StatusMessage = "Add a part first, then read pictures into it.";
            return;
        }
        if (!_ai.VisionAvailable)
        {
            StatusMessage = !_ai.IsAvailable
                ? "Add a language model in Settings to read pictures."
                : "Vision is turned off. Enable it in Settings to read pictures.";
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Choose pictures, PDF, or Word files to read",
            Filter = ImageExtractor.VisionSourceFilter,
            Multiselect = true
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

        IsBusy = true;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        StatusMessage = "Working. Reading pictures with the vision model.";
        try
        {
            var texts = new List<string>();
            int total = 0;
            foreach (var path in dialog.FileNames)
            {
                _aiCts.Token.ThrowIfCancellationRequested();
                if (!ImageExtractor.TryExtract(path, out var images, out var error))
                {
                    texts.Add($"[{Path.GetFileName(path)}: {error}]");
                    continue;
                }
                foreach (var image in images)
                {
                    _aiCts.Token.ThrowIfCancellationRequested();
                    total++;
                    StatusMessage = $"Working. Reading picture {total}: {image.Name}.";
                    var read = await _ai.ReadImportImageAsync(image, SelectedPart.Skill, _aiCts.Token);
                    texts.Add(read.Success
                        ? $"[Picture {total}: {image.Name}]\n{read.Text.Trim()}"
                        : $"[{image.Name}: {read.Error}]");
                }
            }
            var combined = string.Join("\n\n", texts.Where(t => t.Length > 0));
            SelectedPart.Material = string.IsNullOrWhiteSpace(SelectedPart.Material)
                ? combined
                : SelectedPart.Material.Trim() + "\n\n" + combined;
            StatusMessage = total == 0
                ? "No pictures could be read. See the notes in the material box."
                : $"Read {total} picture(s) into {SelectedPart.Id}. Check the text, then save.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Reading stopped.";
        }
        catch (Exception)
        {
            StatusMessage = "The model could not be reached. Check Settings and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BuildPartFromPaste()
    {
        if (SelectedPart is null)
        {
            StatusMessage = "Add a part first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            StatusMessage = "Paste some text first.";
            return;
        }
        SelectedPart.Material = PasteText.Trim();
        SelectedPart.Skill = string.IsNullOrWhiteSpace(PasteSkill) ? SelectedPart.Skill : PasteSkill.Trim();
        PasteText = string.Empty;
        StatusMessage = $"Pasted into {SelectedPart.Id}. Add questions below, then save.";
    }

    [RelayCommand]
    private async Task AiDraftAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            StatusMessage = "Paste some text first, then ask AI to draft.";
            return;
        }
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings for AI drafts.";
            return;
        }
        IsBusy = true;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        StatusMessage = "Working. AI is drafting the paper.";
        try
        {
            var draft = await _ai.DraftPaperAsync(PasteText.Trim(), PasteSkill, _aiCts.Token);
            if (!draft.Success || draft.Paper is null)
            {
                // Keep the raw JSON when the model replied but validation failed,
                // so the user can fix it by hand instead of losing it.
                if (!string.IsNullOrWhiteSpace(draft.Json) &&
                    Services.Storage.PaperValidator.TryParse(
                        draft.Json, out var raw, out _) && raw is not null)
                {
                    LoadModel(raw);
                    _originalTitle = string.Empty;
                }
                StatusMessage = draft.Error;
                return;
            }
            LoadModel(draft.Paper);
            _originalTitle = string.Empty;
            PasteText = string.Empty;
            StatusMessage = $"Draft ready for {draft.Paper.Title}. Check every question, then save.";
        }
        catch (OperationCanceledException) { StatusMessage = "Draft cancelled."; }
        catch (Exception) { StatusMessage = "The model could not be reached. Check Settings and try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void CancelAi()
    {
        _aiCts?.Cancel();
        StatusMessage = "Stopping.";
    }
}
