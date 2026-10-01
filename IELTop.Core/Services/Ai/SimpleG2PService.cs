using System.IO;
using System.Text.Json;

namespace IELTop.Services.Ai;

/// <summary>
/// Đọc bảng ánh xạ từ tiếng Anh sang âm vị IPA đơn giản, dùng để tạo
/// chuỗi âm vị chuẩn cho người học đọc. Không cần espeak offline.
/// Cách phát âm tham khảo từ CMUdict, gom nhóm theo vần thường gặp.
/// </summary>
public interface IG2PService
{
    string[] ToPhonemes(string word);
    string[] SentenceToPhonemes(string sentence);
    string ToDisplay(IEnumerable<string> phonemes);

    /// <summary>Target words split for scoring, with the phonemes of each word.</summary>
    IReadOnlyList<WordPhonemes> SentenceToWords(string sentence);

    /// <summary>Words with no entry in the map. They are skipped in scoring.</summary>
    string[] UnknownWords(string sentence);
}

/// <summary>One target word and its canonical sounds.</summary>
public sealed record WordPhonemes(string Word, string[] Phonemes);

public sealed class SimpleG2PService : IG2PService
{
    private readonly Dictionary<string, string[]> _map;

    public SimpleG2PService()
    {
        var path = Path.Combine(OnnxModelRegistry.ModelsDir, "phoneme-map.json");
        if (!File.Exists(path))
        {
            _map = BuildFallback();
            return;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path));
            if (raw is null || raw.Count == 0)
            {
                _map = BuildFallback();
                return;
            }
            // Keys starting with "_" are metadata (source, license), not words.
            _map = raw
                .Where(kv => !kv.Key.StartsWith('_'))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // A broken map must not stop the app. Fall back to the built in words.
            _map = BuildFallback();
        }
    }

    public string[] ToPhonemes(string word)
    {
        var key = word.Trim().ToLowerInvariant().Trim('\'', '"', '.', ',', '!', '?', ';', ':');
        if (key.Length == 0) return Array.Empty<string>();
        if (key.Length == 1 && DigitWords.TryGetValue(key[0], out var digit))
            key = digit;
        return _map.TryGetValue(key, out var p) ? p : BuildFallback(key);
    }

    public string[] SentenceToPhonemes(string sentence)
    {
        var result = new List<string>();
        foreach (var word in SentenceToWords(sentence))
            result.AddRange(word.Phonemes);
        return result.ToArray();
    }

    public IReadOnlyList<WordPhonemes> SentenceToWords(string sentence)
    {
        var result = new List<WordPhonemes>();
        // Hyphenated compounds are spoken as separate words.
        var cleaned = sentence.Replace('-', ' ').Replace('/', ' ');
        foreach (var raw in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            result.Add(new WordPhonemes(raw, ToPhonemes(raw)));
        return result;
    }

    public string[] UnknownWords(string sentence)
    {
        var unknown = new List<string>();
        foreach (var word in SentenceToWords(sentence))
        {
            if (word.Phonemes.Length == 0 && !unknown.Contains(word.Word, StringComparer.OrdinalIgnoreCase))
                unknown.Add(word.Word);
        }
        return unknown.ToArray();
    }

    public string ToDisplay(IEnumerable<string> phonemes)
        => string.Join(" ", phonemes.Select(p => $"/{p}/"));

    /// <summary>
    /// Small built in map used only when phoneme-map.json is missing or broken.
    /// Must use the same IPA symbols the model emits, or scores would be wrong.
    /// </summary>
    private Dictionary<string, string[]> BuildFallback()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["cat"] = new[] { "k", "æ", "t" },
            ["think"] = new[] { "θ", "ɪ", "ŋ", "k" },
            ["the"] = new[] { "ð", "ə" },
            ["this"] = new[] { "ð", "ɪ", "s" },
            ["three"] = new[] { "θ", "ɹ", "iː" },
            ["ship"] = new[] { "ʃ", "ɪ", "p" },
            ["sheep"] = new[] { "ʃ", "iː", "p" },
            ["very"] = new[] { "v", "ɛ", "ɹ", "i" },
            ["hello"] = new[] { "h", "ə", "l", "oʊ" },
            ["world"] = new[] { "w", "ɜː", "l", "d" },
            ["english"] = new[] { "ɪ", "ŋ", "ɡ", "l", "ɪ", "ʃ" },
            ["people"] = new[] { "p", "iː", "p", "ə", "l" },
            ["water"] = new[] { "w", "ɔː", "t", "ə" },
            ["study"] = new[] { "s", "t", "ʌ", "d", "i" },
            ["learning"] = new[] { "l", "ɜː", "n", "ɪ", "ŋ" },
            ["language"] = new[] { "l", "æ", "ŋ", "ɡ", "w", "ɪ", "dʒ" },
        };

    /// <summary>
    /// Last resort for a word that is not in any map. Returns nothing rather
    /// than guessing, so a missing word cannot create fake pronunciation errors.
    /// </summary>
    private static string[] BuildFallback(string word) => Array.Empty<string>();

    private static readonly Dictionary<char, string> DigitWords = new()
    {
        ['0'] = "zero", ['1'] = "one", ['2'] = "two", ['3'] = "three", ['4'] = "four",
        ['5'] = "five", ['6'] = "six", ['7'] = "seven", ['8'] = "eight", ['9'] = "nine"
    };
}
