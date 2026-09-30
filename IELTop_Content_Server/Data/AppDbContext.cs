using IELTop_Content_Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IELTop_Content_Server.Data;

/// <summary>
/// SQLite cannot sort or compare DateTimeOffset, so every timestamp is
/// stored as UTC ticks. That is one integer column that orders
/// correctly on both SQLite and Postgres.
/// </summary>
public sealed class DateTimeOffsetToTicksConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));

public sealed class NullableDateTimeOffsetToTicksConverter()
    : ValueConverter<DateTimeOffset?, long?>(
        value => value.HasValue ? value.Value.UtcTicks : null,
        value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

/// <summary>
/// The whole server store. One context per request, never shared.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AccessCode> AccessCodes => Set<AccessCode>();
    public DbSet<LoginAccount> LoginAccounts => Set<LoginAccount>();
    public DbSet<ExamPaper> Papers => Set<ExamPaper>();
    public DbSet<AudioAsset> Audio => Set<AudioAsset>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Contributor> Contributors => Set<Contributor>();
    public DbSet<EditorApplication> EditorApplications => Set<EditorApplication>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CatalogPaper> Catalog => Set<CatalogPaper>();
    public DbSet<DailyStat> DailyStats => Set<DailyStat>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var required = new DateTimeOffsetToTicksConverter();
        var optional = new NullableDateTimeOffsetToTicksConverter();

        model.Entity<AdminUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.LastLoginAt).HasConversion(optional);
        });

        model.Entity<AccessCode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(128).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.ExpiresAt).HasConversion(optional);
            e.Property(x => x.LastUsedAt).HasConversion(optional);
        });

        model.Entity<LoginAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.LastLoginAt).HasConversion(optional);
        });

        model.Entity<ExamPaper>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(128);
            e.HasIndex(x => x.UpdatedAt);
            e.HasIndex(x => x.Category);
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.UpdatedAt).HasConversion(required);
        });

        model.Entity<AudioAsset>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FileName).IsUnique();
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.UploadedAt).HasConversion(required);
        });

        model.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.At);
            e.Property(x => x.At).HasConversion(required);
        });

        model.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
        });

        model.Entity<Contributor>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.LastLoginAt).HasConversion(optional);
        });

        model.Entity<EditorApplication>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.DecidedAt).HasConversion(optional);
            e.Property(x => x.InviteTokenExpiresAt).HasConversion(optional);
        });

        model.Entity<Submission>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.ContributorId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Reference).HasMaxLength(32);
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.UpdatedAt).HasConversion(required);
            e.Property(x => x.DecidedAt).HasConversion(optional);
        });

        model.Entity<SubmissionFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SubmissionId);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.CreatedAt).HasConversion(required);
        });

        model.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.ToAddress).HasMaxLength(256);
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.SentAt).HasConversion(optional);
        });

        model.Entity<CatalogPaper>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PaperId).IsUnique();
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.UpdatedAt);
            e.Property(x => x.PaperId).HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasConversion(required);
            e.Property(x => x.UpdatedAt).HasConversion(required);
        });

        model.Entity<DailyStat>(e =>
        {
            e.HasKey(x => x.Day);
            e.Property(x => x.Day).HasMaxLength(16);
            e.Property(x => x.UpdatedAt).HasConversion(required);
        });
    }
}
