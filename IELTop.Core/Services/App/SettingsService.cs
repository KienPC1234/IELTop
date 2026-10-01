using System.Globalization;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Storage;
using IELTop.Services.Update;

namespace IELTop.Services.App;

/// <summary>One problem line shown under the language model fields.</summary>
public sealed record SettingsProblem(string Text, bool IsError);

/// <summary>The Settings screen state, plus the About and update cards.</summary>
public sealed class SettingsSnapshot
{
    public string BaseUrl { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public bool HasApiKey { get; init; }
    public double Temperature { get; init; }
    public int MaxTokens { get; init; }
    public double TopP { get; init; }
    public int TimeoutSeconds { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public bool UseStreaming { get; init; }
    public bool VisionEnabled { get; init; }
    public bool ModelAutoLoad { get; init; }
    public bool UpdateCheckOnStartup { get; init; }
    public string SelectedTextSize { get; init; } = "Normal";
    public bool FullscreenOnStart { get; init; }

    public IReadOnlyList<string> TextSizeOptions { get; init; } = new[] { "Normal", "Large" };
    /// <summary>Exam font scale the web UI applies: 1.0 normal, 1.15 large.</summary>
    public double FontScale { get; init; } = 1.0;
    /// <summary>Chosen speaker device id, empty for the system default.</summary>
    public string AudioOutputDeviceId { get; init; } = string.Empty;
    /// <summary>Chosen microphone device id, empty for the system default.</summary>
    public string AudioInputDeviceId { get; init; } = string.Empty;
    public IReadOnlyList<SettingsProblem> Problems { get; init; } = Array.Empty<SettingsProblem>();
    public bool IsValid { get; init; } = true;
    public string CheckSummary { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = "Not checked yet.";
    public string DebugDetails { get; init; } = string.Empty;
    public bool HasDebugDetails { get; init; }
    public bool IsBusy { get; init; }

    // About and update cards.
    public string AppVersion { get; init; } = "1.0";
    public string AppVersionLabel { get; init; } = string.Empty;
    public string DataFolder { get; init; } = string.Empty;
    public string AboutLine { get; init; } = string.Empty;
    public string AuthorLine { get; init; } = string.Empty;
    public string LicenseLine { get; init; } = string.Empty;
    public string RepoLine { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public string ModelModeSummary { get; init; } = string.Empty;
    public string TextSizeSummary { get; init; } = string.Empty;
    public string FullscreenSummary { get; init; } = string.Empty;
    public string UpdateStatus { get; init; } = string.Empty;
    public string InstallKindLabel { get; init; } = string.Empty;
    public string UpdateSourceLabel { get; init; } = string.Empty;
    public bool IsPackagedBuild { get; init; }
    public bool UpdateAvailable { get; init; }
    public bool UpdateDownloaded { get; init; }
}

/// <summary>
/// Settings for the optional language model, plus About and update info.
/// Nothing here is required: with no model set the app works fully offline.
/// Format checks run as fields change; reachability is a separate live test.
/// </summary>
public sealed class SettingsService
{
    /// <summary>Project home on GitHub, wired to the About card.</summary>
    public const string ProjectUrl = "https://github.com/KienPC1234/IELTop";

    private readonly ISettingsStore _store;
    private readonly IIeltsAiService _ai;
    private readonly IUpdateService _updates;

    // Draft values, applied to the store only on Save. Live preferences
    // (text size, full screen) write through right away.
    private string _baseUrl = string.Empty;
    private string _model = string.Empty;
    private string _apiKey = string.Empty;
    private double _temperature = 0.3;
    private int _maxTokens = 800;
    private double _topP = 1.0;
    private int _timeoutSeconds = 120;
    private string _systemPrompt = string.Empty;
    private bool _useStreaming = true;
    private bool _visionEnabled;
    private bool _modelAutoLoad;
    private bool _updateCheckOnStartup = true;
    private string _textSize = "Normal";
    private bool _fullscreenOnStart;
    private string _audioOutput = string.Empty;
    private string _audioInput = string.Empty;
    private bool _loading;

    private string _status = "Not checked yet.";
    private string _debug = string.Empty;
    private bool _busy;
    private string _updateStatus = string.Empty;
    private bool _updateAvailable;
    private bool _updateDownloaded;

    public SettingsService(ISettingsStore store, IIeltsAiService ai, IUpdateService updates)
    {
        _store = store;
        _ai = ai;
        _updates = updates;
        LoadFromStore();
    }

    private void LoadFromStore()
    {
        var s = _store.Current;
        _loading = true;
        _baseUrl = s.LlmBaseUrl;
        _model = s.LlmModel;
        _apiKey = s.LlmApiKey;
        _temperature = s.LlmTemperature;
        _maxTokens = s.LlmMaxTokens;
        _topP = s.LlmTopP <= 0 || s.LlmTopP > 1 ? 1.0 : s.LlmTopP;
        _timeoutSeconds = s.LlmTimeoutSeconds < 15 || s.LlmTimeoutSeconds > 300 ? 120 : s.LlmTimeoutSeconds;
        _systemPrompt = s.LlmSystemPrompt;
        _useStreaming = s.LlmUseStreaming;
        _visionEnabled = s.LlmVisionEnabled;
        _modelAutoLoad = s.ModelAutoLoad;
        _updateCheckOnStartup = s.UpdateCheckOnStartup;
        _textSize = s.UiTextSize == "Large" ? "Large" : "Normal";
        _fullscreenOnStart = s.FullscreenOnStart;
        _audioOutput = s.AudioOutputDeviceId ?? string.Empty;
        _audioInput = s.AudioInputDeviceId ?? string.Empty;
        _loading = false;
    }

    // ---- Field setters (draft only, except the live preferences) ----

    public SettingsSnapshot SetBaseUrl(string v) { _baseUrl = v ?? string.Empty; return Snapshot(); }
    public SettingsSnapshot SetModel(string v) { _model = v ?? string.Empty; return Snapshot(); }
    public SettingsSnapshot SetApiKey(string v) { _apiKey = v ?? string.Empty; return Snapshot(); }
    public SettingsSnapshot SetTemperature(double v) { _temperature = v; return Snapshot(); }
    public SettingsSnapshot SetMaxTokens(int v) { _maxTokens = v; return Snapshot(); }
    public SettingsSnapshot SetTopP(double v) { _topP = v; return Snapshot(); }
    public SettingsSnapshot SetTimeoutSeconds(int v) { _timeoutSeconds = v; return Snapshot(); }
    public SettingsSnapshot SetSystemPrompt(string v) { _systemPrompt = v ?? string.Empty; return Snapshot(); }
    public SettingsSnapshot SetUseStreaming(bool v) { _useStreaming = v; return Snapshot(); }
    public SettingsSnapshot SetVisionEnabled(bool v) { _visionEnabled = v; return Snapshot(); }
    public SettingsSnapshot SetModelAutoLoad(bool v) { _modelAutoLoad = v; return Snapshot(); }
    public SettingsSnapshot SetUpdateCheckOnStartup(bool v) { _updateCheckOnStartup = v; return Snapshot(); }

    /// <summary>Exam text size applies right away, without waiting for Save.</summary>
    public SettingsSnapshot SetTextSize(string v)
    {
        _textSize = v == "Large" ? "Large" : "Normal";
        PersistPreference(s => s.UiTextSize = _textSize);
        return Snapshot();
    }

    /// <summary>Full screen on start sticks without waiting for Save.</summary>
    public SettingsSnapshot SetFullscreenOnStart(bool v)
    {
        _fullscreenOnStart = v;
        PersistPreference(s => s.FullscreenOnStart = v);
        return Snapshot();
    }

    /// <summary>Speaker choice applies right away, used by the test and the exam.</summary>
    public SettingsSnapshot SetAudioOutput(string id)
    {
        _audioOutput = id ?? string.Empty;
        PersistPreference(s => s.AudioOutputDeviceId = _audioOutput);
        return Snapshot();
    }

    /// <summary>Microphone choice applies right away, used by the test and the exam.</summary>
    public SettingsSnapshot SetAudioInput(string id)
    {
        _audioInput = id ?? string.Empty;
        PersistPreference(s => s.AudioInputDeviceId = _audioInput);
        return Snapshot();
    }

    private void PersistPreference(Action<AppSettings> apply)
    {
        if (_loading) return;
        apply(_store.Current);
        try { _store.Save(); }
        catch (Exception) { /* a failed save must not break the toggle */ }
    }

    public SettingsSnapshot Save()
    {
        if (!Validation().IsValid)
        {
            _status = "Settings were not saved. Fix the errors above first.";
            return Snapshot();
        }
        var s = _store.Current;
        s.LlmBaseUrl = _baseUrl.Trim();
        s.LlmModel = _model.Trim();
        s.LlmApiKey = _apiKey.Trim();
        s.LlmTemperature = _temperature;
        s.LlmMaxTokens = _maxTokens;
        s.LlmTopP = _topP;
        s.LlmTimeoutSeconds = _timeoutSeconds;
        s.LlmSystemPrompt = _systemPrompt.Trim();
        s.LlmUseStreaming = _useStreaming;
        s.LlmVisionEnabled = _visionEnabled;
        s.ModelAutoLoad = _modelAutoLoad;
        s.UiTextSize = _textSize;
        s.FullscreenOnStart = _fullscreenOnStart;
        s.AudioOutputDeviceId = _audioOutput;
        s.AudioInputDeviceId = _audioInput;
        s.UpdateCheckOnStartup = _updateCheckOnStartup;
        _store.Save();
        _status = "Saved.";
        return Snapshot();
    }

    public SettingsSnapshot Reset()
    {
        _store.Reset();
        LoadFromStore();
        _status = "Settings were reset to defaults.";
        _debug = string.Empty;
        return Snapshot();
    }

    public SettingsSnapshot CompactDatabase()
    {
        _status = AppDbContext.CompactAndClean();
        return Snapshot();
    }

    /// <summary>Live test of the settings shown on screen. Nothing is saved.</summary>
    public async Task<SettingsSnapshot> TestConnectionAsync(CancellationToken ct)
    {
        var check = Validation();
        if (!check.IsValid)
        {
            _status = "Fix the errors above before testing.";
            return Snapshot();
        }

        // Apply the draft to the in memory settings so the test uses what the
        // user sees. This is never written to disk here.
        var s = _store.Current;
        s.LlmBaseUrl = _baseUrl.Trim();
        s.LlmModel = _model.Trim();
        s.LlmApiKey = _apiKey.Trim();
        s.LlmTemperature = _temperature;
        s.LlmMaxTokens = _maxTokens;
        s.LlmTopP = _topP;
        s.LlmTimeoutSeconds = _timeoutSeconds;
        s.LlmSystemPrompt = _systemPrompt.Trim();
        s.LlmUseStreaming = _useStreaming;
        s.LlmVisionEnabled = _visionEnabled;

        _busy = true;
        _status = "Contacting the model server.";
        try
        {
            var result = await _ai.TestAsync(ct);
            _status = result.Success
                ? $"Connected. The model replied: {Truncate(result.Text, 80)}. Press Save to keep these settings."
                : result.Error;
            _debug = BuildDebug(result);
        }
        finally
        {
            _busy = false;
        }
        return Snapshot();
    }

    public async Task<SettingsSnapshot> CheckForUpdatesAsync(CancellationToken ct)
    {
        _busy = true;
        _updateStatus = "Checking for updates.";
        try
        {
            var result = await _updates.CheckAsync(ct);
            _updateStatus = result.Message;
            _updateAvailable = result.Success && result.UpdateAvailable;
            _updateDownloaded = false;
        }
        finally
        {
            _busy = false;
        }
        return Snapshot();
    }

    public async Task<SettingsSnapshot> DownloadUpdateAsync(CancellationToken ct)
    {
        _busy = true;
        _updateStatus = "Downloading the update.";
        try
        {
            var result = await _updates.DownloadAsync(ct);
            _updateStatus = result.Message;
            _updateDownloaded = result.Success && result.UpdateAvailable;
        }
        finally
        {
            _busy = false;
        }
        return Snapshot();
    }

    public SettingsSnapshot ApplyUpdate()
    {
        if (!_updateDownloaded) return Snapshot();
        _updates.ApplyAndRestart();
        return Snapshot();
    }

    private LlmConfigCheck Validation()
    {
        var issues = new List<ConfigIssue>();
        if (!string.IsNullOrWhiteSpace(_baseUrl) || !string.IsNullOrWhiteSpace(_model))
        {
            var result = LlmConfigValidator.Validate(_baseUrl, _model, _apiKey);
            issues.AddRange(result.Issues);
        }
        if (_topP <= 0 || _topP > 1)
            issues.Add(new ConfigIssue("Sampling", "Top P must stay between 0 and 1.", true));
        if (_timeoutSeconds < 15 || _timeoutSeconds > 300)
            issues.Add(new ConfigIssue("Timeout", "Use 15 to 300 seconds.", true));

        bool valid = !issues.Any(i => i.IsError);
        return new LlmConfigCheck(valid, issues);
    }

    private string CheckSummary()
    {
        if (string.IsNullOrWhiteSpace(_baseUrl) && string.IsNullOrWhiteSpace(_model))
            return "No model configured. AI features stay off and the app still works.";
        var check = Validation();
        return check.IsValid
            ? "The settings look valid. Press Test connection to check the server."
            : $"Fix {check.Errors.Count()} problem(s) before the model will work.";
    }

    private string ModelModeSummary()
    {
        if (!_modelAutoLoad)
            return "Models load when first needed, then stay in memory until you unload them.";
        int ready = OnnxModelRegistry.Slots.Count(s => OnnxModelRegistry.IsComplete(s.Name));
        return ready == 0
            ? "Keep ready is on, but no model files are installed yet."
            : $"Keep ready is on. {ready} model(s) load when Grade with AI starts, then unload after.";
    }

    private string BuildDebug(LlmResult result)
    {
        var endpoint = string.IsNullOrWhiteSpace(result.Endpoint)
            ? $"{_baseUrl.Trim().TrimEnd('/')}/chat/completions"
            : result.Endpoint;
        var status = result.StatusCode == 0 ? "no response" : $"HTTP {result.StatusCode}";
        return $"Endpoint: {endpoint}\n" +
               $"Model: {_model.Trim()}\n" +
               $"API key: {(string.IsNullOrWhiteSpace(_apiKey) ? "empty" : "set")}\n" +
               $"Vision: {(_visionEnabled ? "on" : "off")}, streaming: {(_useStreaming ? "on" : "off")}\n" +
               $"Sampling: temperature {_temperature.ToString("0.0", CultureInfo.InvariantCulture)}, " +
               $"top_p {_topP.ToString("0.00", CultureInfo.InvariantCulture)}, " +
               $"max tokens {_maxTokens}, timeout {_timeoutSeconds}s\n" +
               $"System prompt: {(string.IsNullOrWhiteSpace(_systemPrompt) ? "built in" : "custom")}\n" +
               $"Result: {(result.Success ? "ok" : "failed")}, {status}, {result.ElapsedMs} ms\n" +
               $"Tested at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }

    private static string Truncate(string text, int max)
    {
        var clean = text.Replace('\n', ' ').Trim();
        return clean.Length <= max ? clean : clean[..max] + "...";
    }

    public SettingsSnapshot Snapshot()
    {
        var check = Validation();
        return new SettingsSnapshot
        {
            BaseUrl = _baseUrl,
            Model = _model,
            ApiKey = _apiKey,
            HasApiKey = !string.IsNullOrWhiteSpace(_apiKey),
            Temperature = _temperature,
            MaxTokens = _maxTokens,
            TopP = _topP,
            TimeoutSeconds = _timeoutSeconds,
            SystemPrompt = _systemPrompt,
            UseStreaming = _useStreaming,
            VisionEnabled = _visionEnabled,
            ModelAutoLoad = _modelAutoLoad,
            UpdateCheckOnStartup = _updateCheckOnStartup,
            SelectedTextSize = _textSize,
            FontScale = _textSize == "Large" ? 1.15 : 1.0,
            AudioOutputDeviceId = _audioOutput,
            AudioInputDeviceId = _audioInput,
            FullscreenOnStart = _fullscreenOnStart,
            Problems = check.Issues.Select(i => new SettingsProblem(
                $"{(i.IsError ? "Error" : "Note")}, {i.Field}: {i.Message}", i.IsError)).ToList(),
            IsValid = check.IsValid,
            CheckSummary = CheckSummary(),
            StatusMessage = _status,
            DebugDetails = _debug,
            HasDebugDetails = !string.IsNullOrWhiteSpace(_debug),
            IsBusy = _busy,
            AppVersion = AppVersion(),
            AppVersionLabel = $"IELTop version {_updates.CurrentVersion}",
            DataFolder = _store.DataFolder,
            AboutLine = $"IELTop {AppVersion()}. Offline IELTS mock test app.",
            AuthorLine = "Author: KienPC",
            LicenseLine = "License: AGPL-3.0. Source code is open, see the project page.",
            RepoLine = $"Project: {ProjectUrl}",
            ProjectUrl = ProjectUrl,
            ModelModeSummary = ModelModeSummary(),
            TextSizeSummary = _textSize == "Large"
                ? "Large makes test passages, questions and answers bigger, like the real test setting."
                : "Normal keeps the default test text size. Choose Large for a bigger reading view.",
            FullscreenSummary = _fullscreenOnStart
                ? "A test opens full screen on its own. You can still leave full screen from the test."
                : "A test opens in a normal window. Use the full screen button in the test if you want to focus.",
            UpdateStatus = _updateStatus,
            IsPackagedBuild = _updates.IsInstalled,
            InstallKindLabel = _updates.IsInstalled
                ? "Installed build. Auto update works here."
                : "Development build. Auto update works after a packaged install.",
            UpdateSourceLabel = $"Releases: {ProjectUrl}/releases",
            UpdateAvailable = _updateAvailable,
            UpdateDownloaded = _updateDownloaded,
        };
    }

    private static string AppVersion() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0";
}
