using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IELTop.Services.Ai;

public sealed record SttResult(bool Success, string Text, string Error)
{
    public static SttResult Fail(string error) => new(false, string.Empty, error);
}

public interface ISttService
{
    bool IsAvailable();
    Task<SttResult> TranscribeAsync(string wavPath, CancellationToken ct = default);
}

/// <summary>
/// English speech to text with whisper-tiny.en on CPU. Audio is cut into
/// 30 second windows, each encoded once and decoded greedily, then the
/// window texts are joined. Needs the encoder, decoder, and vocab files.
/// </summary>
public sealed class SttService : ISttService
{
    public const string SlotName = "stt-whisper-tiny-en";

    private readonly IOnnxService _onnx;
    private Dictionary<int, string>? _vocab;

    public SttService(IOnnxService onnx)
    {
        _onnx = onnx;
    }

    public bool IsModelAvailable() => OnnxModelRegistry.IsComplete(SlotName)
        && File.Exists(VocabPath);

    bool ISttService.IsAvailable() => IsModelAvailable();

    private static string VocabPath => Path.Combine(
        OnnxModelRegistry.ModelsDir, "stt-whisper-tiny-en-vocab.json");

    public async Task<SttResult> TranscribeAsync(string wavPath, CancellationToken ct = default)
    {
        if (!IsModelAvailable())
            return SttResult.Fail("The transcription model is not installed. See Assets/Models for setup.");
        if (!File.Exists(wavPath))
            return SttResult.Fail("Recording file not found.");

        if (!_onnx.TryLoad(SlotName, out var loadError))
            return SttResult.Fail(loadError);

        try
        {
            _vocab ??= WhisperCodec.LoadVocab(VocabPath);
        }
        catch (Exception)
        {
            return SttResult.Fail("The transcription word list is broken. Re-run the download script.");
        }

        return await Task.Run(() =>
        {
            var parts = TranscribeWindows(wavPath, ct);
            return new SttResult(true, string.Join(" ", parts).Trim(), string.Empty);
        }, ct);
    }

    private List<string> TranscribeWindows(string wavPath, CancellationToken ct)
    {
        var pcm = Audio.WavLoader.LoadMono16k(wavPath);
        var texts = new List<string>();
        for (int at = 0; at < pcm.Length; at += WhisperFeatures.SamplesPerWindow)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(WhisperFeatures.SamplesPerWindow, pcm.Length - at);
            var window = new float[WhisperFeatures.SamplesPerWindow];
            Array.Copy(pcm, at, window, 0, len);

            var mel = WhisperFeatures.LogMel(window);
            var hidden = RunEncoder(mel, ct);
            var ids = Seq2SeqDecoder.Greedy(
                RequireDecoder(), hidden, WhisperCodec.Prompt, WhisperCodec.Eot, 448, ct,
                blockFirstStep: new[] { 220, WhisperCodec.Eot },
                blockAlways: new[] { 50359, 50360, 50361 });
            var text = WhisperCodec.Decode(ids.Skip(WhisperCodec.Prompt.Length), _vocab!);
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(text.Trim());
        }
        return texts;
    }

    private float[,,] RunEncoder(float[,] mel, CancellationToken ct)
    {
        var session = RequireEncoder();
        var inputName = session.InputMetadata.Keys.First();
        var tensor = new DenseTensor<float>(new[] { 1, 80, WhisperFeatures.FramesPerWindow });
        for (int m = 0; m < 80; m++)
            for (int f = 0; f < WhisperFeatures.FramesPerWindow; f++)
                tensor[0, m, f] = mel[m, f];
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
        ct.ThrowIfCancellationRequested();
        var hidden = results.First().AsTensor<float>();
        int frames = hidden.Dimensions[1];
        int dim = hidden.Dimensions[2];
        var output = new float[1, frames, dim];
        for (int t = 0; t < frames; t++)
            for (int d = 0; d < dim; d++)
                output[0, t, d] = hidden[0, t, d];
        return output;
    }

    private InferenceSession RequireEncoder()
    {
        var session = _onnx.Get(SlotName);
        if (session is null)
            throw new InvalidOperationException("The transcription encoder is not loaded.");
        return session;
    }

    private InferenceSession RequireDecoder()
    {
        var session = _onnx.GetExtra(SlotName);
        if (session is null)
            throw new InvalidOperationException("The transcription decoder is not loaded.");
        return session;
    }
}
