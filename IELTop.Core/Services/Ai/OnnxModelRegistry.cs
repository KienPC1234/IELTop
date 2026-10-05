namespace IELTop.Services.Ai;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// One ONNX model slot. Drop the file into Assets/Models and it is picked up here.
/// Source and License are recorded so the list stays honest about where each model comes from.
/// Every file a slot needs is declared: a slot is only usable when the model file,
/// the paired file, and every support file (vocab, sentence piece, config) exist.
/// </summary>
public sealed record OnnxModelSlot(
    string Name,
    string FileName,
    string Skill,
    string Purpose,
    string License,
    string Source,
    bool Required = false,
    string? ExtraFile = null,
    IReadOnlyList<string>? ExtraFiles = null);

/// <summary>
/// The offline models this app supports. MDD drives pronunciation error
/// detection, STT transcribes speaking audio, GEC corrects writing, and the
/// Piper voice reads text aloud. All run on CPU. Large files are never committed
/// to git; the desktop project copies whatever is on disk into the output.
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
        new("stt-whisper-base-en", "stt-whisper-base-en-encoder-int8.onnx",
            "Speaking",
            "Transcribe recorded speech (balanced speed and accuracy)",
            "MIT",
            "openai/whisper-base.en, exported by tools/speech-stt",
            ExtraFile: "stt-whisper-base-en-decoder-int8.onnx",
            ExtraFiles: new[] { "stt-whisper-base-en-vocab.json" }),
        new("stt-whisper-small-en", "stt-whisper-small-en-encoder-int8.onnx",
            "Speaking",
            "Transcribe recorded speech (most accurate of the three)",
            "MIT",
            "openai/whisper-small.en, exported by tools/speech-stt",
            ExtraFile: "stt-whisper-small-en-decoder-int8.onnx",
            ExtraFiles: new[] { "stt-whisper-small-en-vocab.json" }),
        new("gec-t5-small", "gec-t5-small-encoder-int8.onnx",
            "Writing",
            "Correct grammar in writing and cap the band from measured errors",
            "Apache-2.0",
            "Unbabel/gec-t5_small, exported by tools/writing-gec",
            ExtraFile: "gec-t5-small-decoder-int8.onnx",
            ExtraFiles: new[] { "gec-t5-spiece.model" }),
        new("tts-piper-lessac", "tts-piper-lessac-medium.onnx",
            "Speaking, Listening",
            "Read text aloud with a neural voice for listening and speaking",
            "MIT",
            "rhasspy/piper-voices en_US-lessac-medium, fetched by tools/speaking-tts",
            ExtraFile: "tts-piper-lessac-medium.onnx.json"),
    };

    public static string ModelsDir =>
        _modelsDirOverride ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Models");

    private static string? _modelsDirOverride;

    /// <summary>
    /// Points the registry at another model folder, for tests that run from a
    /// different output directory than the app.
    /// </summary>
    public static void SetModelsDirForTesting(string? dir) => _modelsDirOverride = dir;

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

    /// <summary>Full path of one support file declared by a slot.</summary>
    public static string PathOfExtraFile(string name, string file)
    {
        var slot = Slots.FirstOrDefault(s => s.Name == name);
        if (slot is null || slot.ExtraFiles is null || !slot.ExtraFiles.Contains(file)) return string.Empty;
        return Path.Combine(ModelsDir, file);
    }

    /// <summary>The files a slot needs that are not on disk, for a clear message.</summary>
    public static IReadOnlyList<string> MissingFiles(string name)
    {
        var slot = Slots.FirstOrDefault(s => s.Name == name);
        if (slot is null) return new[] { name };
        var missing = new List<string>();
        if (!File.Exists(PathOf(name))) missing.Add(slot.FileName);
        if (slot.ExtraFile is not null && !File.Exists(PathOfExtra(name))) missing.Add(slot.ExtraFile);
        if (slot.ExtraFiles is not null)
        {
            foreach (var file in slot.ExtraFiles)
            {
                if (!File.Exists(Path.Combine(ModelsDir, file))) missing.Add(file);
            }
        }
        return missing;
    }

    /// <summary>True when the model file, the paired file, and every support file exist.</summary>
    public static bool IsComplete(string name) => MissingFiles(name).Count == 0;

    /// <summary>STT slots, best first. The app uses the first one that is installed.</summary>
    public static IReadOnlyList<string> SttSlotsByQuality { get; } = new[]
    {
        "stt-whisper-small-en", "stt-whisper-base-en",
    };

    /// <summary>
    /// The transcription slot to use: the most accurate one that is installed.
    /// Copying a bigger model into Assets/Models is all it takes to switch.
    /// </summary>
    public static string ResolveSttSlot() =>
        SttSlotsByQuality.FirstOrDefault(IsComplete) ?? SttSlotsByQuality[^1];
}
