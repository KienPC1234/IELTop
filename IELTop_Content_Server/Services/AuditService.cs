using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using Microsoft.EntityFrameworkCore;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Records who did what in the portal. Writes are best effort: a failed
/// audit row must never fail the action it describes.
/// </summary>
public interface IAuditService
{
    Task WriteAsync(string actor, string action, string target, string detail, string ip, CancellationToken ct = default);
    Task<List<AuditLog>> RecentAsync(int take, CancellationToken ct = default);
}

public sealed class AuditService(IDbContextFactory<AppDbContext> dbFactory) : IAuditService
{
    public async Task WriteAsync(string actor, string action, string target, string detail, string ip, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AuditLogs.Add(new AuditLog
            {
                At = DateTimeOffset.UtcNow,
                Actor = Clamp(actor, 128),
                Action = Clamp(action, 64),
                Target = Clamp(target, 256),
                Detail = Clamp(detail, 512),
                Ip = Clamp(ip, 64)
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Auditing is not allowed to break the request path.
        }
    }

    public async Task<List<AuditLog>> RecentAsync(int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditLogs
            .OrderByDescending(a => a.At)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private static string Clamp(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty
        : value.Length <= max ? value : value[..max];
}
