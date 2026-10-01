using System.IO;
using System.Text.Json;
using IELTop.Services.Audio;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IELTop.Services.Ai;

/// <summary>
/// Examiner voice: Piper neural TTS when its files are installed. The neural
/// path phonemizes with espeak-ng, maps phonemes to ids from the voice config,
/// and runs the VITS model clause by clause. When another voice is supplied
/// (a host may add a system voice) it is used as a fallback. Everything here
/// is offline and OS neutral; the wav is written directly, no audio library.
/// </summary>
public sealed class PiperTtsService : ITtsService
{
    public const string SlotName = "tts-piper-lessac";

    private readonly IOnnxService _onnx;
    private readonly ITtsService? _fallback;

    public PiperTtsService(IOnnxService onnx, ITtsService? fallback = null)
    {
        _onnx = onnx;
        _fallback = fallback;
    }

    public bool IsAvailable => IsNeuralAvailable || _fallback?.IsAvailable == true;

    public string VoiceName => IsNeuralAvailable
        ? "Neural voice"
        : _fallback?.VoiceName ?? "No voice";

    public bool IsNeuralAvailable => OnnxModelRegistry.IsComplete(SlotName)
        && File.Exists(ConfigPath) && EspeakPhonemizer.IsAvailable;

    private static string ConfigPath => Path.Combine(
        OnnxModelRegistry.ModelsDir, "tts-piper-lessac-medium.onnx.json");

    public async Task<string> SpeakToFileAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (!IsNeuralAvailable)
        {
            if (_fallback is not null && _fallback.IsAvailable)
                return await _fallback.SpeakToFileAsync(text, ct);
            return string.Empty;
        }

        try
        {
            return await Task.Run(() => Synthesize(text, ct), ct);
        }
        catch (Exception)
        {
            // The neural voice must never block listening. Fall back quietly.
            if (_fallback is not null && _fallback.IsAvailable)
                return await _fallback.SpeakToFileAsync(text, ct);
            return string.Empty;
        }
    }

    private string Synthesize(string text, CancellationToken ct)
    {
        _config ??= PiperVoiceConfig.Load(ConfigPath);
        if (!_onnx.TryLoad(SlotName, out _))
            throw new InvalidOperationException("Voice model not loaded.");
        var session = _onnx.Get(SlotName)
            ?? throw new InvalidOperationException("Voice model not loaded.");

        var clauses = EspeakPhonemizer.Phonemize(text, _config.EspeakVoice, _config.PhonemeIds);
        if (clauses.Count == 0)
            throw new InvalidOperationException("Phonemizer produced nothing.");

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("piper|" + text)));
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "tts");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"piper_{hash}.wav");
        if (File.Exists(path))
            return path;

        var samples = new List<float>();
        foreach (var ids in clauses)
        {
            ct.ThrowIfCancellationRequested();
            samples.AddRange(RunVoice(session, _config, ids));
        }

        WriteWav(path, _config.SampleRate, samples);
        return path;
    }

    private PiperVoiceConfig? _config;

    /// <summary>Writes 16 bit mono PCM by hand so no audio library is needed.</summary>
    private static void WriteWav(string path, int sampleRate, IReadOnlyList<float> samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        int dataBytes = samples.Count * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);

        foreach (var s in samples)
            writer.Write((short)(Math.Clamp(s, -1f, 1f) * 32767));
    }

    private static float[] RunVoice(InferenceSession session, PiperVoiceConfig config, int[] ids)
    {
        var input = new DenseTensor<long>(new[] { 1, ids.Length });
        for (int i = 0; i < ids.Length; i++)
            input[0, i] = ids[i];
        var lengths = new DenseTensor<long>(new[] { 1 });
        lengths[0] = ids.Length;
        var scales = new DenseTensor<float>(new[] { 3 });
        scales[0] = config.NoiseScale;
        scales[1] = config.LengthScale;
        scales[2] = config.NoiseW;

        string FindInput(string want)
        {
            foreach (var name in session.InputMetadata.Keys)
                if (name.Equals(want, StringComparison.OrdinalIgnoreCase))
                    return name;
            throw new InvalidOperationException($"Voice input {want} missing.");
        }

        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(FindInput("input"), input),
            NamedOnnxValue.CreateFromTensor(FindInput("input_lengths"), lengths),
            NamedOnnxValue.CreateFromTensor(FindInput("scales"), scales),
        });
        var audio = results.First().AsTensor<float>();
        int len = audio.Dimensions[2];
        var output = new float[len];
        for (int i = 0; i < len; i++)
            output[i] = audio[0, 0, i];
        return output;
    }

    private sealed class PiperVoiceConfig
    {
        public string EspeakVoice { get; private set; } = "en-us";
        public int SampleRate { get; private set; } = 22050;
        public float NoiseScale { get; private set; } = 0.667f;
        public float LengthScale { get; private set; } = 1f;
        public float NoiseW { get; private set; } = 0.8f;
        public Dictionary<string, int[]> PhonemeIds { get; private set; } = new();

        public static PiperVoiceConfig Load(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var config = new PiperVoiceConfig();
            if (root.TryGetProperty("espeak", out var espeak)
                && espeak.TryGetProperty("voice", out var voice))
                config.EspeakVoice = voice.GetString() ?? "en-us";
            if (root.TryGetProperty("audio", out var audio)
                && audio.TryGetProperty("sample_rate", out var rate))
                config.SampleRate = rate.GetInt32();
            if (root.TryGetProperty("inference", out var infer))
            {
                if (infer.TryGetProperty("noise_scale", out var ns))
                    config.NoiseScale = (float)ns.GetDouble();
                if (infer.TryGetProperty("length_scale", out var ls))
                    config.LengthScale = (float)ls.GetDouble();
                if (infer.TryGetProperty("noise_w", out var nw))
                    config.NoiseW = (float)nw.GetDouble();
            }
            if (root.TryGetProperty("phoneme_id_map", out var map))
            {
                foreach (var prop in map.EnumerateObject())
                {
                    var ids = prop.Value.EnumerateArray()
                        .Select(e => e.GetInt32()).ToArray();
                    config.PhonemeIds[prop.Name] = ids;
                }
            }
            return config;
        }
    }
}
