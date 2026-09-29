using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Services.Ai;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>One model row on the Models page.</summary>
public sealed partial class ModelSlotViewModel : ObservableObject
{
    private readonly IOnnxService _onnx;

    [ObservableProperty] private string _state = "Missing";
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _fileDetail = string.Empty;

    public string Name { get; }
    public string Skill { get; }
    public string Purpose { get; }
    public string License { get; }
    public string Origin { get; }
    public string FileName { get; }
    public string? ExtraFileName { get; }

    public bool IsReady => OnnxModelRegistry.IsComplete(Name);

    public string FullPath => OnnxModelRegistry.PathOf(Name);

    public ModelSlotViewModel(OnnxModelSlot slot, IOnnxService onnx)
    {
        _onnx = onnx;
        Name = slot.Name;
        Skill = slot.Skill;
        Purpose = slot.Purpose;
        License = slot.License;
        Origin = slot.Source;
        FileName = slot.FileName;
        ExtraFileName = slot.ExtraFile;
        Refresh();
    }

    public void Refresh()
    {
        State = IsReady ? (_onnx.IsLoaded(Name) ? "In memory" : "Ready") : "Missing";
        FileDetail = BuildFileDetail();
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(IsLoaded));
    }

    public bool IsLoaded => _onnx.IsLoaded(Name);

    /// <summary>
    /// Debug line for release and publish checks: exact file state on disk,
    /// with sizes, so a missing model is easy to tell apart from a broken one.
    /// </summary>
    private string BuildFileDetail()
    {
        var main = DescribeFile(OnnxModelRegistry.PathOf(Name));
        if (string.IsNullOrEmpty(ExtraFileName))
            return main;
        return $"{main} | companion {ExtraFileName}: {DescribeFile(OnnxModelRegistry.PathOfExtra(Name))}";
    }

    private static string DescribeFile(string path)
    {
        if (!File.Exists(path))
            return "missing";
        var mb = new FileInfo(path).Length / 1048576.0;
        return $"{mb:0.0} MB present";
    }

    [RelayCommand]
    private void Load()
    {
        if (_onnx.TryLoad(Name, out var error))
            Message = $"Loaded. {BuildFileDetail()}";
        else
            Message = error;
        Refresh();
    }

    [RelayCommand]
    private void Unload()
    {
        _onnx.Unload(Name);
        Message = "Unloaded to free memory.";
        Refresh();
    }

    [RelayCommand]
    private void DeleteModel()
    {
        if (!IsReady)
        {
            Message = "Nothing to delete. The file is not installed.";
            return;
        }
        Message = $"File kept at {FullPath}. Delete it by hand to save disk space.";
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IOnnxService _onnx;
    private readonly IStatsService _stats;

    [ObservableProperty] private string _currentPage = "Overview";

    // Dashboard numbers
    [ObservableProperty] private int _wordCount;
    [ObservableProperty] private int _wordsDue;
    [ObservableProperty] private int _attemptCount;
    [ObservableProperty] private int _examCount;
    [ObservableProperty] private int _papersCount;
    [ObservableProperty] private string _lastBand = "No test yet";
    [ObservableProperty] private string _averageAccuracy = "0";
    [ObservableProperty] private string _modelsSummary = "0 of 0";
    [ObservableProperty] private string _loadedMemory = "Models in memory: 0 MB";
    [ObservableProperty] private string _llmSummary = "Not set";
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ExamViewModel Exam { get; }
    public LibraryViewModel Library { get; }
    public EditorViewModel Editor { get; }
    public SettingsViewModel Settings { get; }
    public ResultsViewModel Results { get; }
    public ServersViewModel Servers { get; }

    public ObservableCollection<ModelSlotViewModel> Models { get; } = new();

    public MainViewModel(
        IOnnxService onnx,
        IStatsService stats,
        ExamViewModel exam,
        LibraryViewModel library,
        EditorViewModel editor,
        SettingsViewModel settings,
        ResultsViewModel results,
        ServersViewModel servers)
    {
        _onnx = onnx;
        _stats = stats;
        Exam = exam;
        Library = library;
        Editor = editor;
        Settings = settings;
        Results = results;
        Servers = servers;
        Library.NavigateTo = page => CurrentPage = page;
        Library.Editor = editor;
        Editor.NavigateTo = page => CurrentPage = page;
        Editor.PapersChanged = RefreshPapers;
        Exam.PapersChanged = () => Library.LoadCommand.Execute(null);

        Exam.Load();
        BuildModels();
        RefreshStats();
        Exam.FontScale = Settings.SelectedTextSize == "Large" ? 1.15 : 1.0;

        Settings.Saved += (_, _) => OnSettingsChanged();
        Exam.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExamViewModel.IsRunning))
                OnPropertyChanged(nameof(IsExamRunning));
        };

        // A quiet update check on startup, off by default in the store only
        // when the user says so, and never blocking the first screen.
        if (Settings.UpdateCheckOnStartup)
            _ = Settings.CheckForUpdatesCommand.ExecuteAsync(null);
    }

    /// <summary>True while the Mock Test page runs a test. Pins the exam bars.</summary>
    public bool IsExamRunning => CurrentPage == "Exam" && Exam.IsRunning;

    /// <summary>
    /// Lists go stale while the user is elsewhere: a downloaded paper, a
    /// finished test, or new stats. Refresh the page being opened.
    /// </summary>
    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsExamRunning));
        if (value == "Exam")
            Exam.Load();
        else if (value == "Library")
            Library.LoadCommand.Execute(null);
        else if (value == "Results")
            Results.LoadCommand.Execute(null);
        else if (value == "Overview")
            RefreshStatsCommand.Execute(null);
    }

    /// <summary>Reloads every paper list after a save or delete.</summary>
    private void RefreshPapers()
    {
        Exam.Load();
        Library.LoadCommand.Execute(null);
        PapersCount = Exam.Papers.Count;
    }

    /// <summary>
    /// After Settings change, refresh the dashboard and every AI gate so the
    /// buttons turn on or off right away. Nothing AI stays enabled without a model.
    /// </summary>
    private void OnSettingsChanged()
    {
        RefreshStats();
        Exam.RefreshAiState();
        Library.RefreshAiState();
        Editor.RefreshAiState();
        Exam.FontScale = Settings.SelectedTextSize == "Large" ? 1.15 : 1.0;
        OnPropertyChanged(nameof(LlmSummary));
    }

    public string ModelsFolder => OnnxModelRegistry.ModelsDir;

    [RelayCommand]
    private void UnloadAllModels()
    {
        _onnx.UnloadAll();
        foreach (var model in Models)
            model.Refresh();
        LoadedMemory = $"Models in memory: 0 MB.";
        StatusMessage = "All models were unloaded.";
    }

    [RelayCommand]
    private void GoExam() => CurrentPage = "Exam";

    [RelayCommand]
    private void GoLibrary() => CurrentPage = "Library";

    [RelayCommand]
    private void GoServers() => CurrentPage = "Servers";

    [RelayCommand]
    private void RefreshStats()
    {
        var s = _stats.Build();
        WordCount = s.WordCount;
        WordsDue = s.WordsDueToday;
        AttemptCount = s.SpeakingAttempts;
        AverageAccuracy = $"{s.AverageAccuracy:0.#}%";
        ModelsSummary = $"{s.ModelsReady} of {s.ModelsTotal}";
        LoadedMemory = $"Models in memory: {_onnx.LoadedBytes() / 1048576} MB. Unload a model to free RAM.";
        LlmSummary = s.LlmConfigured ? s.LlmModel : "Not set";
        PapersCount = Exam.Papers.Count;
        ExamCount = s.ExamAttempts;
        LastBand = s.LastBandLabel;

        foreach (var model in Models)
            model.Refresh();
    }

    private void BuildModels()
    {
        Models.Clear();
        foreach (var slot in OnnxModelRegistry.Slots)
            Models.Add(new ModelSlotViewModel(slot, _onnx));
    }
}
