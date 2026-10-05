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

/// <summary>
/// One difference between the expected sound and the heard sound. Gop is the
/// goodness of pronunciation of the expected sound here (1 = the model clearly
/// heard it, 0 = it did not), computed from the frame posteriors, so even a
/// "correct" sound can be flagged as shaky.
/// </summary>
public sealed record PhonemeEdit(
    PhonemeErrorType Type,
    string? Expected,
    string? Heard,
    int Position,
    double Gop = 1.0);

public sealed record MddResult(
    bool Success,
    string Error,
    string HeardPhonemes,
    string ExpectedPhonemes,
    IReadOnlyList<PhonemeEdit> Edits,
    IReadOnlyList<WordPronunciation> Words,
    IReadOnlyList<string> SkippedWords,
    int Substitutions,
    int Omissions,
    int Insertions,
    int Correct,
    double MeanGop = 0)
{
    public int Total => Substitutions + Omissions + Insertions + Correct;

    public double Accuracy => Total == 0 ? 0 : Math.Round(Correct * 100.0 / Total, 1);

    public static MddResult Fail(string error, string expected)
        => new(false, error, string.Empty, expected,
            Array.Empty<PhonemeEdit>(), Array.Empty<WordPronunciation>(), Array.Empty<string>(),
            0, 0, 0, 0);
}

