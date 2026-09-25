using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Models;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>
/// Import raw study files into practice content, and export the content back
/// out to share. Import and export both work without a language model: the
/// model only drafts questions when one is configured.
/// </summary>
public sealed partial class ContentViewModel : ObservableObject
{
    private readonly IContentImportService _import;
    private readonly IFileDialogService _dialogs;
    private readonly IPracticeRepository _content;
    private readonly IExamRepository _exams;
    private readonly IPaperExporter _exporter;

    [ObservableProperty] private string _statusMessage = "Pick a file to import, or export what you already have.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _importTitle = string.Empty;
    [ObservableProperty] private string _importPreview = string.Empty;
    [ObservableProperty] private bool _hasDraft;
    [ObservableProperty] private bool _draftUsedAi;
    [ObservableProperty] private ContentKind _draftKind = ContentKind.Reading;
    [ObservableProperty] private ExamPaper? _selectedPaper;

    private ImportDraft? _draft;

    public ObservableCollection<string> ImportedFiles { get; } = new();
    public ObservableCollection<ExamPaper> Papers { get; } = new();

    public ContentViewModel(
        IContentImportService import,
        IFileDialogService dialogs,
        IPracticeRepository content,
        IExamRepository exams,
        IPaperExporter exporter)
    {
        _import = import;
        _dialogs = dialogs;
        _content = content;
        _exams = exams;
        _exporter = exporter;
        ReloadPapers();
    }

    public bool AiAvailable => _import.AiAvailable;
    public bool ShowAiHint => !AiAvailable;
    public bool VisionAvailable => _import.VisionAvailable;
    public bool ShowVisionHint => !VisionAvailable;

