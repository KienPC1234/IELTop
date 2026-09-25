using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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

    public string Name { get; }
    public string Skill { get; }
    public string Purpose { get; }
    public string License { get; }
    public string Origin { get; }
    public string FileName { get; }

    public bool IsReady => OnnxModelRegistry.IsComplete(Name);

    public ModelSlotViewModel(OnnxModelSlot slot, IOnnxService onnx)
    {
        _onnx = onnx;
        Name = slot.Name;
        Skill = slot.Skill;
        Purpose = slot.Purpose;
        License = slot.License;
        Origin = slot.Source;
        FileName = slot.FileName;
        Refresh();
    }

    public void Refresh()
    {
        State = IsReady ? "Ready" : "Missing";
        OnPropertyChanged(nameof(IsReady));
    }

    [RelayCommand]
    private void Load()
    {
        if (_onnx.TryLoad(Name, out var error))
            Message = "Loaded.";
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
    [ObservableProperty] private string _averageAccuracy = "0";
    [ObservableProperty] private string _modelsSummary = "0 of 0";
    [ObservableProperty] private string _llmSummary = "Not set";

    public SpeakingViewModel Speaking { get; }
    public ExamViewModel Exam { get; }
    public VocabularyViewModel Vocabulary { get; }
    public WritingViewModel Writing { get; }
    public ReadingViewModel Reading { get; }
    public ListeningViewModel Listening { get; }
    public SettingsViewModel Settings { get; }
    public ContentViewModel Content { get; }

    public ObservableCollection<ModelSlotViewModel> Models { get; } = new();

    public MainViewModel(
        IOnnxService onnx,
        IStatsService stats,
        SpeakingViewModel speaking,
        ExamViewModel exam,
        VocabularyViewModel vocabulary,
        WritingViewModel writing,
        ReadingViewModel reading,
        ListeningViewModel listening,
        SettingsViewModel settings,
        ContentViewModel content)
    {
        _onnx = onnx;
        _stats = stats;
        Speaking = speaking;
        Exam = exam;
        Vocabulary = vocabulary;
        Writing = writing;
        Reading = reading;
        Listening = listening;
        Settings = settings;
        Content = content;

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
        Vocabulary.RefreshAiState();
        Writing.RefreshAiState();
        Speaking.RefreshAiState();
        Content.RefreshAiState();
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
