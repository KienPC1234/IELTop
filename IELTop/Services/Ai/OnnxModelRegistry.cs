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
/// The offline models this app supports. Every entry points to a real,
/// downloadable model with a recorded source and license. Not all are wired to
/// a feature yet: the MDD slot drives Speaking scoring today, the others are
/// loadable and reserved for vocabulary, dictation and grammar features.
/// All run on CPU. Large files are never committed to git.
/// </summary>
public static class OnnxModelRegistry
{
    public static IReadOnlyList<OnnxModelSlot> Slots { get; } = new List<OnnxModelSlot>
    {
        new("vocab-embeddings", "all-MiniLM-L6-v2.onnx",
            "Vocabulary",
            "Embed words and sentences so the app can suggest related vocabulary",
            "Apache-2.0",
            "Qdrant/all-MiniLM-L6-v2-onnx on HuggingFace",
            ExtraFile: "vocab.txt"),

        new("mdd-wav2vec2-base", "mdd-wav2vec2-base-int8.onnx",
            "Speaking",
            "Recognize spoken phonemes so the app can find pronunciation errors",
            "Apache-2.0",
            "bobboyms/wav2vec2-base-en-phoneme-ctc-41h, exported by tools/speaking-mdd",
            ExtraFile: "mdd-labels.json"),

        new("whisper-tiny-encoder", "whisper-tiny-encoder.onnx",
            "Speaking",
            "Turn speech into text, encoder half",
            "MIT",
            "openai/whisper-tiny, exported with Optimum ONNX",
            ExtraFile: "whisper-tiny-tokens.txt"),
        new("whisper-tiny-decoder", "whisper-tiny-decoder.onnx",
            "Speaking",
            "Turn speech into text, decoder half",
            "MIT",
            "openai/whisper-tiny, exported with Optimum ONNX",
            ExtraFile: "whisper-tiny-tokens.txt"),

        new("listening-vad", "silero-vad.onnx",
            "Listening",
            "Find the speech parts of a clip so silence can be skipped",
            "MIT",
            "onnx-community/silero-vad on HuggingFace"),

        new("grammar-gec", "grammar-gec-t5-small.onnx",
            "Writing",
            "Suggest grammar fixes for a sentence",
            "CC-BY-NC-SA-4.0 (not for commercial use)",
            "vennify/t5-base-grammar-correction, exported with Optimum ONNX",
            ExtraFile: "spiece.model"),
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
