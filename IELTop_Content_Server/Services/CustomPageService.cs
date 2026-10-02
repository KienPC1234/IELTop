using Markdig;
using Microsoft.Extensions.Caching.Memory;

namespace IELTop_Content_Server.Services;

public sealed record CustomPage(
    string Slug,
    string BaseSlug,
    string Language,
    string Title,
    string Description,
    string HtmlContent,
    DateTimeOffset LastModified,
    bool HasVietnamese = false,
    bool HasEnglish = false);

public sealed record CustomPageSummary(
    string Slug,
    string BaseSlug,
    string Language,
    string Title,
    string Description,
    DateTimeOffset LastModified);

public interface ICustomPageService
{
    Task<CustomPage?> GetPageAsync(string slug, string? preferredLang = null, CancellationToken ct = default);
    Task<List<CustomPageSummary>> ListPagesAsync(string? preferredLang = null, CancellationToken ct = default);
}

public sealed class CustomPageService : ICustomPageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CustomPageService> _logger;
    private readonly MarkdownPipeline _pipeline;
    private readonly string _contentPagesDir;
    private readonly string _appDataPagesDir;

    public CustomPageService(
        IWebHostEnvironment env,
        IMemoryCache cache,
        ILogger<CustomPageService> logger)
    {
        _env = env;
        _cache = cache;
        _logger = logger;

        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseBootstrap()
            .Build();

        _contentPagesDir = Path.Combine(_env.ContentRootPath, "Content", "Pages");
        _appDataPagesDir = Path.Combine(_env.ContentRootPath, "App_Data", "Pages");

        Directory.CreateDirectory(_contentPagesDir);
        Directory.CreateDirectory(_appDataPagesDir);
    }

    public static (string BaseSlug, string? SuffixLang) ParseSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return (string.Empty, null);

        string clean = slug.Trim();
        if (clean.EndsWith("_vi", StringComparison.OrdinalIgnoreCase))
            return (clean[..^3], "vi");
        if (clean.EndsWith(".vi", StringComparison.OrdinalIgnoreCase))
            return (clean[..^3], "vi");
        if (clean.EndsWith("_en", StringComparison.OrdinalIgnoreCase))
            return (clean[..^3], "en");
        if (clean.EndsWith(".en", StringComparison.OrdinalIgnoreCase))
            return (clean[..^3], "en");

        return (clean, null);
    }

    public async Task<CustomPage?> GetPageAsync(string slug, string? preferredLang = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var (baseSlug, suffixLang) = ParseSlug(slug);
        string sanitizedBase = SanitizeSlug(baseSlug);
        if (string.IsNullOrEmpty(sanitizedBase))
            return null;

        string effectiveLang = (suffixLang ?? preferredLang ?? "en").Trim().ToLowerInvariant();
        if (effectiveLang != "vi" && effectiveLang != "en")
            effectiveLang = "en";

        string cacheKey = $"custom_page:{sanitizedBase}:{effectiveLang}";

        if (_cache.TryGetValue(cacheKey, out CustomPage? cachedPage) && cachedPage is not null)
            return cachedPage;

        var (filePath, matchedLang, hasVi, hasEn) = ResolvePageFile(sanitizedBase, effectiveLang);
        if (filePath is null || !File.Exists(filePath))
            return null;

        try
        {
            string markdown = await File.ReadAllTextAsync(filePath, ct);
            var (title, description, bodyMarkdown) = ParseMetadata(markdown, sanitizedBase);
            string html = Markdown.ToHtml(bodyMarkdown, _pipeline);

            var page = new CustomPage(
                Slug: $"{sanitizedBase}{(matchedLang == "vi" ? "_vi" : "")}",
                BaseSlug: sanitizedBase,
                Language: matchedLang,
                Title: title,
                Description: description,
                HtmlContent: html,
                LastModified: File.GetLastWriteTimeUtc(filePath),
                HasVietnamese: hasVi,
                HasEnglish: hasEn);

            _cache.Set(cacheKey, page, TimeSpan.FromMinutes(5));
            return page;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load custom markdown page for slug: {Slug} (lang: {Lang})", sanitizedBase, effectiveLang);
            return null;
        }
    }

    public Task<List<CustomPageSummary>> ListPagesAsync(string? preferredLang = null, CancellationToken ct = default)
    {
        string lang = (preferredLang ?? "en").Trim().ToLowerInvariant();
        var summaries = new Dictionary<string, CustomPageSummary>(StringComparer.OrdinalIgnoreCase);

        var directories = new[] { _contentPagesDir, _appDataPagesDir };
        var uniqueBaseSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.GetFiles(dir, "*.md"))
            {
                string rawName = Path.GetFileNameWithoutExtension(file);
                var (baseSlug, _) = ParseSlug(rawName);
                if (!string.IsNullOrEmpty(baseSlug))
                {
                    uniqueBaseSlugs.Add(SanitizeSlug(baseSlug));
                }
            }
        }

        foreach (var baseSlug in uniqueBaseSlugs)
        {
            var (filePath, matchedLang, _, _) = ResolvePageFile(baseSlug, lang);
            if (filePath is null || !File.Exists(filePath))
                continue;

            try
            {
                string firstLines = string.Join('\n', File.ReadLines(filePath).Take(10));
                var (title, description, _) = ParseMetadata(firstLines, baseSlug);
                summaries[baseSlug] = new CustomPageSummary(
                    Slug: $"{baseSlug}{(matchedLang == "vi" ? "_vi" : "")}",
                    BaseSlug: baseSlug,
                    Language: matchedLang,
                    Title: title,
                    Description: description,
                    LastModified: File.GetLastWriteTimeUtc(filePath));
            }
            catch
            {
                summaries[baseSlug] = new CustomPageSummary(
                    Slug: baseSlug,
                    BaseSlug: baseSlug,
                    Language: matchedLang,
                    Title: baseSlug,
                    Description: string.Empty,
                    LastModified: File.GetLastWriteTimeUtc(filePath));
            }
        }

        return Task.FromResult(summaries.Values.OrderBy(s => s.Title).ToList());
    }

    private (string? FilePath, string MatchedLang, bool HasVi, bool HasEn) ResolvePageFile(string baseSlug, string preferredLang)
    {
        string? viFile = FindFileVariant(baseSlug, "_vi") ?? FindFileVariant(baseSlug, ".vi");
        string? enFile = FindFileVariant(baseSlug, "_en") ?? FindFileVariant(baseSlug, ".en") ?? FindFileVariant(baseSlug, "");

        bool hasVi = viFile is not null;
        bool hasEn = enFile is not null;

        if (preferredLang == "vi")
        {
            if (viFile is not null)
                return (viFile, "vi", hasVi, hasEn);
            if (enFile is not null)
                return (enFile, "en", hasVi, hasEn);
        }
        else
        {
            if (enFile is not null)
                return (enFile, "en", hasVi, hasEn);
            if (viFile is not null)
                return (viFile, "vi", hasVi, hasEn);
        }

        return (null, preferredLang, hasVi, hasEn);
    }

    private string? FindFileVariant(string baseSlug, string suffix)
    {
        string fileName = string.IsNullOrEmpty(suffix) ? $"{baseSlug}.md" : $"{baseSlug}{suffix}.md";

        // 1. Check user App_Data/Pages first (allows overrides without redeploy)
        string appDataPath = Path.Combine(_appDataPagesDir, fileName);
        if (File.Exists(appDataPath))
            return appDataPath;

        // 2. Check shipped Content/Pages
        string contentPath = Path.Combine(_contentPagesDir, fileName);
        if (File.Exists(contentPath))
            return contentPath;

        return null;
    }

    private static string SanitizeSlug(string slug)
    {
        var allowed = slug.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray();
        return new string(allowed).ToLowerInvariant();
    }

    private static (string Title, string Description, string BodyMarkdown) ParseMetadata(string raw, string fallbackTitle)
    {
        string title = fallbackTitle;
        string description = string.Empty;
        var lines = raw.Split('\n');
        int contentStart = 0;

        // Check if frontmatter exists: starts with ---
        if (lines.Length > 2 && lines[0].Trim() == "---")
        {
            int closingIndex = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    closingIndex = i;
                    break;
                }

                var line = lines[i].Trim();
                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    title = line["title:".Length..].Trim().Trim('"').Trim('\'');
                }
                else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                {
                    description = line["description:".Length..].Trim().Trim('"').Trim('\'');
                }
            }

            if (closingIndex > 0)
                contentStart = closingIndex + 1;
        }

        // If title was not found in frontmatter, find the first # Heading
        if (title == fallbackTitle)
        {
            for (int i = contentStart; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith('#') && !trimmed.StartsWith("##"))
                {
                    title = trimmed.TrimStart('#').Trim();
                    break;
                }
            }
        }

        string bodyMarkdown = string.Join('\n', lines.Skip(contentStart));
        return (title, description, bodyMarkdown);
    }
}
