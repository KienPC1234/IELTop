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

/// <summary>One paper waiting in the custom test basket.</summary>
public sealed record BasketItem(string PaperTitle, string Detail);

/// <summary>
/// Paper library: browse every paper with grouping, filters and tags,
/// and drag papers into a basket that starts one custom mixed test.
/// Also imports IELTop JSON, plain text files, pasted text, and manual drafts.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IExamRepository _repository;
    private readonly ExamViewModel _exam;
    private readonly IIeltsAiService _ai;
    private CancellationTokenSource? _aiCts;

    public EditorViewModel? Editor { get; set; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedCategory = "All categories";
    [ObservableProperty] private string _selectedSkill = "All skills";
    [ObservableProperty] private string _statusMessage = "Pick papers, drop them in the basket, then start.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _pasteText = string.Empty;
    [ObservableProperty] private string _draftJson = string.Empty;
    [ObservableProperty] private string _draftSkill = "Reading";
    [ObservableProperty] private string _draftTitle = string.Empty;

    private readonly List<ExamPaper> _allPapers = new();

    public ObservableCollection<PaperRow> Rows { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Skills { get; } =
        new() { "All skills", "Listening", "Reading", "Writing", "Speaking" };
    public ObservableCollection<string> DraftSkills { get; } =
        new() { "Listening", "Reading", "Writing", "Speaking" };
    public ObservableCollection<BasketItem> Basket { get; } = new();

    /// <summary>Set by the shell to change pages, for example to Exam.</summary>
    public Action<string>? NavigateTo { get; set; }

    public LibraryViewModel(IExamRepository repository, ExamViewModel exam, IIeltsAiService ai)
    {
        _repository = repository;
        _exam = exam;
        _ai = ai;
        Load();
    }

    public bool HasBasket => Basket.Count > 0;
    public string BasketLabel => HasBasket
        ? $"{Basket.Count} paper(s) in the basket."
        : "The basket is empty. Drag papers here or press +.";
    public bool HasRows => Rows.Count > 0;
    public string LibrarySummary => _allPapers.Count == 0
        ? "No papers yet."
        : Rows.Count == _allPapers.Count
            ? $"{_allPapers.Count} paper(s) in the library."
            : $"Showing {Rows.Count} of {_allPapers.Count} paper(s). Filters hide the rest.";
    public bool CanUseAi => _ai.IsAvailable && !IsBusy;
    public string AiHint => _ai.IsAvailable
        ? "AI formats pasted text into questions. Review the draft before saving."
        : "Add a language model in Settings to let AI draft questions.";
    public bool CanUseVision => _ai.VisionAvailable && !IsBusy;
    public string VisionHint => !_ai.IsAvailable
        ? "Add a language model in Settings to read pictures."
        : !_ai.VisionAvailable
            ? "Vision is turned off. Enable it in Settings to read pictures from scans, photos, charts, and Word images."
            : "Reads pictures from scans, photos, charts, and Word images into text you can edit.";

    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(AiHint));
        OnPropertyChanged(nameof(CanUseVision));
        OnPropertyChanged(nameof(VisionHint));
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSelectedSkillChanged(string value) => ApplyFilter();
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(CanUseVision));
    }

    [RelayCommand]
    private void Load()
    {
        _allPapers.Clear();
        _allPapers.AddRange(_repository.LoadPapers());

        Categories.Clear();
        Categories.Add("All categories");
        foreach (var category in _allPapers
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            Categories.Add(category);
        // A saved filter can point at a category that no longer exists
        // after a delete. Reset it instead of showing an empty library.
        if (SelectedCategory != "All categories" && !Categories.Any(c =>
            string.Equals(c, SelectedCategory, StringComparison.OrdinalIgnoreCase)))
            SelectedCategory = "All categories";

        ApplyFilter();
        StatusMessage = _allPapers.Count == 0
            ? "No papers yet. Import a file below or open Servers to download some."
            : Rows.Count == 0
                ? "Filters hide every paper. Press Clear filters to see the full library."
                : $"{_allPapers.Count} paper(s) in the library.";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedCategory = "All categories";
        SelectedSkill = "All skills";
        StatusMessage = $"{_allPapers.Count} paper(s) in the library.";
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var query = SearchText.Trim();
        foreach (var paper in _allPapers)
        {
            if (SelectedCategory != "All categories" && !string.Equals(
                paper.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
                continue;
            if (SelectedSkill != "All skills" && !paper.Parts.Any(p =>
                string.Equals(p.Skill, SelectedSkill, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (query.Length > 0 && !paper.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !paper.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
                continue;
            Rows.Add(new PaperRow(paper, _repository.IsUserPaper(paper.Title)));
        }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(LibrarySummary));
    }

    [RelayCommand]
    private void AddToBasket(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        var paper = _allPapers.FirstOrDefault(p =>
            string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
        if (paper is null) return;
        if (Basket.Any(b => string.Equals(b.PaperTitle, paper.Title, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"{paper.Title} is already in the basket.";
            return;
        }
        Basket.Add(new BasketItem(paper.Title,
            $"{paper.Parts.Count} parts, {paper.Parts.Sum(p => p.Questions.Count)} questions, " +
            $"{string.Join(", ", paper.Parts.Select(p => p.Skill).Distinct())}"));
        OnPropertyChanged(nameof(HasBasket));
        OnPropertyChanged(nameof(BasketLabel));
        StatusMessage = $"Added {paper.Title} to the basket.";
    }

    [RelayCommand]
    private void RemoveFromBasket(BasketItem? item)
    {
        if (item is null) return;
        Basket.Remove(item);
        OnPropertyChanged(nameof(HasBasket));
        OnPropertyChanged(nameof(BasketLabel));
    }

    [RelayCommand]
    private void ClearBasket()
    {
        Basket.Clear();
        OnPropertyChanged(nameof(HasBasket));
        OnPropertyChanged(nameof(BasketLabel));
        StatusMessage = "Basket cleared.";
    }

    /// <summary>Opens one paper in Mock Test setup without starting yet.</summary>
    [RelayCommand]
    private void StartPaper(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        _exam.Load();
        Load();
        var paper = _exam.Papers.FirstOrDefault(p =>
            string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
        if (paper is null)
        {
            StatusMessage = "That paper is gone. Press Reload.";
            return;
        }
        _exam.SelectedPaper = paper;
        _exam.SelectedScope = "Full test";
        NavigateTo?.Invoke("Exam");
        StatusMessage = $"Loaded {paper.Title}. Press Start test when ready.";
    }

    /// <summary>Opens one paper in the dedicated editor.</summary>
    [RelayCommand]
    private void EditPaper(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || Editor is null) return;
        Editor.OpenPaper(title);
        NavigateTo?.Invoke("Editor");
    }

    [RelayCommand]
    private void DuplicatePaper(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        var paper = _repository.GetPaper(title);
        if (paper is null)
        {
            StatusMessage = "That paper is gone. Press Reload.";
            return;
        }
        paper.Title = paper.Title + " copy";
        var result = _repository.SavePaper(paper);
        if (!result.Success)
        {
            StatusMessage = result.Error;
            return;
        }
        Load();
        _exam.Load();
        StatusMessage = $"Saved {result.Title}. Edit it to make it yours.";
    }

    [RelayCommand]
    private void DeletePaper(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        if (!_repository.IsUserPaper(title))
        {
            StatusMessage = "Built in papers cannot be deleted.";
            return;
        }
        var ask = System.Windows.MessageBox.Show(
            $"Delete {title} from this computer?",
            "Delete paper", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (ask != System.Windows.MessageBoxResult.Yes) return;
        if (_repository.DeleteUserPaper(title))
        {
            var stale = Basket.FirstOrDefault(b =>
                string.Equals(b.PaperTitle, title, StringComparison.OrdinalIgnoreCase));
            if (stale is not null) Basket.Remove(stale);
            OnPropertyChanged(nameof(HasBasket));
            OnPropertyChanged(nameof(BasketLabel));
            Load();
            _exam.Load();
            StatusMessage = $"Deleted {title}.";
        }
        else StatusMessage = "Could not delete that paper.";
    }

    [RelayCommand]
    private void StartBasket()
    {
        if (Basket.Count == 0)
        {
            StatusMessage = "The basket is empty. Drag papers into it first.";
            return;
        }
        var picks = new List<(string PaperTitle, string PartId)>();
        foreach (var item in Basket)
        {
            var paper = _allPapers.FirstOrDefault(p =>
                string.Equals(p.Title, item.PaperTitle, StringComparison.OrdinalIgnoreCase));
            if (paper is null) continue;
            foreach (var part in paper.Parts)
                picks.Add((paper.Title, part.Id));
        }
        _exam.StartCustomTest(picks, $"Custom test from {Basket.Count} paper(s).");
        NavigateTo?.Invoke("Exam");
    }

    [RelayCommand]
    private void GoServers() => NavigateTo?.Invoke("Servers");

    [RelayCommand]
    private async Task ImportPapers()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose files to import",
            Filter = FileTextExtractor.SupportedFilter,
            Multiselect = true
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

        IsBusy = true;
        StatusMessage = "Working. Reading the selected files.";
        try
        {
            await Task.Run(() => ImportFiles(dialog.FileNames));
        }
        finally { IsBusy = false; }
    }

    private void ImportFiles(string[] paths)
    {
        int ok = 0;
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".json")
            {
                string raw;
                try { raw = File.ReadAllText(path); }
                catch (Exception)
                {
                    problems.Add($"{Path.GetFileName(path)}: could not be read.");
                    continue;
                }
                var direct = _repository.ImportPaper(raw);
                if (direct.Success) { ok++; continue; }
                problems.Add($"{Path.GetFileName(path)}: {direct.Error}");
                continue;
            }

            if (!FileTextExtractor.TryExtract(path, out var text, out var error))
            {
                problems.Add($"{Path.GetFileName(path)}: {error}");
                continue;
            }
            var paper = BuildOfflinePaper(
                Path.GetFileNameWithoutExtension(path), text, DraftSkill);
            var result = _repository.SavePaper(paper);
            if (result.Success) ok++;
            else problems.Add($"{Path.GetFileName(path)}: {result.Error}");
        }

        Load();
        _exam.Load();
        StatusMessage = problems.Count == 0
            ? $"Imported {ok} paper(s). Text files were saved as {DraftSkill} drafts. Open the Editor to add questions."
            : $"Imported {ok}, skipped {problems.Count}: {string.Join(" ", problems.Take(3))}";
    }

    /// <summary>Saves pasted text as an offline draft part, no model needed.</summary>
    [RelayCommand]
    private void BuildDraftFromPaste()
    {
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            StatusMessage = "Paste some text first, then build a draft.";
            return;
        }
        var title = string.IsNullOrWhiteSpace(DraftTitle)
            ? $"Pasted {DraftSkill} {DateTime.Now:yyyyMMdd-HHmm}"
            : DraftTitle.Trim();
        var paper = BuildOfflinePaper(title, PasteText.Trim(), DraftSkill);
        var result = _repository.SavePaper(paper);
        if (!result.Success)
        {
            StatusMessage = result.Error;
            return;
        }
        PasteText = string.Empty;
        Load();
        _exam.Load();
        StatusMessage = $"Saved {result.Title} as an offline draft. Add questions by editing its JSON.";
    }

    /// <summary>Asks the model to turn pasted text into questions. Opens the editor.</summary>
    [RelayCommand]
    private async Task AiFormatPasteAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(PasteText))
        {
            StatusMessage = "Paste some text first, then ask AI to format it.";
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
        StatusMessage = "Working. AI is drafting questions.";
        try
        {
            var draft = await _ai.DraftPaperAsync(PasteText.Trim(), DraftSkill, _aiCts.Token);
            if (!draft.Success || draft.Paper is null)
            {
                StatusMessage = draft.Error;
                if (!string.IsNullOrWhiteSpace(draft.Json))
                {
                    DraftJson = draft.Json;
                    StatusMessage += " The raw reply is in the JSON box below for hand fixes.";
                }
                return;
            }
            if (!string.IsNullOrWhiteSpace(DraftTitle))
                draft.Paper.Title = DraftTitle.Trim();
            DraftJson = draft.Json;
            Editor?.LoadDraft(draft.Paper);
            StatusMessage = $"Draft ready for {draft.Paper.Title}. Opened in the Editor. Check every question, then save.";
            NavigateTo?.Invoke("Editor");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Draft cancelled.";
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

    /// <summary>Sends the JSON draft into the structured editor.</summary>
    [RelayCommand]
    private void OpenDraftInEditor()
    {
        if (Editor is null) return;
        if (string.IsNullOrWhiteSpace(DraftJson))
        {
            StatusMessage = "No draft yet. Make a template or ask AI first.";
            return;
        }
        if (!Services.Storage.PaperValidator.TryParse(DraftJson, out var paper, out var issues) || paper is null)
        {
            StatusMessage = issues.Count > 0 ? issues[0] : "The draft needs fixes before editing.";
            return;
        }
        Editor.LoadDraft(paper);
        NavigateTo?.Invoke("Editor");
    }

    /// <summary>Opens the editor for a fresh paper.</summary>
    [RelayCommand]
    private void OpenEditor()
    {
        NavigateTo?.Invoke("Editor");
    }

    /// <summary>Starts a blank paper in the editor in one press.</summary>
    [RelayCommand]
    private void NewPaper()
    {
        if (Editor is null)
        {
            StatusMessage = "The editor is not ready. Try again.";
            return;
        }
        Editor.NewPaper();
        NavigateTo?.Invoke("Editor");
        StatusMessage = "Blank paper ready in the Editor.";
    }

    /// <summary>
    /// Reads pictures with a vision model: scans, photos, charts, Word
    /// images. The text lands in the paste box for review, never auto saved.
    /// </summary>
    [RelayCommand]
    private async Task ReadImagesAsync()
    {
        if (IsBusy) return;
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
                if (!string.IsNullOrWhiteSpace(error))
                    StatusMessage = error;
                foreach (var image in images)
                {
                    _aiCts.Token.ThrowIfCancellationRequested();
                    total++;
                    StatusMessage = $"Working. Reading picture {total}: {image.Name}.";
                    var read = await _ai.ReadImportImageAsync(image, DraftSkill, _aiCts.Token);
                    texts.Add(read.Success
                        ? $"[Picture {total}: {image.Name}]\n{read.Text.Trim()}"
                        : $"[{image.Name}: {read.Error}]");
                }
            }
            var combined = string.Join("\n\n", texts.Where(t => t.Length > 0));
            PasteText = string.IsNullOrWhiteSpace(PasteText)
                ? combined
                : PasteText.Trim() + "\n\n" + combined;
            StatusMessage = total == 0
                ? "No pictures could be read. See the notes in the paste box."
                : $"Read {total} picture(s) into the paste box. Check the text, then build a draft.";
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
    private void CancelAi()
    {
        _aiCts?.Cancel();
        StatusMessage = "Stopping.";
    }

    /// <summary>Puts a blank template in the editor for fully manual creation.</summary>
    [RelayCommand]
    private void NewManualTemplate()
    {
        var title = string.IsNullOrWhiteSpace(DraftTitle)
            ? "My manual paper"
            : DraftTitle.Trim();
        var template = new ExamPaper
        {
            Title = title,
            Source = "Created by hand in the Library. Edit before sharing.",
            Category = "Imported",
            Level = string.Empty,
            Tags = new List<string> { "manual" },
            Parts = new List<ExamPart>
            {
                new()
                {
                    Id = "R1",
                    Skill = DraftSkill,
                    Title = $"{DraftSkill} Part 1",
                    Topic = string.Empty,
                    TaskType = DraftSkill == "Writing" ? "Task 2 Opinion" : "Passage 1",
                    Material = "Paste your passage or task here.",
                    Instructions = DraftSkill == "Writing"
                        ? "Write at least 250 words."
                        : "Read the text, then answer the questions.",
                    AudioFile = string.Empty,
                    Minutes = DraftSkill == "Writing" ? 40 : 12,
                    PrepSeconds = 0,
                    Questions = DraftSkill is "Writing" or "Speaking"
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
                                    new() { Key = "C", Text = "Third option" }
                                },
                                CorrectKey = "A",
                                Explanation = "Why A is right, in one sentence."
                            }
                        }
                }
            }
        };
        DraftJson = JsonSerializer.Serialize(template,
            new JsonSerializerOptions { WriteIndented = true });
        StatusMessage = "Blank template ready. Edit the JSON, then press Save draft.";
    }

    /// <summary>Saves the edited JSON draft after validation. Never overwrites.</summary>
    [RelayCommand]
    private void SaveDraftJson()
    {
        if (string.IsNullOrWhiteSpace(DraftJson))
        {
            StatusMessage = "The draft box is empty. Make a template or paste JSON first.";
            return;
        }
        var result = _repository.ImportPaper(DraftJson);
        if (!result.Success)
        {
            StatusMessage = result.Error;
            return;
        }
        DraftJson = string.Empty;
        Load();
        _exam.Load();
        StatusMessage = $"Saved {result.Title}. It is ready in the list above.";
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
            Level = string.Empty,
            Tags = new List<string> { "imported" },
            Parts = new List<ExamPart>
            {
                new()
                {
                    Id = "P1",
                    Skill = cleanSkill,
                    Title = $"{cleanSkill} draft: {title}",
                    Topic = string.Empty,
                    TaskType = cleanSkill,
                    Material = text.Trim(),
                    Instructions = productive
                        ? "Answer in your own words. Add a teacher or AI check later."
                        : "Draft saved offline. Edit this paper and add questions.",
                    AudioFile = string.Empty,
                    Minutes = cleanSkill.Equals("Writing", StringComparison.OrdinalIgnoreCase) ? 40 : 12,
                    PrepSeconds = 0,
                    Questions = new List<ExamQuestion>()
                }
            }
        };
    }

    [RelayCommand]
    private void ExportShown()
    {
        var titles = Rows.Select(r => r.Title).ToList();
        if (titles.Count == 0)
        {
            StatusMessage = "Nothing matches the current filter.";
            return;
        }
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "IELTop-Export", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var result = _repository.ExportPapers(titles, folder);
        StatusMessage = result.Success
            ? $"Exported {result.PaperCount} paper(s) and {result.AudioCount} clip(s) to {result.Folder}."
            : result.Error;
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "IELTop-Export");
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            StatusMessage = "Could not open the export folder.";
        }
    }

    [RelayCommand]
    private async Task AiCheckPaperAsync(string? title)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(title)) return;
        var paper = _allPapers.FirstOrDefault(p =>
            string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
        if (paper is null)
        {
            StatusMessage = "That paper is gone. Press Reload.";
            return;
        }
        if (!_ai.IsAvailable)
        {
            StatusMessage = "Add a language model in Settings for AI paper checks.";
            return;
        }
        IsBusy = true;
        StatusMessage = $"AI is checking {paper.Title}.";
        try
        {
            var review = await _ai.ReviewPaperAsync(paper);
            if (!review.Success)
            {
                StatusMessage = review.Error;
                return;
            }
            var lines = new List<string> { $"Score {review.Score:0.0} of 10." };
            lines.AddRange(review.Strengths.Take(2).Select(s => "Good: " + s));
            lines.AddRange(review.Fixes.Take(2).Select(s => "Fix: " + s));
            StatusMessage = $"{paper.Title}: {string.Join(" ", lines)}";
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
}
