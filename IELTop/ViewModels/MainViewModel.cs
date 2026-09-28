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
        State = IsReady ? "Ready" : "Missing";
        FileDetail = BuildFileDetail();
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(FullPath));
    }

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
    [ObservableProperty] private string _lastBand = "No test yet";
    [ObservableProperty] private string _averageAccuracy = "0";
    [ObservableProperty] private string _modelsSummary = "0 of 0";
    [ObservableProperty] private string _llmSummary = "Not set";

    public ExamViewModel Exam { get; }
    public SettingsViewModel Settings { get; }
    public ResultsViewModel Results { get; }

    public ObservableCollection<ModelSlotViewModel> Models { get; } = new();

    public MainViewModel(
        IOnnxService onnx,
        IStatsService stats,
        ExamViewModel exam,
        SettingsViewModel settings,
        ResultsViewModel results)
    {
        _onnx = onnx;
        _stats = stats;
        Exam = exam;
        Settings = settings;
        Results = results;

        Exam.Load();
        BuildModels();
        RefreshStats();

        Settings.Saved += (_, _) => OnSettingsChanged();
    }

    /// <summary>
    /// After Settings change, refresh the dashboard and every AI gate so the
    /// buttons turn on or off right away. Nothing AI stays enabled without a model.
    /// </summary>
    private void OnSettingsChanged()
    {
        RefreshStats();
        Exam.RefreshAiState();
        OnPropertyChanged(nameof(LlmSummary));
    }

    public string ModelsFolder => OnnxModelRegistry.ModelsDir;

    [RelayCommand]
    private void RefreshStats()
    {
        var s = _stats.Build();
        WordCount = s.WordCount;
        WordsDue = s.WordsDueToday;
        AttemptCount = s.SpeakingAttempts;
        AverageAccuracy = $"{s.AverageAccuracy:0.#}%";
        ModelsSummary = $"{s.ModelsReady} of {s.ModelsTotal}";
        LlmSummary = s.LlmConfigured ? s.LlmModel : "Not set";

        try
        {
            using var db = new Data.AppDbContext();
            var list = db.ExamAttempts.OrderByDescending(x => x.CreatedAt).Take(20).ToList();
            ExamCount = db.ExamAttempts.Count();
            var last = list.FirstOrDefault();
            LastBand = last is null ? "No test yet" : $"{last.BandLow:0.0} to {last.BandHigh:0.0}";
        }
        catch
        {
            ExamCount = 0;
            LastBand = "No test yet";
        }

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
