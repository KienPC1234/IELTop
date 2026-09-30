using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Reads plain text from a submitted file: json, txt, md, csv, docx,
/// pdf. Text only, no OCR, no image rendering, so a scanned PDF is
/// reported instead of guessed at. The same rule as the desktop app.
/// </summary>
public static class FileTextExtractor
{
    public static readonly string[] Supported =
        { ".json", ".txt", ".md", ".csv", ".docx", ".pdf" };

    public static bool IsSupported(string path) =>
        Supported.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string SupportedFilter =>
        "Papers and text (*.json;*.txt;*.md;*.csv;*.docx;*.pdf)|*.json;*.txt;*.md;*.csv;*.docx;*.pdf";

    /// <summary>Reads up to limit characters. Never throws.</summary>
    public static bool TryExtract(string path, out string text, out string error, int limit = 400_000)
    {
        text = string.Empty;
        error = string.Empty;
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (!IsSupported(path))
        {
            error = "That file type is not supported. Use json, txt, md, csv, docx, or pdf.";
            return false;
        }

        try
        {
            text = ext switch
            {
                ".json" or ".txt" or ".md" or ".csv" => File.ReadAllText(path),
                ".docx" => ReadDocx(path),
                ".pdf" => ReadPdf(path),
                _ => string.Empty
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            text = string.Empty;
            error = e is InvalidDataException
                ? "The file is too large or malformed."
                : "The file could not be read.";
            return false;
        }

        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            error = ext == ".pdf"
                ? "No text was found in this PDF. Scanned PDFs are not supported."
                : "The file holds no readable text.";
            return false;
        }

        if (ext == ".pdf" && text.Length < 50)
        {
            error = "No text was found in this PDF. Scanned PDFs are not supported.";
            return false;
        }

        if (text.Length > limit)
            text = text[..limit];
        return true;
    }

    private static string ReadDocx(string path)
    {
        // A docx is a zip whose word/document.xml holds the text. No
        // extra package needed, and the same approach as the app. The
        // read is capped because a small zip can expand to gigabytes.
        const int maxXml = 4_000_000;

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null)
            return string.Empty;
        if (entry.Length > maxXml)
            throw new InvalidDataException("The document is too large to read.");

        using var stream = entry.Open();
        var buffer = new char[8192];
        var sb = new StringBuilder();
        using var reader = new StreamReader(stream);
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length > maxXml)
                throw new InvalidDataException("The document is too large to read.");
        }

        string xml = sb.ToString();
        xml = Regex.Replace(xml, @"</w:p[^>]*>", "\n", RegexOptions.IgnoreCase);
        xml = Regex.Replace(xml, @"<w:tab[^>]*>", "\t", RegexOptions.IgnoreCase);
        xml = Regex.Replace(xml, @"<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(xml);
    }

    private static string ReadPdf(string path)
    {
        // Best effort text layer read without a PDF library. Visible
        // strings sit in (text) Tj or <hex> Tj. A scan has neither.
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            return string.Empty;

        string raw = Encoding.Latin1.GetString(bytes);
        var sb = new StringBuilder(raw.Length / 4);

        foreach (Match m in Regex.Matches(raw, @"\((?:\\.|[^\\()])*\)"))
        {
            string inner = m.Value[1..^1];
            inner = inner.Replace("\\n", "\n").Replace("\\r", "\n").Replace("\\t", "\t");
            inner = Regex.Replace(inner, @"\\(.)", "$1");
            if (inner.Trim().Length > 0)
                sb.Append(inner).Append(' ');
            if (sb.Length > 250_000)
                break;
        }

        foreach (Match m in Regex.Matches(raw, @"<([0-9A-Fa-f]{4,})>"))
        {
            try
            {
                string hex = m.Groups[1].Value;
                if (hex.Length % 2 == 1)
                    hex += "0";
                var buffer = new byte[hex.Length / 2];
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                string decoded = Encoding.BigEndianUnicode.GetString(buffer);
                sb.Append(string.IsNullOrWhiteSpace(decoded)
                    ? Encoding.Latin1.GetString(buffer)
                    : decoded).Append(' ');
            }
            catch (FormatException)
            {
                // Skip a malformed hex run.
            }
            if (sb.Length > 250_000)
                break;
        }

        return Regex.Replace(sb.ToString(), @"[ \t]+", " ")
            .Replace(" \n", "\n")
            .Trim();
    }
}