/// <summary>One target word with the mistakes found inside it.</summary>
public sealed record WordPronunciation(
    string Word,
    string Expected,
    string Heard,
    IReadOnlyList<PhonemeEdit> Edits,
    double Gop = 0)
{
    public bool HasErrors => Edits.Any(e => e.Type != PhonemeErrorType.Correct);
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
        var targetWords = _g2p.SentenceToWords(targetText);
        var expected = targetWords.SelectMany(w => w.Phonemes).ToArray();
        var expectedDisplay = _g2p.ToDisplay(expected);
        var skipped = targetWords
            .Where(w => w.Phonemes.Length == 0)
            .Select(w => w.Word)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
            var display = _g2p.ToDisplay(heard.Labels);
            var edits = Align(expected, heard.Labels);
            AttachGop(edits, expected, heard, labels);
            int sub = edits.Count(x => x.Type == PhonemeErrorType.Substitution);
            int omi = edits.Count(x => x.Type == PhonemeErrorType.Omission);
            int ins = edits.Count(x => x.Type == PhonemeErrorType.Insertion);
            int ok = edits.Count(x => x.Type == PhonemeErrorType.Correct);
            var words = GroupByWord(targetWords, edits);
            double meanGop = edits.Count == 0
                ? 0
                : Math.Round(edits.Where(e => e.Expected is not null).Select(e => e.Gop).DefaultIfEmpty(0).Average(), 3);
            return new MddResult(true, string.Empty, display, expectedDisplay, edits, words, skipped, sub, omi, ins, ok, meanGop);
        }, ct);
    }

    /// <summary>
    /// Goodness of pronunciation for one expected sound over its frames: the
    /// mean gap between the log posterior of that sound and the best sound each
    /// frame, mapped to 0..1. A sound the model clearly heard scores near 1 even
    /// when the alignment had to guess, and a substituted sound scores whatever
    /// little probability the expected sound had.
    /// </summary>
    public static double SegmentGop(IReadOnlyList<double> gaps)
    {
        if (gaps.Count == 0) return 0;
        double mean = 0;
        foreach (var gap in gaps) mean += gap;
        mean /= gaps.Count;
        if (mean >= 0) return 1;
        if (mean < -20) return 0;
        return Math.Round(Math.Exp(mean), 3);
    }

    private sealed record HeardRun(int LabelIndex, int StartFrame, int EndFrame);

    private sealed record HeardResult(string[] Labels, IReadOnlyList<HeardRun> Runs, float[,] LogPosteriors, float[] MaxLogPosteriors);

    /// <summary>
    /// Fills the Gop of every edit from the frame posteriors. Heard phones are
    /// consumed in order, so the run behind each edit is known: a correct or
    /// substituted sound takes the posterior of the expected sound over that
    /// run, an omission has no frames and scores 0, an insertion scores the
    /// posterior of the extra sound that was heard.
    /// </summary>
    private static void AttachGop(
        List<PhonemeEdit> edits, string[] expected, HeardResult heard, string[] labels)
    {
        var labelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i].Length > 0 && !labelIndex.ContainsKey(labels[i]))
                labelIndex[labels[i]] = i;
        }

        int run = 0;
        for (int i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            double gop;
            if (edit.Type == PhonemeErrorType.Omission)
            {
                gop = 0;
            }
            else if (run < heard.Runs.Count)
            {
                var segment = heard.Runs[run++];
                var wanted = edit.Expected ?? edit.Heard ?? string.Empty;
                gop = GopOverRun(segment, wanted, heard, labelIndex);
            }
            else
            {
                gop = 0;
            }
            edits[i] = edit with { Gop = gop };
        }
    }

    private static double GopOverRun(
        HeardRun run, string wanted, HeardResult heard, Dictionary<string, int> labelIndex)
    {
        if (!labelIndex.TryGetValue(wanted, out int wantedIndex)) return 0;
        var gaps = new List<double>();
        for (int t = run.StartFrame; t <= run.EndFrame; t++)
        {
            gaps.Add(heard.LogPosteriors[t, wantedIndex] - heard.MaxLogPosteriors[t]);
        }
        return SegmentGop(gaps);
    }

    /// <summary>
    /// Puts every edit inside its target word, so mistakes read as
    /// "in 'think': ..." instead of a bare phoneme position.
    /// Insertions sit between sounds, so they join the word on their left.
    /// </summary>
    private static IReadOnlyList<WordPronunciation> GroupByWord(
        IReadOnlyList<WordPhonemes> targetWords, IReadOnlyList<PhonemeEdit> edits)
    {
        var starts = new List<int>(targetWords.Count);
        int at = 0;
        foreach (var w in targetWords)
        {
            starts.Add(at);
            at += w.Phonemes.Length;
        }

        var buckets = targetWords.Select(_ => new List<PhonemeEdit>()).ToList();
        foreach (var edit in edits)
        {
            int word = edit.Expected is null
                ? WordAt(starts, targetWords, edit.Position - 1)
                : WordAt(starts, targetWords, edit.Position);
            if (word >= 0)
                buckets[word].Add(edit);
        }

        var result = new List<WordPronunciation>(targetWords.Count);
        for (int i = 0; i < targetWords.Count; i++)
        {
            var heard = buckets[i]
                .Where(e => e.Heard is not null)
                .Select(e => e.Heard!);
            var scored = buckets[i].Where(e => e.Expected is not null).ToList();
            double gop = scored.Count == 0 ? 0 : Math.Round(scored.Average(e => e.Gop), 3);
            result.Add(new WordPronunciation(
                targetWords[i].Word,
                string.Join(" ", targetWords[i].Phonemes.Select(p => $"/{p}/")),
                string.Join(" ", heard.Select(p => $"/{p}/")),
                buckets[i],
                gop));
        }
        return result;
    }

    /// <summary>Index of the word owning an expected sound position, or -1.</summary>
    private static int WordAt(List<int> starts, IReadOnlyList<WordPhonemes> words, int position)
    {
        for (int i = 0; i < words.Count; i++)
        {
            int end = starts[i] + words[i].Phonemes.Length;
            if (position >= starts[i] && position < end)
                return i;
        }
        return words.Count > 0 && position < 0 ? 0 : -1;
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

    private static HeardResult Recognize(InferenceSession session, string[] labels, string wavPath, CancellationToken ct)
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

        // Log posteriors per frame, so every heard sound carries how sure the
        // model was, not just which label won.
        var logPosteriors = new float[frames, classes];
        var maxLogPosteriors = new float[frames];
        var argmax = new int[frames];
        for (int t = 0; t < frames; t++)
        {
            float max = float.NegativeInfinity;
            for (int c = 0; c < classes; c++)
            {
                float v = logits[0, t, c];
                if (v > max) max = v;
            }
            double sum = 0;
            for (int c = 0; c < classes; c++)
                sum += Math.Exp(logits[0, t, c] - max);
            double logSum = Math.Log(sum);
            int best = 0;
            float bestLog = float.NegativeInfinity;
            for (int c = 0; c < classes; c++)
            {
                float logP = (float)(logits[0, t, c] - max - logSum);
                logPosteriors[t, c] = logP;
                if (logP > bestLog) { bestLog = logP; best = c; }
            }
            maxLogPosteriors[t] = bestLog;
            argmax[t] = best;
        }

        // CTC collapse into runs, same rule as before: skip repeats, skip labels
        // that are not spoken sounds.
        var heard = new List<string>();
        var runs = new List<HeardRun>();
        int last = -1;
        int runStart = 0;
        for (int t = 0; t < frames; t++)
        {
            int best = argmax[t];
            if (best == last)
            {
                if (runs.Count > 0 && best >= 0 && best < labels.Length && labels[best].Length > 0)
                    runs[^1] = runs[^1] with { EndFrame = t };
                continue;
            }
            last = best;
            runStart = t;
            if (best >= 0 && best < labels.Length && labels[best].Length > 0)
            {
                heard.Add(labels[best]);
                runs.Add(new HeardRun(best, runStart, t));
            }
        }
        return new HeardResult(heard.ToArray(), runs, logPosteriors, maxLogPosteriors);
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
    public static List<PhonemeEdit> Align(string[] expected, string[] heard)
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
