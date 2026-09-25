using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Services.Ai;
using IELTop.Services.Storage;

namespace IELTop.ViewModels;

/// <summary>
/// Settings for the optional language model. Works with any server that
/// speaks the OpenAI chat completions API, including local servers.
/// Includes a format check and a live connection test.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IIeltsAiService _ai;

    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private double _temperature = 0.3;
    [ObservableProperty] private int _maxTokens = 800;
    [ObservableProperty] private bool _useStreaming = true;
    [ObservableProperty] private bool _visionEnabled;
    [ObservableProperty] private string _statusMessage = "Not checked yet.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isValid = true;
    [ObservableProperty] private string _checkSummary = "Fill in the fields, then press Check.";

    public ObservableCollection<string> Problems { get; } = new();

    public SettingsViewModel(ISettingsStore store, IIeltsAiService ai)
    {
        _store = store;
        _ai = ai;

        var s = store.Current;
        BaseUrl = s.LlmBaseUrl;
        Model = s.LlmModel;
        ApiKey = s.LlmApiKey;
        Temperature = s.LlmTemperature;
        MaxTokens = s.LlmMaxTokens;
        UseStreaming = s.LlmUseStreaming;
        VisionEnabled = s.LlmVisionEnabled;

        RunChecks();
    }

    public bool ShowApiKeyHint => string.IsNullOrWhiteSpace(ApiKey);
    public bool HasProblems => Problems.Count > 0;

    partial void OnApiKeyChanged(string value) => OnPropertyChanged(nameof(ShowApiKeyHint));

    partial void OnBaseUrlChanged(string value) => RunChecks();
    partial void OnModelChanged(string value) => RunChecks();

    /// <summary>
    /// Format checks only, no network. Runs as the user types so mistakes show up early.
    /// </summary>
    private void RunChecks()
    {
        Problems.Clear();

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Model))
        {
            IsValid = true;
            CheckSummary = "No model configured. AI features stay off and the app still works.";
            OnPropertyChanged(nameof(HasProblems));
            return;
        }

        var result = LlmConfigValidator.Validate(BaseUrl, Model, ApiKey);
        foreach (var issue in result.Errors)
            Problems.Add($"Error, {issue.Field}: {issue.Message}");
        foreach (var issue in result.Warnings)
            Problems.Add($"Note, {issue.Field}: {issue.Message}");

        IsValid = result.IsValid;
        CheckSummary = result.IsValid
            ? "The settings look valid. Press Test connection to check the server."
            : $"Fix {result.Errors.Count()} problem(s) before the model will work.";
        OnPropertyChanged(nameof(HasProblems));
    }

    [RelayCommand]
    private void Save()
    {
        RunChecks();
        if (!IsValid)
        {
            StatusMessage = "Settings were not saved. Fix the errors above first.";
            return;
        }

        var s = _store.Current;
        s.LlmBaseUrl = BaseUrl.Trim();
        s.LlmModel = Model.Trim();
        s.LlmApiKey = ApiKey.Trim();
        s.LlmTemperature = Temperature;
        s.LlmMaxTokens = MaxTokens;
        s.LlmUseStreaming = UseStreaming;
        s.LlmVisionEnabled = VisionEnabled;
        _store.Save();
        StatusMessage = "Saved.";
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after a successful save so the shell can refresh its status line.</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        RunChecks();
        if (!IsValid)
        {
            StatusMessage = "Fix the errors above before testing.";
            return;
        }

        // Save first so the test uses exactly what the user sees.
        var s = _store.Current;
        s.LlmBaseUrl = BaseUrl.Trim();
        s.LlmModel = Model.Trim();
        s.LlmApiKey = ApiKey.Trim();
        s.LlmUseStreaming = UseStreaming;
        s.LlmVisionEnabled = VisionEnabled;
        _store.Save();

        IsBusy = true;
        StatusMessage = "Contacting the model server.";
        try
        {
            var result = await _ai.TestAsync();
            StatusMessage = result.Success
                ? $"Connected. The model replied: {Truncate(result.Text, 80)}"
                : result.Error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Truncate(string text, int max)
    {
        var clean = text.Replace('\n', ' ').Trim();
        return clean.Length <= max ? clean : clean[..max] + "...";
    }
}
