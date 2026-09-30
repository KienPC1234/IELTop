using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// What one paper lists as, without its questions. Field names match
/// the ieltop/1 protocol, which the WPF client reads case insensitively.
/// </summary>
public sealed class PaperSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = new();
    public int Parts { get; set; }
    public int Questions { get; set; }
    public string Updated { get; set; } = string.Empty;
    public long Size { get; set; }
}

/// <summary>
/// A paper plus the fields the list and the admin table need, derived
/// from the raw JSON. The raw JSON is kept whole, never rebuilt from a
/// narrow model, so nothing a paper carries is ever dropped.
/// </summary>
public sealed record ParsedPaper(
    string Title,
    string Category,
    string Level,
    string Source,
    List<string> Tags,
    List<string> Skills,
    List<string> AudioFiles,
    int PartCount,
    int QuestionCount,
    string Json);

/// <summary>
/// Every read and write of paper content. Both the portal and the
/// protocol go through this, so caching and version bumps happen in
/// exactly one place.
/// </summary>
public interface IPaperService
{
    Task<(bool Ok, string Error, ParsedPaper? Paper)> ParseAsync(Stream json, CancellationToken ct = default);
    Task<List<PaperSummary>> ListAsync(string? skill, string? category, string? query, CancellationToken ct = default);
    Task<string?> GetJsonAsync(string id, CancellationToken ct = default);
    Task<List<ExamPaper>> AllForAdminAsync(string? query, CancellationToken ct = default);
    Task<ExamPaper?> GetAsync(string id, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<int> ImportFolderAsync(string folder, bool overwrite, CancellationToken ct = default);
    Task<(bool Ok, string Error)> SaveAsync(string id, ParsedPaper paper, bool overwrite, CancellationToken ct = default);
    Task<(bool Ok, string Error)> SaveRawAsync(string id, string json, bool overwrite, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<bool> SetPublishedAsync(string id, bool published, CancellationToken ct = default);
    Task RecordDownloadAsync(string id, CancellationToken ct = default);
    Task RebuildSummariesAsync(CancellationToken ct = default);
}

public sealed class PaperService(
    IDbContextFactory<AppDbContext> dbFactory,
    IContentCache cache,
    IWriteThrottle throttle,
    IOptions<CacheOptions> cacheOptions,
    ILogger<PaperService> logger) : IPaperService
{
    private readonly CacheOptions _cache = cacheOptions.Value;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions SummaryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions SummaryRead = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<(bool Ok, string Error, ParsedPaper? Paper)> ParseAsync(Stream json, CancellationToken ct = default)
    {
        try
        {
            using var reader = new StreamReader(json, Encoding.UTF8, leaveOpen: true);
            string text = await reader.ReadToEndAsync(ct);
            return ParseText(text);
        }
        catch (JsonException)
        {
            return (false, "The file is not valid JSON.", null);
        }
        catch (IOException)
        {
            return (false, "The file could not be read.", null);
        }
    }

    /// <summary>
    /// Reads a paper as a document and pulls out the derived fields by
    /// name. The stored JSON is the same document with stable
    /// formatting, so every unknown field survives a round trip.
    /// </summary>
    public static (bool Ok, string Error, ParsedPaper? Paper) ParseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (false, "The file is empty.", null);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException)
        {
            return (false, "The file is not valid JSON.", null);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (false, "The paper must be a JSON object.", null);

            string title = ReadString(root, "title");
            if (title.Length == 0)
                return (false, "The paper needs a title.", null);

            if (!TryRead(root, "parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                return (false, "The paper needs at least one part.", null);
            if (parts.GetArrayLength() == 0)
                return (false, "The paper needs at least one part.", null);

            var skills = new List<string>();
            var audio = new List<string>();
            int questions = 0;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                    continue;

                string skill = ReadString(part, "skill");
                if (skill.Length > 0 && !skills.Contains(skill, StringComparer.OrdinalIgnoreCase))
                    skills.Add(skill);

                string clip = ReadString(part, "audioFile");
                if (clip.Length > 0 && !audio.Contains(clip, StringComparer.OrdinalIgnoreCase))
                    audio.Add(clip);

                if (TryRead(part, "questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
                    questions += qs.GetArrayLength();
            }

            skills.Sort(StringComparer.OrdinalIgnoreCase);
            audio.Sort(StringComparer.OrdinalIgnoreCase);

            var tags = new List<string>();
            if (TryRead(root, "tags", out var tagElement) && tagElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagElement.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String)
                    {
                        string value = (tag.GetString() ?? string.Empty).Trim();
                        if (value.Length > 0 && !tags.Contains(value, StringComparer.OrdinalIgnoreCase))
                            tags.Add(value);
                    }
                }
            }

            string canonical = JsonSerializer.Serialize(root, CanonicalJson);

            var parsed = new ParsedPaper(
                title,
                ReadString(root, "category"),
                ReadString(root, "level"),
                ReadString(root, "source"),
                tags,
                skills,
                audio,
                parts.GetArrayLength(),
                questions,
                canonical);

            return (true, string.Empty, parsed);
        }
    }

    /// <summary>
    /// Property lookup that accepts either casing, since files in the
    /// wild are not consistent about it.
    /// </summary>
    private static bool TryRead(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string ReadString(JsonElement element, string name) =>
        TryRead(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? string.Empty).Trim()
            : string.Empty;

    public async Task<List<PaperSummary>> ListAsync(
        string? skill, string? category, string? query, CancellationToken ct = default)
    {
        long version = await cache.VersionAsync(ct);
        string key = $"v{version}:papers";

        string json = await cache.GetOrAddStringAsync(
            key,
            TimeSpan.FromSeconds(Math.Max(5, _cache.PaperListSeconds)),
            async token => JsonSerializer.Serialize(
                await LoadSummariesAsync(token), SummaryJson),
            ct);

        var all = JsonSerializer.Deserialize<List<PaperSummary>>(json, SummaryRead) ?? new();
        IEnumerable<PaperSummary> filtered = all;

        if (!string.IsNullOrWhiteSpace(skill))
            filtered = filtered.Where(p =>
                p.Skills.Any(s => s.Equals(skill.Trim(), StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(category))
            filtered = filtered.Where(p =>
                p.Category.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
            filtered = filtered.Where(p =>
                p.Title.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

        return filtered.ToList();
    }

    private async Task<List<PaperSummary>> LoadSummariesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Papers
            .Where(p => p.IsPublished)
            .OrderBy(p => p.Title)
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.Select(ToSummary).ToList();
    }

    private static PaperSummary ToSummary(ExamPaper row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Category = row.Category,
        Level = row.Level,
        Skills = Deserialize(row.SkillsJson),
        Parts = row.PartCount,
        Questions = row.QuestionCount,
        Updated = row.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd"),
        Size = row.SizeBytes
    };

    public async Task<string?> GetJsonAsync(string id, CancellationToken ct = default)
    {
        long version = await cache.VersionAsync(ct);
        string key = $"v{version}:paper:{id}";

        string json = await cache.GetOrAddStringAsync(
            key,
            TimeSpan.FromSeconds(Math.Max(5, _cache.PaperDetailSeconds)),
            async token =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token);
                var row = await db.Papers.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id && p.IsPublished, token);
                return row?.Json ?? string.Empty;
            },
            ct);

        return json.Length > 0 ? json : null;
    }

    public async Task<ExamPaper?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<List<ExamPaper>> AllForAdminAsync(string? query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = db.Papers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(p => p.Title.Contains(query) || p.Id.Contains(query));
        return await rows.OrderByDescending(p => p.UpdatedAt).ToListAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.CountAsync(ct);
    }

    public async Task<int> ImportFolderAsync(string folder, bool overwrite, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder))
            return 0;

        int imported = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderBy(p => p))
        {
            ct.ThrowIfCancellationRequested();
            string id = SafeId(Path.GetFileNameWithoutExtension(path));
            if (id.Length == 0)
                continue;

            await using var stream = File.OpenRead(path);
            var (ok, _, paper) = await ParseAsync(stream, ct);
            if (!ok || paper is null)
            {
                logger.LogWarning("Skipped seed file {File}", path);
                continue;
            }

            var (saved, _) = await SaveAsync(id, paper, overwrite, ct);
            if (saved)
                imported++;
        }
        return imported;
    }

    public async Task<(bool Ok, string Error)> SaveAsync(
        string id, ParsedPaper paper, bool overwrite, CancellationToken ct = default)
    {
        id = SafeId(id);
        if (id.Length == 0)
            return (false, "A paper id is required.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Papers.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (existing is not null && !overwrite)
            return (false, "A paper with this id already exists.");

        byte[] bytes = Encoding.UTF8.GetBytes(paper.Json);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (existing is null)
        {
            existing = new ExamPaper { Id = id, CreatedAt = DateTimeOffset.UtcNow };
            db.Papers.Add(existing);
        }

        string title = paper.Title.Length > 300 ? paper.Title[..300] : paper.Title;
        existing.Title = title;
        existing.Category = paper.Category;
        existing.Level = paper.Level;
        existing.Source = paper.Source;
        existing.TagsJson = JsonSerializer.Serialize(paper.Tags);
        existing.SkillsJson = JsonSerializer.Serialize(paper.Skills);
        existing.AudioFilesJson = JsonSerializer.Serialize(paper.AudioFiles);
        existing.PartCount = paper.PartCount;
        existing.QuestionCount = paper.QuestionCount;
        existing.SizeBytes = bytes.Length;
        existing.Sha256 = hash;
        existing.Json = paper.Json;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
        return (true, string.Empty);
    }

    /// <summary>
    /// Saves the exact text the admin pasted, after validating it
    /// parses as a paper. This is the path the editor uses so nothing
    /// is reformatted behind the admin's back.
    /// </summary>
    public async Task<(bool Ok, string Error)> SaveRawAsync(
        string id, string json, bool overwrite, CancellationToken ct = default)
    {
        var (ok, error, parsed) = ParseText(json);
        if (!ok || parsed is null)
            return (false, error);

        // Cap the stored paper so one huge paste cannot fill the store.
        const int maxJson = 2_000_000;
        if (json.Length > maxJson)
            return (false, "The paper is too large. Keep it under 2 MB of JSON.");

        var (saved, saveError) = await SaveAsync(id, parsed with { Json = json.Trim() }, overwrite, ct);
        return saved ? (true, string.Empty) : (false, saveError);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Papers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
            return false;
        db.Papers.Remove(row);
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
        return true;
    }

    public async Task<bool> SetPublishedAsync(string id, bool published, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Papers.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null)
            return false;
        row.IsPublished = published;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
        return true;
    }

    public async Task RecordDownloadAsync(string id, CancellationToken ct = default)
    {
        if (!throttle.ShouldWrite($"paper.dl:{id}", TimeSpan.FromMinutes(1)))
            return;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Papers
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.DownloadCount, p => p.DownloadCount + 1), ct);
        }
        catch (Exception)
        {
            // Counting a download is never worth failing the download.
        }
    }

    public async Task RebuildSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Papers.ToListAsync(ct);
        foreach (var row in rows)
        {
            var (ok, _, paper) = ParseText(row.Json);
            if (!ok || paper is null)
                continue;
            row.Title = paper.Title;
            row.Category = paper.Category;
            row.Level = paper.Level;
            row.Source = paper.Source;
            row.TagsJson = JsonSerializer.Serialize(paper.Tags);
            row.SkillsJson = JsonSerializer.Serialize(paper.Skills);
            row.AudioFilesJson = JsonSerializer.Serialize(paper.AudioFiles);
            row.PartCount = paper.PartCount;
            row.QuestionCount = paper.QuestionCount;
            row.SizeBytes = Encoding.UTF8.GetByteCount(row.Json);
        }
        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);
    }

    internal static List<string> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>
    /// A paper id is also a url segment. Keep it to letters, digits,
    /// dash, and underscore so it cannot escape anywhere, and cap the
    /// length so a crafted id cannot fill the store.
    /// </summary>
    public static string SafeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var builder = new StringBuilder(raw.Length);
        foreach (char c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
                builder.Append(c);
            else if (c is ' ')
                builder.Append('-');
            if (builder.Length >= 80)
                break;
        }
        return builder.ToString();
    }
}
