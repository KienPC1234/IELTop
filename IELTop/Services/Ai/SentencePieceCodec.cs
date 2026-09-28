using System.IO;
using System.Text;

namespace IELTop.Services.Ai;

/// <summary>
/// Minimal SentencePiece unigram codec for T5: parses spiece.model,
/// encodes with Viterbi best-path, and decodes pieces back to text.
/// Only what grammar checking needs is implemented.
/// </summary>
public sealed class SentencePieceCodec
{
    public sealed record Piece(string Text, float Score);

    private readonly List<Piece> _pieces;
    private readonly Dictionary<string, int> _index;

    private SentencePieceCodec(List<Piece> pieces)
    {
        _pieces = pieces;
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pieces.Count; i++)
            if (!_index.ContainsKey(pieces[i].Text))
                _index[pieces[i].Text] = i;
    }

    public int Count => _pieces.Count;

    public static SentencePieceCodec Load(string path)
        => new(ParseModel(File.ReadAllBytes(path)));

    public int IdOf(string piece) => _index.TryGetValue(piece, out var id) ? id : 2; // <unk>

    /// <summary>
    /// Encodes with a dummy prefix, like the reference tokenizer, and
    /// appends EOS (id 1). Output is capped at maxTokens pieces.
    /// </summary>
    public int[] Encode(string text, int maxTokens = 128)
    {
        var s = "▁" + text.Replace(" ", "▁");
        int n = s.Length;
        var best = new double[n + 1];
        var from = new int[n + 1];
        var pieceId = new int[n + 1];
        for (int i = 1; i <= n; i++) best[i] = double.NegativeInfinity;
        best[0] = 0;
        from[0] = -1;

        for (int i = 0; i < n; i++)
        {
            if (double.IsNegativeInfinity(best[i])) continue;
            bool matched = false;
            for (int len = 1; len <= Math.Min(32, n - i); len++)
            {
                var sub = s.Substring(i, len);
                if (!_index.TryGetValue(sub, out var id)) continue;
                double score = best[i] + _pieces[id].Score;
                if (score > best[i + len])
                {
                    best[i + len] = score;
                    from[i + len] = i;
                    pieceId[i + len] = id;
                }
                matched = true;
            }
            if (matched) continue;
            // Byte fallback covers exactly one character.
            var bytes = Encoding.UTF8.GetBytes(s[i].ToString());
            bool ok = true;
            foreach (var b in bytes)
                if (!_index.ContainsKey($"<0x{b:X2}>")) { ok = false; break; }
            if (ok)
            {
                double score = best[i] - 10.0 * bytes.Length;
                if (score > best[i + 1])
                {
                    best[i + 1] = score;
                    from[i + 1] = i;
                    pieceId[i + 1] = -2;
                }
            }
        }

        if (double.IsNegativeInfinity(best[n]))
            return new[] { 1 }; // EOS only, input was not encodable.

        var ids = new List<int>();
        for (int at = n; at > 0;)
        {
            int start = from[at];
            if (start < 0) break;
            if (pieceId[at] == -2)
            {
                foreach (var b in Encoding.UTF8.GetBytes(s[start].ToString()))
                    ids.Add(IdOf($"<0x{b:X2}>"));
            }
            else
            {
                ids.Add(pieceId[at]);
            }
            at = start;
        }
        ids.Reverse();
        if (ids.Count > maxTokens - 1)
            ids = ids.Take(maxTokens - 1).ToList();
        ids.Add(1); // EOS
        return ids.ToArray();
    }

    /// <summary>Joins pieces, turns word markers back into spaces.</summary>
    public string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == 0 || id == 1) continue; // pad and EOS carry no text.
            if (id < 0 || id >= _pieces.Count) continue;
            sb.Append(_pieces[id].Text);
        }
        return sb.ToString().Replace("▁", " ").Trim();
    }

    private static List<Piece> ParseModel(byte[] data)
    {
        var pieces = new List<Piece>();
        int pos = 0;
        while (pos < data.Length)
        {
            ulong tag = ReadVarint(data, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                int len = (int)ReadVarint(data, ref pos);
                ParsePiece(data, pos, len, pieces);
                pos += len;
            }
            else
            {
                pos = Skip(data, pos, wire);
            }
        }
        return pieces;
    }

    private static void ParsePiece(byte[] data, int start, int len, List<Piece> into)
    {
        string? text = null;
        float score = 0;
        int pos = start, end = start + len;
        while (pos < end)
        {
            ulong tag = ReadVarint(data, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                int slen = (int)ReadVarint(data, ref pos);
                text = Encoding.UTF8.GetString(data, pos, slen);
                pos += slen;
            }
            else if (field == 2 && wire == 5)
            {
                score = BitConverter.ToSingle(data, pos);
                pos += 4;
            }
            else
            {
                pos = Skip(data, pos, wire);
            }
        }
        if (text is not null)
            into.Add(new Piece(text, score));
    }

    private static ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong value = 0;
        int shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return value;
    }

    private static int Skip(byte[] data, int pos, int wire) => wire switch
    {
        0 => SkipVarint(data, pos),
        1 => pos + 8,
        2 => pos + (int)ReadVarint(data, ref pos),
        5 => pos + 4,
        _ => pos + 1,
    };

    private static int SkipVarint(byte[] data, int pos)
    {
        while (pos < data.Length && (data[pos++] & 0x80) != 0) { }
        return pos;
    }
}
