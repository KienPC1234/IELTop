using System.IO;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;

namespace IELTop.Services.Audio;

/// <summary>
/// Reads a transcript aloud with the built-in Windows voice and saves the
/// result as a wav file, so every Listening part always has audio even when
/// no clip was shipped with the paper. Nothing needs the network.
/// </summary>
public interface ITtsService
{
    bool IsAvailable { get; }
    string VoiceName { get; }
    Task<string> SpeakToFileAsync(string text, CancellationToken ct = default);
}

public sealed class WindowsTtsService : ITtsService
{
    public string VoiceName => "Windows voice";

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "tts");

    public bool IsAvailable
    {
        get
        {
            try
            {
                using var voice = new SpeechSynthesizer();
                return voice.GetInstalledVoices().Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<string> SpeakToFileAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(string.Empty);

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
        var path = Path.Combine(CacheDir, $"tts_{hash}.wav");
        if (File.Exists(path))
            return Task.FromResult(path);

        // SpeechSynthesizer needs a single threaded apartment, so synthesis
        // runs on its own STA thread instead of a pool thread.
        var done = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                using var voice = new SpeechSynthesizer();
                voice.Rate = -1;
                voice.SetOutputToWaveFile(path);
                voice.Speak(text);
                voice.SetOutputToNull();
                done.TrySetResult(File.Exists(path) ? path : string.Empty);
            }
            catch (Exception)
            {
                done.TrySetResult(string.Empty);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        using var registration = ct.Register(() => done.TrySetResult(string.Empty));
        return done.Task;
    }
}
