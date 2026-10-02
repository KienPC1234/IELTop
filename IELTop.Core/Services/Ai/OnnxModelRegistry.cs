namespace IELTop.Services.Ai;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// One ONNX model slot. Drop the file into Assets/Models and it is picked up here.
/// Source and License are recorded so the list stays honest about where each model comes from.
/// </summary>
public sealed record OnnxModelSlot(
    string Name,
    string FileName,
    string Skill,
    string Purpose,
    string License,
    string Source,
    bool Required = false,
    string? ExtraFile = null);

/// <summary>
/// The offline models this app supports. MDD drives pronunciation error
/// detection and STT transcribes speaking audio for AI assessment.
/// All run on CPU. Large files are never committed to git.
/// </summary>
public static class OnnxModelRegistry
{
    public static IReadOnlyList<OnnxModelSlot> Slots { get; } = new List<OnnxModelSlot>
    {
        new("mdd-wav2vec2-base", "mdd-wav2vec2-base-int8.onnx",
            "Speaking",
            "Recognize spoken phonemes so the app can find pronunciation errors",
            "Apache-2.0",
            "bobboyms/wav2vec2-base-en-phoneme-ctc-41h, exported by tools/speaking-mdd",
            ExtraFile: "mdd-labels.json"),
        new("stt-whisper-tiny-en", "stt-whisper-tiny-en-encoder-int8.onnx",
            "Speaking",
            "Transcribe recorded speech so AI marking can read what was said",
            "MIT",
            "openai/whisper-tiny.en, exported by tools/speech-stt",
            ExtraFile: "stt-whisper-tiny-en-decoder-int8.onnx"),
    };

    public static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Models");

    public static string PathOf(string name)
    {
        var slot = Slots.FirstOrDefault(s => s.Name == name);
        return slot is null ? string.Empty : Path.Combine(ModelsDir, slot.FileName);
    }

    public static string PathOfExtra(string name)
    {
        var slot = Slots.FirstOrDefault(s => s.Name == name);
        if (slot?.ExtraFile is null) return string.Empty;
        return Path.Combine(ModelsDir, slot.ExtraFile);
    }

    public static bool IsComplete(string name)
    {
        var slot = Slots.FirstOrDefault(s => s.Name == name);
        if (slot is null) return false;
        if (!File.Exists(PathOf(name))) return false;
        return slot.ExtraFile is null || File.Exists(PathOfExtra(name));
    }
}
