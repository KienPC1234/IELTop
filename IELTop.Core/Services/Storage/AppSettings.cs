using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IELTop.Services.Storage;

/// <summary>
/// User settings for the optional OpenAI-compatible language model.
/// Empty until the user configures a server, so a fresh install stays offline.
/// The API key is encrypted at rest with DPAPI on Windows.
/// </summary>
public sealed class AppSettings
{
    public string LlmBaseUrl { get; set; } = string.Empty;
    public string LlmModel { get; set; } = string.Empty;
    public double LlmTemperature { get; set; } = 0.3;
    public int LlmMaxTokens { get; set; } = 16384;
    public bool LlmUseStreaming { get; set; } = true;

    /// <summary>When true, images can be sent to models that accept them.</summary>
    public bool LlmVisionEnabled { get; set; }

    /// <summary>Extra sampling control, 0 to 1. Sent as top_p. 1 means off.</summary>
    public double LlmTopP { get; set; } = 1.0;

    /// <summary>Per request timeout in seconds. 15 to 600.</summary>
    public int LlmTimeoutSeconds { get; set; } = 180;

    /// <summary>Custom examiner prompt. Empty means the built in prompt.</summary>
    public string LlmSystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// When true, offline ONNX models load when a feature starts and unload
    /// when it finishes. When false, they load lazily on first use and stay.
    /// </summary>
    public bool ModelAutoLoad { get; set; }

    /// <summary>
    /// When true (the default), a Speaking part records once and is submitted and
    /// scored on its own, with no transcript editing. Off keeps the transcript box
    /// for practice.
    /// </summary>
    public bool SpeakingAutoSubmit { get; set; } = true;

    /// <summary>
    /// Velopack update feed. Empty means auto update stays off. Point it at a
    /// release folder URL or a GitHub Releases URL.
    /// </summary>
    public string UpdateFeedUrl { get; set; } = string.Empty;

    /// <summary>When true, the app checks for updates quietly on startup.</summary>
    public bool UpdateCheckOnStartup { get; set; } = true;

    /// <summary>Encrypted key, base64. Never written as plain text.</summary>
    public string LlmApiKeyProtected { get; set; } = string.Empty;

    /// <summary>Exam text size: Normal or Large. Plain preference, not secret.</summary>
    public string UiTextSize { get; set; } = "Normal";

    /// <summary>Colour theme: System, Light, or Dark. System follows the OS.</summary>
    public string UiTheme { get; set; } = "System";

    /// <summary>Chosen speaker device id. Empty means the system default.</summary>
    public string AudioOutputDeviceId { get; set; } = string.Empty;

    /// <summary>Chosen microphone device id. Empty means the system default.</summary>
    public string AudioInputDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// When true, a running test opens full screen on its own, without the
    /// strict rules. The student can still leave full screen from the test.
    /// </summary>
    public bool FullscreenOnStart { get; set; }

    [JsonIgnore]
    public string LlmApiKey { get; set; } = string.Empty;
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    string FilePath { get; }
    string DataFolder { get; }
    void Save();
    void Reset();
}

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public AppSettings Current { get; private set; }

    public string FilePath => _path;

    public string DataFolder => Path.GetDirectoryName(_path) ?? string.Empty;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options)
                           ?? new AppSettings();
            settings.LlmApiKey = SecretProtector.Unprotect(settings.LlmApiKeyProtected);
            return settings;
        }
        catch (JsonException)
        {
            // A broken settings file should not stop the app from opening.
            return new AppSettings();
        }
    }

    public void Save()
    {
        Current.LlmApiKeyProtected = SecretProtector.Protect(Current.LlmApiKey);
        var json = JsonSerializer.Serialize(Current, Options);
        File.WriteAllText(_path, json);
    }

    /// <summary>Deletes the file and starts fresh. Never throws.</summary>
    public void Reset()    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch
        {
            // Keep going with defaults below.
        }
        Current = new AppSettings();
    }
}
