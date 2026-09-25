using System.IO;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IELTop.Services.Ai;

public enum PhonemeErrorType
{
    Correct,
    Substitution,
    Omission,
    Insertion
}

/// <summary>Một điểm khác biệt giữa âm chuẩn (Expected) và âm nghe được (Heard).</summary>
public sealed record PhonemeEdit(
    PhonemeErrorType Type,
    string? Expected,
    string? Heard,
    int Position);

public sealed record MddResult(
    bool Success,
    string Error,
    string HeardPhonemes,
    string ExpectedPhonemes,
    IReadOnlyList<PhonemeEdit> Edits,
    int Substitutions,
    int Omissions,
    int Insertions,
    int Correct)
{
    public int Total => Substitutions + Omissions + Insertions + Correct;

    public double Accuracy => Total == 0 ? 0 : Math.Round(Correct * 100.0 / Total, 1);

    public static MddResult Fail(string error, string expected)
        => new(false, error, string.Empty, expected, Array.Empty<PhonemeEdit>(), 0, 0, 0, 0);
}

public interface IMddPhonemeService
{
    bool IsModelAvailable();
    Task<MddResult> AssessAsync(string wavPath, string targetText, CancellationToken ct = default);
}

/// <summary>
/// Chấm phát âm ở mức âm vị: nghe file, nhận chuỗi âm thực tế,
/// rồi gióng hàng với chuỗi âm chuẩn để tìm lỗi thay thế / bỏ sót / thêm âm.
/// Model chạy trong Task.Run nên giao diện không bị đơ.
/// </summary>
public sealed class MddPhonemeService : IMddPhonemeService
{
    public const string SlotName = "mdd-wav2vec2-base";

    private readonly IOnnxService _onnx;
    private readonly IG2PService _g2p;
    private string[]? _labels;

    public MddPhonemeService(IOnnxService onnx, IG2PService g2p)
    {
        _onnx = onnx;
        _g2p = g2p;
    }

    public bool IsModelAvailable()
    {
        return OnnxModelRegistry.IsComplete(SlotName);
    }

    public async Task<MddResult> AssessAsync(string wavPath, string targetText, CancellationToken ct = default)
    {
        var expected = _g2p.SentenceToPhonemes(targetText);
        var expectedDisplay = _g2p.ToDisplay(expected);

        if (!IsModelAvailable())
            return MddResult.Fail("The pronunciation model is not installed. See Assets/Models for setup.", expectedDisplay);
        if (!File.Exists(wavPath))
            return MddResult.Fail("Recording file not found.", expectedDisplay);

        if (!_onnx.TryLoad(SlotName, out var loadError))
            return MddResult.Fail(loadError, expectedDisplay);
        var session = _onnx.Get(SlotName);
        if (session is null)
            return MddResult.Fail("Could not open the pronunciation model.", expectedDisplay);

        var labels = LoadLabels();
        if (labels.Length == 0)
            return MddResult.Fail("Missing phoneme label file (mdd-labels.json).", expectedDisplay);

        return await Task.Run(() =>
        {
            var heard = Recognize(session, labels, wavPath, ct);
            var display = _g2p.ToDisplay(heard);
            var edits = Align(expected, heard);
            int sub = edits.Count(x => x.Type == PhonemeErrorType.Substitution);
            int omi = edits.Count(x => x.Type == PhonemeErrorType.Omission);
            int ins = edits.Count(x => x.Type == PhonemeErrorType.Insertion);
            int ok = edits.Count(x => x.Type == PhonemeErrorType.Correct);
            return new MddResult(true, string.Empty, display, expectedDisplay, edits, sub, omi, ins, ok);
        }, ct);
    }

    private string[] LoadLabels()
    {
        if (_labels is not null) return _labels;
        var path = Path.Combine(OnnxModelRegistry.ModelsDir, "mdd-labels.json");
        if (!File.Exists(path)) return _labels = Array.Empty<string>();

        var raw = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new();
        // The model emits IPA phonemes. Keep the symbols as they are, since IPA is
        // case sensitive, but drop special tokens, the word bar, and stress marks
        // so the heard sounds line up with the canonical symbols in phoneme-map.json.
        _labels = raw.Select(CleanLabel).ToArray();
        return _labels;
    }

    /// <summary>
    /// Turns a raw token into a comparison symbol. Returns an empty string for
    /// tokens that are not spoken sounds.
    /// </summary>
    private static string CleanLabel(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        if (token.StartsWith("<") || token.StartsWith("[")) return string.Empty;

        var cleaned = token
            .Replace("|", string.Empty)
            .Replace("\u02C8", string.Empty) // primary stress
            .Replace("\u02CC", string.Empty) // secondary stress
            .Trim();

        return cleaned;
    }

    private static string[] Recognize(InferenceSession session, string[] labels, string wavPath, CancellationToken ct)
    {
        var samples = Audio.WavLoader.LoadMono16k(wavPath);
        Normalize(samples);

        var inputName = session.InputMetadata.Keys.First();
        var tensor = new DenseTensor<float>(samples, new[] { 1, samples.Length });
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });

        ct.ThrowIfCancellationRequested();

        var logits = results.First().AsTensor<float>();
        int frames = logits.Dimensions[1];
        int classes = logits.Dimensions[2];

        var heard = new List<string>();
        int last = -1;
        for (int t = 0; t < frames; t++)
        {
            int best = 0;
            float bestVal = float.NegativeInfinity;
            for (int c = 0; c < classes; c++)
            {
                float v = logits[0, t, c];
                if (v > bestVal) { bestVal = v; best = c; }
            }
            if (best == last) continue; // CTC gộp khung lặp liền kề
            last = best;
            if (best >= 0 && best < labels.Length && labels[best].Length > 0)
                heard.Add(labels[best]);
        }
        return heard.ToArray();
    }

    /// <summary>wav2vec2 expects audio normalized to mean 0 and variance 1.</summary>
    private static void Normalize(float[] samples)
    {
        if (samples.Length == 0) return;
        double mean = 0;
        foreach (var s in samples) mean += s;
        mean /= samples.Length;
        double variance = 0;
        foreach (var s in samples) variance += (s - mean) * (s - mean);
        double std = Math.Sqrt(variance / samples.Length);
        if (std < 1e-5) return;
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)((samples[i] - mean) / std);
    }

    /// <summary>Levenshtein alignment that returns each edit between the two phoneme strings.</summary>
    public static IReadOnlyList<PhonemeEdit> Align(string[] expected, string[] heard)
    {
        int n = expected.Length, m = heard.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
            {
                int cost = expected[i - 1] == heard[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }

        var edits = new List<PhonemeEdit>();
        int x = n, y = m;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0)
            {
                int cost = expected[x - 1] == heard[y - 1] ? 0 : 1;
                if (d[x, y] == d[x - 1, y - 1] + cost)
                {
                    edits.Add(new PhonemeEdit(
                        cost == 0 ? PhonemeErrorType.Correct : PhonemeErrorType.Substitution,
                        expected[x - 1], heard[y - 1], x - 1));
                    x--; y--;
                    continue;
                }
            }
            if (x > 0 && d[x, y] == d[x - 1, y] + 1)
            {
                edits.Add(new PhonemeEdit(PhonemeErrorType.Omission, expected[x - 1], null, x - 1));
                x--;
                continue;
            }
            edits.Add(new PhonemeEdit(PhonemeErrorType.Insertion, null, heard[y - 1], x));
            y--;
        }

        edits.Reverse();
        return edits;
    }
}
