using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Editor applications. Anyone can apply, an admin decides, and an
/// approved applicant gets an account and an invite email so they can
/// sign in and help review content.
/// </summary>
public interface IEditorService
{
    Task<(bool Ok, string Error, EditorApplication? App)> ApplyAsync(
        string email, string fullName, string reason, string portfolioUrl, string languages,
        CancellationToken ct = default);
    Task<List<EditorApplication>> ListAsync(EditorApplicationStatus? status, CancellationToken ct = default);
    Task<EditorApplication?> GetAsync(int id, CancellationToken ct = default);
    Task<(bool Ok, string Error)> ApproveAsync(
        int id, string note, string role, int? adminId, CancellationToken ct = default);
    Task<(bool Ok, string Error)> DeclineAsync(
        int id, string note, int? adminId, CancellationToken ct = default);
    Task<int> CountPendingAsync(CancellationToken ct = default);
    bool IsEditor(string email, IReadOnlyList<EditorApplication> approved);
}

public sealed class EditorService(
    IDbContextFactory<AppDbContext> dbFactory,
    IContributorService contributors,
    INotificationService notify,
    IOptions<SmtpOptions> smtp,
    ILogger<EditorService> logger) : IEditorService
{
    private readonly SmtpOptions _smtp = smtp.Value;

    /// <summary>
    /// A signed in email is an editor when an approved application
    /// matches it. One place for the rule, so pages do not drift.
    /// </summary>
    public bool IsEditor(string email, IReadOnlyList<EditorApplication> approved) =>
        approved.Any(a => a.Status == EditorApplicationStatus.Approved
            && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

    private string SignInUrl()
    {
        string baseUrl = _smtp.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "/Contrib/SignIn";
        return $"{baseUrl.TrimEnd('/')}/Contrib/SignIn";
    }

    public async Task<(bool Ok, string Error, EditorApplication? App)> ApplyAsync(
        string email, string fullName, string reason, string portfolioUrl, string languages,
        CancellationToken ct = default)
    {
        email = ContributorService.Normalize(email);
        if (!ContributorService.LooksLikeEmail(email))
            return (false, "Enter a valid email address.", null);
        if (string.IsNullOrWhiteSpace(fullName))
            return (false, "Enter your name.", null);
        var reason2 = (reason ?? string.Empty).Trim();
        if (reason2.Length < 20)
            return (false, "Tell us a little about your experience, at least a sentence.", null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        bool pending = await db.EditorApplications.AnyAsync(a =>
            a.Email == email && a.Status == EditorApplicationStatus.Pending, ct);
        if (pending)
            return (false, "You already have an application under review.", null);

        var app = new EditorApplication
        {
            Email = email,
            FullName = fullName.Trim(),
            Reason = reason2,
            PortfolioUrl = (portfolioUrl ?? string.Empty).Trim(),
            Languages = (languages ?? string.Empty).Trim()
        };
        db.EditorApplications.Add(app);
        await db.SaveChangesAsync(ct);

        return (true, string.Empty, app);
    }

    public async Task<List<EditorApplication>> ListAsync(
        EditorApplicationStatus? status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<EditorApplication> rows = db.EditorApplications.AsNoTracking();
        if (status is not null)
            rows = rows.Where(a => a.Status == status);
        return await rows.OrderByDescending(a => a.CreatedAt).AsNoTracking().ToListAsync(ct);
    }

    public async Task<EditorApplication?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.EditorApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<(bool Ok, string Error)> ApproveAsync(
        int id, string note, string role, int? adminId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var app = await db.EditorApplications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null)
            return (false, "That application no longer exists.");
        if (app.Status != EditorApplicationStatus.Pending)
            return (false, "That application was already decided.");

        app.Status = EditorApplicationStatus.Approved;
        app.DecisionNote = (note ?? string.Empty).Trim();
        app.Role = string.IsNullOrWhiteSpace(role) ? "Editor" : role.Trim();
        app.DecidedAt = DateTimeOffset.UtcNow;
        app.DecidedByAdminId = adminId;

        // The applicant needs an account to sign in. If they already have
        // one we keep it, otherwise we create it with a temporary
        // password and send that password in the invite email.
        string? temporaryPassword = null;
        var existing = await contributors.FindByEmailAsync(app.Email, ct);
        if (existing is not null)
        {
            app.ContributorId = existing.Id;
        }
        else
        {
            temporaryPassword = NewTempPassword();
            var (created, _, user) = await contributors.RegisterAsync(
                app.Email, app.FullName, temporaryPassword, ct);
            if (created && user is not null)
                app.ContributorId = user.Id;
        }

        await db.SaveChangesAsync(ct);

        string signInUrl = SignInUrl();
        string credentials = temporaryPassword is null
            ? "Sign in with the account you already have."
            : $"Sign in with your email and this temporary password: {temporaryPassword}\n"
              + "Change it from Account after you sign in.\n";

        await notify.QueueAsync(
            app.Email,
            "You were approved as an IELTop editor",
            $"Hello {app.FullName},\n\n"
            + $"Your application to help review content was approved. Your role: {app.Role}.\n\n"
            + (string.IsNullOrWhiteSpace(app.DecisionNote) ? string.Empty : $"Note: {app.DecisionNote}\n\n")
            + credentials
            + $"\nSign in here: {signInUrl}\n\n"
            + "As an editor you can sign in to submit content. Approved editors help the team "
            + "review the queue in the admin portal.\n",
            ct);

        logger.LogInformation("Editor application {Id} approved for {Email}", id, app.Email);
        return (true, string.Empty);
    }

    public async Task<(bool Ok, string Error)> DeclineAsync(
        int id, string note, int? adminId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var app = await db.EditorApplications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null)
            return (false, "That application no longer exists.");
        if (app.Status != EditorApplicationStatus.Pending)
            return (false, "That application was already decided.");

        app.Status = EditorApplicationStatus.Declined;
        app.DecisionNote = (note ?? string.Empty).Trim();
        app.DecidedAt = DateTimeOffset.UtcNow;
        app.DecidedByAdminId = adminId;
        await db.SaveChangesAsync(ct);

        await notify.QueueAsync(
            app.Email,
            "Your IELTop editor application",
            $"Hello {app.FullName},\n\n"
            + "Thank you for applying. We are not taking on new editors right now and your "
            + "application was not approved.\n\n"
            + (string.IsNullOrWhiteSpace(app.DecisionNote) ? string.Empty : $"Note: {app.DecisionNote}\n\n")
            + "You are still welcome to create an account and submit practice content.\n",
            ct);

        return (true, string.Empty);
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.EditorApplications.CountAsync(a => a.Status == EditorApplicationStatus.Pending, ct);
    }

    private static string NewTempPassword() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(9)).ToLowerInvariant();
}
