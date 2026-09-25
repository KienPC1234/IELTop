using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace IELTop.Services.Storage;

/// <summary>
/// Pulls plain text out of common study files so the app can turn them into
/// practice content. Kept deliberately small: text only, no OCR, no image
/// processing, so a modest laptop is not loaded down.
/// </summary>
public interface IFileTextExtractor
{
    IReadOnlyList<string> SupportedExtensions { get; }
    IReadOnlyList<string> SupportedImageExtensions { get; }
    bool CanRead(string path);
    bool IsImage(string path);
    string MediaTypeOf(string path);
    Task<FileTextResult> ExtractAsync(string path, CancellationToken ct = default);
}

public sealed record FileTextResult(bool Success, string Text, string Error)
{
    public static FileTextResult Fail(string error) => new(false, string.Empty, error);
}

public sealed class FileTextExtractor : IFileTextExtractor
{
    // Text based formats only. Images need a vision model, handled separately.
    private static readonly string[] Extensions =
        { ".txt", ".md", ".json", ".csv", ".pdf", ".docx" };

    // Image formats a vision model can read.
    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    public IReadOnlyList<string> SupportedExtensions => Extensions;

    public IReadOnlyList<string> SupportedImageExtensions => ImageExtensions;

    public bool CanRead(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return Extensions.Contains(ext);
    }

    public bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    public string MediaTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    public async Task<FileTextResult> ExtractAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return FileTextResult.Fail("The file was not found.");

        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".txt" or ".md" or ".json" or ".csv" => await ReadPlainTextAsync(path, ct),
                ".pdf" => await Task.Run(() => ReadPdf(path), ct),
                ".docx" => await Task.Run(() => ReadDocx(path), ct),
                _ => FileTextResult.Fail($"Unsupported file type: {ext}.")
            };
        }
        catch (OperationCanceledException)
        {
            return FileTextResult.Fail("Reading was cancelled.");
        }
        catch (Exception)
        {
            return FileTextResult.Fail("The file could not be read. It may be damaged or password protected.");
        }
    }

    private static async Task<FileTextResult> ReadPlainTextAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        return string.IsNullOrWhiteSpace(text)
            ? FileTextResult.Fail("The file has no readable text.")
            : new FileTextResult(true, text, string.Empty);
    }

    private static FileTextResult ReadPdf(string path)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
            builder.AppendLine();
        }

        var text = Clean(builder.ToString());
        return string.IsNullOrWhiteSpace(text)
            ? FileTextResult.Fail("No text was found. The PDF may be a scan, which needs OCR and is not supported.")
            : new FileTextResult(true, text, string.Empty);
    }

    /// <summary>
    /// A .docx is a zip. Read word/document.xml and strip the tags, so we avoid
    /// pulling in a full Office library just to get text.
    /// </summary>
    private static FileTextResult ReadDocx(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
            return FileTextResult.Fail("The document has no readable body text.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();

        var withBreaks = xml
            .Replace("</w:p>", "\n", StringComparison.Ordinal)
            .Replace("<w:br/>", "\n", StringComparison.Ordinal);

        var text = Regex.Replace(withBreaks, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Clean(text);

        return string.IsNullOrWhiteSpace(text)
            ? FileTextResult.Fail("The document has no readable text.")
            : new FileTextResult(true, text, string.Empty);
    }

    private static string Clean(string text)
    {
        // Collapse the ragged line breaks and blank lines PDF readers leave behind.
        text = Regex.Replace(text, "[ \t]+", " ");
        text = Regex.Replace(text, "(\r?\n){3,}", "\n\n");
        return text.Trim();
    }
}
