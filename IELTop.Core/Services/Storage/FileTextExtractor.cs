using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IELTop.Services.Storage;

/// <summary>
/// Reads plain text from user files: txt, md, json, csv, docx, pdf.
/// Text only, no images, no OCR. A scanned PDF has no text layer,
/// so it reports that case instead of failing silently.
/// </summary>
public static class FileTextExtractor
{
    private static readonly string[] Supported =
        { ".txt", ".md", ".json", ".csv", ".docx", ".pdf" };

    public static bool IsSupported(string path)
        => Supported.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string SupportedFilter
        => "Text and papers (*.json;*.txt;*.md;*.csv;*.docx;*.pdf)|*.json;*.txt;*.md;*.csv;*.docx;*.pdf";

    public static bool TryExtract(string path, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            text = ext switch
            {
                ".txt" or ".md" or ".json" or ".csv" => File.ReadAllText(path),
                ".docx" => ReadDocx(path),
                ".pdf" => ReadPdf(path),
                _ => string.Empty
            };
            if (ext is not (".txt" or ".md" or ".json" or ".csv" or ".docx" or ".pdf"))
            {
                error = "That file type is not supported. Use json, txt, md, csv, docx, or pdf.";
                return false;
            }
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = ext == ".pdf"
                    ? "No text found in this PDF. Scanned PDFs with only images are not supported."
                    : "The file holds no readable text.";
                return false;
            }
            if (text.Length > 200_000)
                text = text[..200_000];
            // A tiny PDF extract is almost always a scan or a broken file.
            if (ext == ".pdf" && text.Length < 50)
            {
                error = "No text found in this PDF. Scanned PDFs with only images are not supported.";
                return false;
            }
            return true;
        }
        catch (Exception)
        {
            text = string.Empty;
            error = "Could not read that file. Check it is not open elsewhere.";
            return false;
        }
    }

    private static string ReadDocx(string path)
    {
        // docx is a zip with word/document.xml inside. No extra package needed.
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return string.Empty;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();
        // Keep paragraph and tab breaks, drop the rest of the tags.
        xml = Regex.Replace(xml, @"</w:p[^>]*>", "\n", RegexOptions.IgnoreCase);
        xml = Regex.Replace(xml, @"<w:tab[^>]*>", "\t", RegexOptions.IgnoreCase);
        xml = Regex.Replace(xml, @"<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(xml);
    }

    private static string ReadPdf(string path)
    {
        // Best effort text layer read without a PDF library:
        // most text PDFs store visible strings as (text) Tj or <hex> Tj.
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return string.Empty;
        var raw = Encoding.Latin1.GetString(bytes);
        var sb = new StringBuilder(raw.Length / 4);

        foreach (Match m in Regex.Matches(raw, @"\((?:\\.|[^\\()])*\)"))
        {
            var inner = m.Value[1..^1];
            inner = inner.Replace("\\n", "\n").Replace("\\r", "\n").Replace("\\t", "\t");
            inner = Regex.Replace(inner, @"\\(.)", "$1");
            if (inner.Trim().Length > 0)
                sb.Append(inner).Append(' ');
            if (sb.Length > 250_000) break;
        }
        foreach (Match m in Regex.Matches(raw, @"<([0-9A-Fa-f]{4,})>"))
        {
            try
            {
                var hex = m.Groups[1].Value;
                if (hex.Length % 2 == 1) hex += "0";
                var buf = new byte[hex.Length / 2];
                for (int i = 0; i < buf.Length; i++)
                    buf[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                var decoded = Encoding.BigEndianUnicode.GetString(buf);
                if (!string.IsNullOrWhiteSpace(decoded))
                    sb.Append(decoded).Append(' ');
                else
                    sb.Append(Encoding.Latin1.GetString(buf)).Append(' ');
            }
            catch (FormatException) { }
            if (sb.Length > 250_000) break;
        }
        return Regex.Replace(sb.ToString(), @"[ \t]+", " ")
            .Replace(" \n", "\n").Trim();
    }
}
