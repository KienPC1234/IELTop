using System;
using System.IO;
using IELTop.Models;
using Microsoft.EntityFrameworkCore;

namespace IELTop.Data;

public sealed class AppDbContext : DbContext
{
    public DbSet<VocabularyWord> Words => Set<VocabularyWord>();
    public DbSet<StudyRecord> StudyRecords => Set<StudyRecord>();
    public DbSet<SpeakingAttempt> SpeakingAttempts => Set<SpeakingAttempt>();

    private readonly string _dbPath;

    public AppDbContext(string? dbPath = null)
    {
        _dbPath = dbPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IELTop", "ieltop.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<VocabularyWord>(e =>
        {
            e.HasIndex(x => x.Word).IsUnique();
            e.Property(x => x.Word).HasMaxLength(100).IsRequired();
        });
        b.Entity<StudyRecord>(e =>
        {
            e.HasOne(x => x.Word)
             .WithMany()
             .HasForeignKey(x => x.VocabularyWordId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public static void EnsureCreated()
    {
        using var db = new AppDbContext();
        Directory.CreateDirectory(Path.GetDirectoryName(db._dbPath)!);
        db.Database.EnsureCreated();
        db.ApplyCompatibilityFixes();
    }

    /// <summary>
    /// EnsureCreated does not alter existing tables, so rename older columns
    /// in place to keep databases from earlier builds working.
    /// </summary>
    private void ApplyCompatibilityFixes()
    {
        var columns = GetColumns("Words");
        if (columns.Contains("MeaningVi") && !columns.Contains("Meaning"))
            Database.ExecuteSqlRaw("ALTER TABLE \"Words\" RENAME COLUMN \"MeaningVi\" TO \"Meaning\";");
    }

    private HashSet<string> GetColumns(string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        var wasClosed = Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (wasClosed) Database.GetDbConnection().Open();
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(1));
        }
        finally
        {
            if (wasClosed) Database.GetDbConnection().Close();
        }
        return names;
    }
}
