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
    public int LlmMaxTokens { get; set; } = 800;
    public bool LlmUseStreaming { get; set; } = true;

    /// <summary>When true, images can be sent to models that accept them.</summary>
    public bool LlmVisionEnabled { get; set; }

    /// <summary>Encrypted key, base64. Never written as plain text.</summary>
    public string LlmApiKeyProtected { get; set; } = string.Empty;

    [JsonIgnore]
    public string LlmApiKey { get; set; } = string.Empty;
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    void Save();
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
}