    /// <summary>Re-reads AI availability after Settings changes.</summary>
    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(AiAvailable));
        OnPropertyChanged(nameof(ShowAiHint));
        OnPropertyChanged(nameof(VisionAvailable));
        OnPropertyChanged(nameof(ShowVisionHint));
    }

    public IReadOnlyList<ContentKind> Kinds { get; } =
        new[] { ContentKind.Reading, ContentKind.Listening, ContentKind.Writing };

    public string DraftLabel => DraftUsedAi
        ? "Draft prepared by the language model. Check it before saving."
        : "Draft prepared without a model. Fill in the questions yourself.";

    public bool HasPapers => Papers.Count > 0;

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var paths = _dialogs.PickFiles("Choose study files to import", multiSelect: true);
        if (paths.Count == 0) return;

        IsBusy = true;
        ImportedFiles.Clear();
        int ok = 0, failed = 0;
        try
        {
            foreach (var path in paths)
            {
                StatusMessage = $"Reading {Path.GetFileName(path)}.";
                var draft = await _import.ImportAsync(path, forcedKind: null);
                if (!draft.Success)
                {
                    failed++;
                    ImportedFiles.Add($"{Path.GetFileName(path)}: {draft.Error}");
                    continue;
                }

                if (SaveDraft(draft))
                {
                    ok++;
                    ImportedFiles.Add($"{Path.GetFileName(path)}: saved as {draft.Kind}.");
                }
                else
                {
                    failed++;
                    ImportedFiles.Add($"{Path.GetFileName(path)}: could not be saved.");
                }
            }

            StatusMessage = $"Imported {ok} file(s). {failed} skipped. Open the matching section to review.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviewFileAsync()
    {
        var paths = _dialogs.PickFiles("Choose a file to preview", multiSelect: false);
        if (paths.Count == 0) return;

        IsBusy = true;
        StatusMessage = "Reading the file.";
        try
        {
            var draft = await _import.ImportAsync(paths[0], forcedKind: null);
            if (!draft.Success)
            {
                StatusMessage = draft.Error;
                HasDraft = false;
                _draft = null;
                return;
            }

            _draft = draft;
            ImportTitle = draft.Title;
            DraftKind = draft.Kind;
            DraftUsedAi = draft.DraftedByAi;
            ImportPreview = BuildPreview(draft);
            HasDraft = true;
            StatusMessage = "Preview ready. Adjust the type if needed, then save.";
            OnPropertyChanged(nameof(DraftLabel));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-classifies the held draft when the user changes the type, without re-reading the file.</summary>
    partial void OnDraftKindChanged(ContentKind value)
    {
        if (_draft is null) return;
        _draft = Reclassify(_draft, value);
        ImportPreview = BuildPreview(_draft);
    }

    [RelayCommand]
    private void SavePreview()
    {
        if (_draft is null)
        {
            StatusMessage = "Preview a file first.";
            return;
        }

        StatusMessage = SaveDraft(_draft)
            ? $"Saved as {_draft.Kind}. Open that section to review."
            : "Could not save the draft.";
    }

    /// <summary>
    /// Moves the extracted text into the shape for the chosen kind, so changing
    /// the type after import still saves something usable.
    /// </summary>
    private static ImportDraft Reclassify(ImportDraft draft, ContentKind kind)
    {
        if (draft.Kind == kind) return draft;

        var text = draft.RawText;
        var title = draft.Title;
        return kind switch
        {
            ContentKind.Reading => draft with
            {
                Kind = kind,
                Reading = new ReadingPassage { Id = MakeId("R", title), Title = title, Source = "Imported by the user", Body = text },
                Listening = null, Writing = null
            },
            ContentKind.Listening => draft with
            {
                Kind = kind,
                Listening = new ListeningItem { Id = MakeId("L", title), Title = title, Source = "Imported by the user", Transcript = text },
                Reading = null, Writing = null
            },
            ContentKind.Writing => draft with
            {
                Kind = kind,
                Writing = new WritingTask { Id = MakeId("W", title), Title = title, Source = "Imported by the user", Prompt = text },
                Reading = null, Listening = null
            },
            _ => draft with
            {
                Kind = ContentKind.Reading,
                Reading = new ReadingPassage { Id = MakeId("R", title), Title = title, Source = "Imported by the user", Body = text },
                Listening = null, Writing = null
            }
        };
    }

    private static string MakeId(string prefix, string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 40) slug = slug[..40];
        return string.IsNullOrWhiteSpace(slug) ? $"{prefix}-imported" : $"{prefix}-{slug}";
    }

    [RelayCommand]
    private void ExportSelectedPaper()
    {
        if (SelectedPaper is null)
        {
            StatusMessage = "Pick a paper to export.";
            return;
        }
        var folder = _dialogs.PickFolder("Choose where to export");
        if (string.IsNullOrEmpty(folder)) return;

        _ = ExportPapersAsync(new[] { SelectedPaper }, folder);
    }

    [RelayCommand]
    private void ExportAllPapers()
    {
        if (Papers.Count == 0)
        {
            StatusMessage = "No mock papers to export.";
            return;
        }
        var folder = _dialogs.PickFolder("Choose where to export");
        if (string.IsNullOrEmpty(folder)) return;

        _ = ExportPapersAsync(Papers.ToList(), folder);
    }

    [RelayCommand]
    private void ExportAllPractice()
    {
        var folder = _dialogs.PickFolder("Choose where to export");
        if (string.IsNullOrEmpty(folder)) return;

        _ = ExportPracticeAsync(folder);
    }

    private async Task ExportPapersAsync(IReadOnlyList<ExamPaper> papers, string folder)
    {
        IsBusy = true;
        StatusMessage = "Exporting papers.";
        try
        {
            var result = await _exporter.ExportPapersAsync(papers, folder, ExportFormat.Json);
            StatusMessage = result.Success
                ? $"Exported {result.FileCount} paper(s) to {result.Folder}."
                : result.Error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportPracticeAsync(string folder)
    {
        IsBusy = true;
        StatusMessage = "Exporting practice content.";
        try
        {
            var reading = _content.LoadReading();
            var listening = _content.LoadListening();
            var writing = _content.LoadWriting();

            int total = 0;
            if (reading.Count > 0)
                total += (await _exporter.ExportReadingAsync(reading, folder, ExportFormat.Json)).FileCount;
            if (listening.Count > 0)
                total += (await _exporter.ExportListeningAsync(listening, folder, ExportFormat.Json)).FileCount;
            if (writing.Count > 0)
                total += (await _exporter.ExportWritingAsync(writing, folder, ExportFormat.Json)).FileCount;

            StatusMessage = total > 0
                ? $"Exported {total} item(s) to {folder}."
                : "There is no practice content to export yet.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ReloadPapers()
    {
        Papers.Clear();
        foreach (var paper in _exams.LoadPapers())
            Papers.Add(paper);
        SelectedPaper = Papers.FirstOrDefault();
        OnPropertyChanged(nameof(HasPapers));
    }

    /// <summary>Writes the draft into the right Assets folder as JSON.</summary>
    private bool SaveDraft(ImportDraft draft)
    {
        try
        {
            switch (draft.Kind)
            {
                case ContentKind.Reading when draft.Reading is not null:
                    WriteJson(_content.ReadingDir, draft.Reading.Id, draft.Reading);
                    return true;
                case ContentKind.Listening when draft.Listening is not null:
                    WriteJson(_content.ListeningDir, draft.Listening.Id, draft.Listening);
                    return true;
                case ContentKind.Writing when draft.Writing is not null:
                    WriteJson(_content.WritingDir, draft.Writing.Id, draft.Writing);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void WriteJson<T>(string folder, string id, T item)
    {
        Directory.CreateDirectory(folder);
        var safeId = string.IsNullOrWhiteSpace(id) ? "imported" : id;
        var path = Path.Combine(folder, safeId + ".json");
        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string BuildPreview(ImportDraft draft)
    {
        var text = draft.Reading?.Body
                   ?? draft.Listening?.Transcript
                   ?? draft.Writing?.Prompt
                   ?? draft.RawText;

        if (text.Length > 1500) text = text[..1500] + "...";
        return text;
    }
}
