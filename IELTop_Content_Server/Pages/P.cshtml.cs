using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages;

public sealed class PModel(
    ICustomPageService customPageService,
    ILogger<PModel> logger) : PageModel
{
    public CustomPage? PageItem { get; private set; }
    public string CurrentLang { get; private set; } = "en";

    public async Task<IActionResult> OnGetAsync(string? slug, [FromQuery] string? lang, CancellationToken ct)
    {
        string targetSlug = string.IsNullOrWhiteSpace(slug) ? "pro" : slug.Trim();
        CurrentLang = ResolveLanguage(targetSlug, lang);

        PageItem = await customPageService.GetPageAsync(targetSlug, CurrentLang, ct);

        if (PageItem is null)
        {
            logger.LogInformation("Custom page with slug '{Slug}' (lang: {Lang}) was not found", targetSlug, CurrentLang);
            return NotFound();
        }

        CurrentLang = PageItem.Language;
        ViewData["Title"] = PageItem.Title;
        if (!string.IsNullOrWhiteSpace(PageItem.Description))
        {
            ViewData["Description"] = PageItem.Description;
        }

        return Page();
    }

    private string ResolveLanguage(string slug, string? queryLang)
    {
        // 1. Explicit slug suffix overrides everything
        var (_, suffixLang) = CustomPageService.ParseSlug(slug);
        if (!string.IsNullOrEmpty(suffixLang))
            return suffixLang;

        // 2. Query param ?lang=vi or ?lang=en
        if (string.Equals(queryLang, "vi", StringComparison.OrdinalIgnoreCase))
            return "vi";
        if (string.Equals(queryLang, "en", StringComparison.OrdinalIgnoreCase))
            return "en";

        // 3. User language choice stored in cookie
        if (Request.Cookies.TryGetValue("ieltop_lang", out string? cookieLang))
        {
            if (string.Equals(cookieLang, "vi", StringComparison.OrdinalIgnoreCase))
                return "vi";
            if (string.Equals(cookieLang, "en", StringComparison.OrdinalIgnoreCase))
                return "en";
        }

        // 4. Client timezone detected by JavaScript and stored in cookie
        if (Request.Cookies.TryGetValue("ieltop_tz", out string? tz) && !string.IsNullOrWhiteSpace(tz))
        {
            string cleanTz = Uri.UnescapeDataString(tz).Trim();
            if (cleanTz.Equals("Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                cleanTz.Equals("Asia/Saigon", StringComparison.OrdinalIgnoreCase) ||
                cleanTz.Equals("Asia/Bangkok", StringComparison.OrdinalIgnoreCase) ||
                cleanTz.Equals("Asia/Hanoi", StringComparison.OrdinalIgnoreCase))
            {
                return "vi";
            }
        }

        // 5. Accept-Language header fallback
        string acceptLang = Request.Headers.AcceptLanguage.ToString();
        if (acceptLang.StartsWith("vi", StringComparison.OrdinalIgnoreCase) ||
            acceptLang.Contains("vi-VN", StringComparison.OrdinalIgnoreCase))
        {
            return "vi";
        }

        return "en";
    }
}
