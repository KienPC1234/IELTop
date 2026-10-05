using System.IO;
using System.Text;
using System.Text.Json;

namespace IELTop.Services.Ai;

/// <summary>
/// <summary>
/// GPT-2 style byte decoding for the English whisper models. The prompt uses
/// hardcoded special token ids, which are identical across whisper-tiny/base/
/// small .en because they share one vocabulary.
/// </summary>
/// special token ids (measured from the real tokenizer), so BPE encoding is
/// never needed. Only decoding text tokens back to words is implemented.
/// </summary>
public static class WhisperCodec
{
    // Measured from the English-only layout; identical for tiny, base, and
    // small .en because they share one vocabulary.
    public const int Eot = 50256;
    public const int Sot = 50257;
    public const int English = 50258;
    public const int Transcribe = 50358;
    public const int NoTimestamps = 50362;

    public static int[] Prompt => new[] { Sot, English, Transcribe, NoTimestamps };

    /// <summary>Reads vocab.json (token string to id) and inverts it.</summary>
    public static Dictionary<int, string> LoadVocab(string path)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path))
            ?? new Dictionary<string, int>();
        var vocab = new Dictionary<int, string>(raw.Count);
        foreach (var kv in raw)
            vocab[kv.Value] = kv.Key;
        return vocab;
    }

    private static readonly Dictionary<char, byte> CharToByte = BuildCharMap();

    /// <summary>
    /// Exact port of the GPT-2 bytes_to_unicode table, inverted.
    /// </summary>
    private static Dictionary<char, byte> BuildCharMap()
    {
        var bs = new List<int>();
        for (int b = 33; b <= 126; b++) bs.Add(b);
        for (int b = 161; b <= 172; b++) bs.Add(b);
        for (int b = 174; b <= 255; b++) bs.Add(b);
        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (bs.Contains(b)) continue;
            bs.Add(b);
            cs.Add(256 + n);
            n++;
        }
        var map = new Dictionary<char, byte>();
        for (int i = 0; i < bs.Count; i++)
            map[(char)cs[i]] = (byte)bs[i];
        return map;
    }

    /// <summary>
    /// Turns output token ids back into text. Control ids (end of text and
    /// everything from start of transcript up) carry no words and are skipped.
    /// EOT never reaches here because the decode loop stops at it.
    /// </summary>
    public static string Decode(IEnumerable<int> ids, Dictionary<int, string> vocab)
    {
        var bytes = new List<byte>();
        foreach (var id in ids)
        {
            if (id == Eot || id >= Sot) continue;
            if (!vocab.TryGetValue(id, out var token)) continue;
            foreach (var ch in token)
                if (CharToByte.TryGetValue(ch, out var b))
                    bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
