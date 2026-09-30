using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IELTop_Content_Server.Pages.Catalog;

public sealed class IndexModel(
    IDbContextFactory<AppDbContext> dbFactory) : PageModel
{
    public List<CatalogPaper> Rows { get; private set; } = new();
    public string? Query { get; private set; }
    public int Total { get; private set; }

    public async Task OnGetAsync(string? q, CancellationToken ct)
    {
        Query = q;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Total = await db.Catalog.CountAsync(ct);

        IQueryable<CatalogPaper> rows = db.Catalog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            rows = rows.Where(p => p.Title.Contains(q)
                || p.PaperId.Contains(q)
                || p.AuthorName.Contains(q)
                || p.TagsJson.Contains(q));

        Rows = await rows.OrderByDescending(p => p.UpdatedAt).Take(500).ToListAsync(ct);
    }

    public List<string> TagsOf(CatalogPaper row) => PaperService.Deserialize(row.TagsJson);
}
