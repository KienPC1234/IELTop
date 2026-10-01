using System.IO;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IELTop.Services.Ai;

public sealed record GecEdit(string Original, string Corrected);
public sealed record GecResult(
    bool Success,
    string Error,
    string CorrectedText,
    IReadOnlyList<GecEdit> Edits,
    int ErrorCount,
    double ErrorsPer100Words)
{
    public static GecResult Fail(string error)
        => new(false, error, string.Empty, Array.Empty<GecEdit>(), 0, 0);
}

public interface IGecService
{
    bool IsAvailable();
    Task<GecResult> CheckAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// Deterministic grammar check with gec-t5_small on CPU. Each sentence is
/// corrected on its own, so one bad sentence cannot change the rest.
/// Counts errors per 100 words, which caps the Writing grammar band and
/// keeps generous AI marking honest.
/// </summary>
public sealed partial class GecService : IGecService
{
    public const string SlotName = "gec-t5-small";
    private const int MaxInputTokens = 128;
    private const int MaxOutputTokens = 64;

    private readonly IOnnxService _onnx;
    private SentencePieceCodec? _codec;

    public GecService(IOnnxService onnx)
    {
        _onnx = onnx;
    }

    public bool IsModelAvailable() => OnnxModelRegistry.IsComplete(SlotName)
        && File.Exists(SpiecePath);

    bool IGecService.IsAvailable() => IsModelAvailable();

    private static string SpiecePath => Path.Combine(
        OnnxModelRegistry.ModelsDir, "gec-t5-spiece.model");

    public async Task<GecResult> CheckAsync(string text, CancellationToken ct = default)
    {
        if (!IsModelAvailable())
            return GecResult.Fail("The grammar model is not installed. See Assets/Models for setup.");
        var sentences = SplitSentences(text);
        if (sentences.Count == 0)
            return GecResult.Fail("There is no text to check.");

        if (!_onnx.TryLoad(SlotName, out var loadError))
            return GecResult.Fail(loadError);

        try
        {
            _codec ??= SentencePieceCodec.Load(SpiecePath);
        }
        catch (Exception)
        {
            return GecResult.Fail("The grammar word list is broken. Re-run the download script.");
        }

        return await Task.Run(() =>
        {
            var edits = new List<GecEdit>();
            var fixedSentences = new List<string>();
            foreach (var sentence in sentences)
            {
                ct.ThrowIfCancellationRequested();
                var fixedSentence = CorrectOne(sentence, ct);
                fixedSentences.Add(fixedSentence);
                if (!SameIgnoringCaseSpace(sentence, fixedSentence))
                    edits.Add(new GecEdit(sentence.Trim(), fixedSentence.Trim()));
            }
            int words = CountWords(text);
            double per100 = words == 0 ? 0 : Math.Round(edits.Count * 100.0 / words, 1);
            return new GecResult(true, string.Empty,
                string.Join(" ", fixedSentences), edits, edits.Count, per100);
        }, ct);
    }

    private string CorrectOne(string sentence, CancellationToken ct)
    {
        var encoder = RequireEncoder();
        var decoder = RequireDecoder();
        var inputIds = _codec!.Encode("gec: " + sentence.Trim(), MaxInputTokens);

        var encTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
        for (int i = 0; i < inputIds.Length; i++)
            encTensor[0, i] = inputIds[i];
        var mask = new DenseTensor<long>(new[] { 1, inputIds.Length });
        for (int i = 0; i < inputIds.Length; i++)
            mask[0, i] = 1;

        float[,,] hidden;
        var encInputs = ResolveInputs(encoder, encTensor, mask);
        using (var encOut = encoder.Run(encInputs))
        {
            var last = encOut.First().AsTensor<float>();
            int frames = last.Dimensions[1];
            int dim = last.Dimensions[2];
            hidden = new float[1, frames, dim];
            for (int t = 0; t < frames; t++)
                for (int d = 0; d < dim; d++)
                    hidden[0, t, d] = last[0, t, d];
        }

        var ids = Seq2SeqDecoder.Greedy(decoder, hidden, new[] { 0 }, 1, MaxOutputTokens, ct);
        return _codec.Decode(ids.Skip(1));
    }

    private static IReadOnlyCollection<NamedOnnxValue> ResolveInputs(
        InferenceSession session, DenseTensor<long> ids, DenseTensor<long> mask)
    {
        var inputs = new List<NamedOnnxValue>();
        foreach (var name in session.InputMetadata.Keys)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("attention_mask"))
                inputs.Add(NamedOnnxValue.CreateFromTensor(name, mask));
            else if (lower.Contains("input_ids"))
                inputs.Add(NamedOnnxValue.CreateFromTensor(name, ids));
        }
        return inputs;
    }

    private InferenceSession RequireEncoder()
    {
        var session = _onnx.Get(SlotName);
        if (session is null)
            throw new InvalidOperationException("The grammar encoder is not loaded.");
        return session;
    }

    private InferenceSession RequireDecoder()
    {
        var session = _onnx.GetExtra(SlotName);
        if (session is null)
            throw new InvalidOperationException("The grammar decoder is not loaded.");
        return session;
    }

    private static List<string> SplitSentences(string text)
    {
        var parts = SentenceSplit().Split(text);
        var sentences = new List<string>();
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (s.Length >= 2)
                sentences.Add(s.Length > 500 ? s[..500] : s);
            if (sentences.Count >= 60) break;
        }
        return sentences;
    }

    private static bool SameIgnoringCaseSpace(string a, string b)
    {
        static string norm(string s) => Whitespace().Replace(s.Trim(), " ");
        return string.Equals(norm(a), norm(b), StringComparison.OrdinalIgnoreCase);
    }

    private static int CountWords(string text)
        => Whitespace().Split(text.Trim()).Count(s => s.Length > 0);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();
}
