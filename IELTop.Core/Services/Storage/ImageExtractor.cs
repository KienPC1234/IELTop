using System.IO;
using System.IO.Compression;

namespace IELTop.Services.Storage;

/// <summary>
/// One picture pulled out of a file, ready to send to a vision model.
/// </summary>
public sealed record ImportedImage(string Name, string Base64, string MediaType);

/// <summary>
/// Image input for vision models: plain image files, pictures embedded in
/// docx files, and photos carved out of PDFs. Text only PDFs have no
/// pictures, and scanned PDFs keep each page as a photo, usually JPEG.
/// No OCR here. Reading the pictures is the vision model job.
/// </summary>
public static class ImageExtractor
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }

    public static string ImageFilter
        => "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";

    public static string VisionSourceFilter
        => "Images, PDF, Word (*.png;*.jpg;*.jpeg;*.pdf;*.docx)|*.png;*.jpg;*.jpeg;*.pdf;*.docx";

    /// <summary>Pulls every usable picture out of an image, pdf, or docx file.</summary>
    public static bool TryExtract(string path, out IReadOnlyList<ImportedImage> images, out string error)
    {
        images = Array.Empty<ImportedImage>();
        error = string.Empty;
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var found = ext switch
            {
                ".png" or ".jpg" or ".jpeg" => ReadSingleImage(path),
                ".docx" => ReadDocxImages(path),
                ".pdf" => CarvePdfPhotos(path),
                _ => new List<ImportedImage>()
            };
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".docx" or ".pdf"))
            {
                error = "That file type is not supported. Use png, jpg, pdf, or docx.";
                return false;
            }
            if (found.Count == 0)
            {
                error = ext == ".pdf"
                    ? "No photos found in this PDF. It may hold text only, import it as a text file instead."
                    : ext == ".docx"
                        ? "No pictures found in this Word file. Import it as a text file instead."
                        : "Could not read that image.";
                return false;
            }
            images = found.Take(6).ToList();
            if (found.Count > 6)
                error = $"Found {found.Count} pictures, only the first 6 will be read.";
            return true;
        }
        catch (Exception)
        {
            images = Array.Empty<ImportedImage>();
            error = "Could not read that file. Check it is not open elsewhere.";
            return false;
        }
    }

    private static List<ImportedImage> ReadSingleImage(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length > 12_000_000) return new();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return new List<ImportedImage>
        {
            new(Path.GetFileName(path), Convert.ToBase64String(bytes),
                ext == ".png" ? "image/png" : "image/jpeg")
        };
    }

    private static List<ImportedImage> ReadDocxImages(string path)
    {
        var found = new List<ImportedImage>();
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
                continue;
            var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg")) continue;
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length == 0 || bytes.Length > 12_000_000) continue;
            found.Add(new(entry.Name, Convert.ToBase64String(bytes),
                ext == ".png" ? "image/png" : "image/jpeg"));
        }
        return found;
    }

    /// <summary>
    /// Scanned PDFs store each page as a photo, most often JPEG with clear
    /// start and end markers. Carves those photos straight out of the file.
    /// Vector drawings and PNG photo streams are skipped.
    /// </summary>
    private static List<ImportedImage> CarvePdfPhotos(string path)
    {
        var found = new List<ImportedImage>();
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes.Length > 120_000_000) return found;
        int index = 0;
        int page = 0;
        while (index < bytes.Length - 4 && page < 20)
        {
            int start = FindMarker(bytes, index);
            if (start < 0) break;
            int end = FindEndMarker(bytes, start + 2);
            if (end < 0) break;
            int length = end + 2 - start;
            if (length > 4_000)
            {
                page++;
                var slice = new byte[length];
                Buffer.BlockCopy(bytes, start, slice, 0, length);
                found.Add(new($"page-photo-{page}.jpg",
                    Convert.ToBase64String(slice), "image/jpeg"));
            }
            index = end + 2;
        }
        return found;
    }

    private static int FindMarker(byte[] bytes, int from)
    {
        for (int i = from; i < bytes.Length - 1; i++)
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8)
                return i;
        return -1;
    }

    private static int FindEndMarker(byte[] bytes, int from)
    {
        for (int i = from; i < bytes.Length - 1; i++)
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD9)
                return i;
        return -1;
    }
}
