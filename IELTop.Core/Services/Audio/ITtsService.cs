namespace IELTop.Services.Audio;

/// <summary>
/// Reads a transcript aloud, fully offline. Piper is the neural voice; the
/// Windows voice is a Windows only fallback that lives in that host. Core
/// depends only on this contract so the same logic runs everywhere.
/// </summary>
public interface ITtsService
{
    bool IsAvailable { get; }
    string VoiceName { get; }
    Task<string> SpeakToFileAsync(string text, CancellationToken ct = default);
}
