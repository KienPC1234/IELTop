using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IELTop.Services.Ai;
using IELTop.Services.Storage;
using IELTop.Services.Update;

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
    private readonly IUpdateService _updates;

    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private double _temperature = 0.3;
    [ObservableProperty] private int _maxTokens = 800;
    [ObservableProperty] private double _topP = 1.0;
    [ObservableProperty] private int _timeoutSeconds = 120;
    [ObservableProperty] private string _systemPrompt = string.Empty;
    [ObservableProperty] private bool _useStreaming = true;
    [ObservableProperty] private bool _visionEnabled;
    [ObservableProperty] private bool _modelAutoLoad;
    [ObservableProperty] private string _updateFeedUrl = string.Empty;
    [ObservableProperty] private bool _updateCheckOnStartup = true;
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private string _selectedTextSize = "Normal";
    [ObservableProperty] private bool _fullscreenOnStart;
    [ObservableProperty] private string _statusMessage = "Not checked yet.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isValid = true;
    [ObservableProperty] private string _checkSummary = "Fill in the fields, then press Check.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDebugDetails))]
    private string _debugDetails = string.Empty;

    public ObservableCollection<string> Problems { get; } = new();

    public IReadOnlyList<string> TextSizeOptions { get; } = new[] { "Normal", "Large" };

    public SettingsViewModel(ISettingsStore store, IIeltsAiService ai, IUpdateService updates)
    {
        _store = store;
        _ai = ai;
        _updates = updates;

        var s = store.Current;
        BaseUrl = s.LlmBaseUrl;
        Model = s.LlmModel;
        ApiKey = s.LlmApiKey;
        Temperature = s.LlmTemperature;
        MaxTokens = s.LlmMaxTokens;
        TopP = s.LlmTopP <= 0 || s.LlmTopP > 1 ? 1.0 : s.LlmTopP;
        TimeoutSeconds = s.LlmTimeoutSeconds < 15 || s.LlmTimeoutSeconds > 300 ? 120 : s.LlmTimeoutSeconds;
        SystemPrompt = s.LlmSystemPrompt;
        UseStreaming = s.LlmUseStreaming;
        VisionEnabled = s.LlmVisionEnabled;
        ModelAutoLoad = s.ModelAutoLoad;
        SelectedTextSize = s.UiTextSize == "Large" ? "Large" : "Normal";
        FullscreenOnStart = s.FullscreenOnStart;
        UpdateFeedUrl = s.UpdateFeedUrl;
        UpdateCheckOnStartup = s.UpdateCheckOnStartup;

        RunChecks();
    }

    /// <summary>Reloads every field from disk, for the Reset button.</summary>
    public void Reload()
    {
        var s = _store.Current;
        BaseUrl = s.LlmBaseUrl;
        Model = s.LlmModel;
        ApiKey = s.LlmApiKey;
        Temperature = s.LlmTemperature;
        MaxTokens = s.LlmMaxTokens;
        TopP = s.LlmTopP <= 0 || s.LlmTopP > 1 ? 1.0 : s.LlmTopP;
        TimeoutSeconds = s.LlmTimeoutSeconds < 15 || s.LlmTimeoutSeconds > 300 ? 120 : s.LlmTimeoutSeconds;
        SystemPrompt = s.LlmSystemPrompt;
        UseStreaming = s.LlmUseStreaming;
        VisionEnabled = s.LlmVisionEnabled;
        ModelAutoLoad = s.ModelAutoLoad;
        SelectedTextSize = s.UiTextSize == "Large" ? "Large" : "Normal";
        FullscreenOnStart = s.FullscreenOnStart;
        UpdateFeedUrl = s.UpdateFeedUrl;
        UpdateCheckOnStartup = s.UpdateCheckOnStartup;
        StatusMessage = "Settings reloaded from disk.";
        RunChecks();
    }

    public bool ShowApiKeyHint => string.IsNullOrWhiteSpace(ApiKey);
    public bool HasProblems => Problems.Count > 0;

    public string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";

    public string DataFolder => _store.DataFolder;

    /// <summary>One line about how offline models load, for the Settings card.</summary>
    public string ModelModeSummary
    {
        get
        {
            if (!ModelAutoLoad)
                return "Models load when first needed, then stay in memory until you unload them.";
            int ready = OnnxModelRegistry.Slots.Count(s => OnnxModelRegistry.IsComplete(s.Name));
            return ready == 0
                ? "Keep ready is on, but no model files are installed yet."
                : $"Keep ready is on. {ready} model(s) load when Grade with AI starts, then unload after.";
        }
    }

    partial void OnApiKeyChanged(string value) => OnPropertyChanged(nameof(ShowApiKeyHint));

    partial void OnModelAutoLoadChanged(bool value) => OnPropertyChanged(nameof(ModelModeSummary));

        /// <summary>Writes the full screen choice to settings, so it sticks even
        /// if the user does not press Save on the Settings page.</summary>
        partial void OnFullscreenOnStartChanged(bool value)
        {
            OnPropertyChanged(nameof(FullscreenSummary));
            PersistFullscreenPreference();
        }

        private void PersistFullscreenPreference()
        {
            var s = _store.Current;
            if (s.FullscreenOnStart == FullscreenOnStart) return;
            s.FullscreenOnStart = FullscreenOnStart;
            try
            {
                _store.Save();
            }
            catch (Exception)
            {
                // A failed save must not break the toggle in the current session.
            }
        }

    /// <summary>Plain line about what the full screen start option does.</summary>
    public string FullscreenSummary => FullscreenOnStart
        ? "A test opens full screen on its own. You can still leave full screen from the test."
        : "A test opens in a normal window. Use the full screen button in the test if you want to focus.";

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

        if (TopP <= 0 || TopP > 1)
            Problems.Add("Error, Sampling: Top P must stay between 0 and 1.");
        if (TimeoutSeconds < 15 || TimeoutSeconds > 300)
            Problems.Add("Error, Timeout: use 15 to 300 seconds.");

        IsValid = result.IsValid && TopP > 0 && TopP <= 1
            && TimeoutSeconds >= 15 && TimeoutSeconds <= 300;
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
        s.LlmTopP = TopP;
        s.LlmTimeoutSeconds = TimeoutSeconds;
        s.LlmSystemPrompt = SystemPrompt.Trim();
        s.LlmUseStreaming = UseStreaming;
        s.LlmVisionEnabled = VisionEnabled;
        s.ModelAutoLoad = ModelAutoLoad;
        s.UiTextSize = SelectedTextSize;
        s.FullscreenOnStart = FullscreenOnStart;
        s.UpdateFeedUrl = UpdateFeedUrl.Trim();
        s.UpdateCheckOnStartup = UpdateCheckOnStartup;
        _store.Save();
        StatusMessage = "Saved.";
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after a successful save so the shell can refresh its status line.</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DataFolder,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            StatusMessage = "Could not open the data folder.";
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        _store.Reset();
        Reload();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CompactDatabase()
    {
        StatusMessage = Data.AppDbContext.CompactAndClean();
    }

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
        s.LlmTemperature = Temperature;
        s.LlmMaxTokens = MaxTokens;
        s.LlmTopP = TopP;
        s.LlmTimeoutSeconds = TimeoutSeconds;
        s.LlmSystemPrompt = SystemPrompt.Trim();
        s.LlmUseStreaming = UseStreaming;
        s.LlmVisionEnabled = VisionEnabled;
        s.ModelAutoLoad = ModelAutoLoad;
        s.UiTextSize = SelectedTextSize;
        s.FullscreenOnStart = FullscreenOnStart;
        s.UpdateFeedUrl = UpdateFeedUrl.Trim();
        s.UpdateCheckOnStartup = UpdateCheckOnStartup;
        _store.Save();

        IsBusy = true;
        StatusMessage = "Contacting the model server.";
        try
        {
            var result = await _ai.TestAsync();
            StatusMessage = result.Success
                ? $"Connected. The model replied: {Truncate(result.Text, 80)}"
                : result.Error;
            DebugDetails = BuildDebugDetails(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool HasDebugDetails => !string.IsNullOrWhiteSpace(DebugDetails);

    /// <summary>
    /// Technical details of the last connection test. The API key value is
    /// never included, only whether one was sent.
    /// </summary>
    private string BuildDebugDetails(LlmResult result)
    {
        var endpoint = string.IsNullOrWhiteSpace(result.Endpoint)
            ? $"{BaseUrl.Trim().TrimEnd('/')}/chat/completions"
            : result.Endpoint;
        var status = result.StatusCode == 0 ? "no response" : $"HTTP {result.StatusCode}";
        return $"Endpoint: {endpoint}\n" +
               $"Model: {Model.Trim()}\n" +
               $"API key: {(string.IsNullOrWhiteSpace(ApiKey) ? "empty" : "set")}\n" +
               $"Vision: {(VisionEnabled ? "on" : "off")}, streaming: {(UseStreaming ? "on" : "off")}\n" +
               $"Sampling: temperature {Temperature:0.0}, top_p {TopP:0.00}, max tokens {MaxTokens}, timeout {TimeoutSeconds}s\n" +
               $"System prompt: {(string.IsNullOrWhiteSpace(SystemPrompt) ? "built in" : "custom")}\n" +
               $"Result: {(result.Success ? "ok" : "failed")}, {status}, {result.ElapsedMs} ms\n" +
               $"Tested at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }

    private static string Truncate(string text, int max)
    {
        var clean = text.Replace('\n', ' ').Trim();
        return clean.Length <= max ? clean : clean[..max] + "...";
    }

    // ---- Auto update (Velopack) ----

    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _updateDownloaded;

    public string AppVersionLabel => $"IELTop version {_updates.CurrentVersion}";
    public bool IsPackagedBuild => _updates.IsInstalled;
    public string InstallKindLabel => _updates.IsInstalled
        ? "Installed build. Auto update works here."
        : "Development build. Auto update works after a Velopack install.";

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsBusy = true;
        UpdateStatus = "Checking for updates.";
        try
        {
            // Save the feed first so the check uses what the user sees.
            SaveCurrentToStore();
            var result = await _updates.CheckAsync();
            UpdateStatus = result.Message;
            UpdateAvailable = result.Success && result.UpdateAvailable;
            UpdateDownloaded = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        IsBusy = true;
        UpdateStatus = "Downloading the update.";
        try
        {
            var result = await _updates.DownloadAsync();
            UpdateStatus = result.Message;
            UpdateDownloaded = result.Success && result.UpdateAvailable;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RestartToUpdate()
    {
        if (!UpdateDownloaded) return;
        _updates.ApplyAndRestart();
    }

    private void SaveCurrentToStore()
    {
        var s = _store.Current;
        s.UpdateFeedUrl = UpdateFeedUrl.Trim();
        s.UpdateCheckOnStartup = UpdateCheckOnStartup;
        _store.Save();
    }
}
