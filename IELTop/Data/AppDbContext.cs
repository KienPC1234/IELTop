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
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();

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
        db.EnsureNewTables();
        db.ApplyCompatibilityFixes();
    }

    /// <summary>
    /// EnsureCreated never adds tables to an existing database, so create
    /// tables added by newer builds by hand. Column layout matches what
    /// EF Core maps for these entities.
    /// </summary>
    private void EnsureNewTables()
    {
        Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"ExamAttempts\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_ExamAttempts\" PRIMARY KEY AUTOINCREMENT, " +
            "\"PaperTitle\" TEXT NOT NULL, \"Scope\" TEXT NOT NULL, \"Strictness\" TEXT NOT NULL, " +
            "\"BandLow\" REAL NOT NULL, \"BandHigh\" REAL NOT NULL, " +
            "\"Correct\" INTEGER NOT NULL, \"Total\" INTEGER NOT NULL, " +
            "\"Summary\" TEXT NOT NULL, \"Violations\" INTEGER NOT NULL DEFAULT 0, " +
            "\"WritingBand\" REAL NOT NULL DEFAULT 0, \"SpeakingBand\" REAL NOT NULL DEFAULT 0, " +
            "\"AiFeedback\" TEXT NOT NULL DEFAULT '', " +
            "\"CreatedAt\" TEXT NOT NULL);");
        EnsureViolationsColumn();
        EnsureAttemptColumn("WritingBand", "REAL NOT NULL DEFAULT 0");
        EnsureAttemptColumn("SpeakingBand", "REAL NOT NULL DEFAULT 0");
        EnsureAttemptColumn("AiFeedback", "TEXT NOT NULL DEFAULT ''");
        EnsureIndexes();
        Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"SpeakingAttempts\" (" +
            "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_SpeakingAttempts\" PRIMARY KEY AUTOINCREMENT, " +
            "\"TargetText\" TEXT NOT NULL, \"HeardPhonemes\" TEXT NOT NULL, " +
            "\"Substitutions\" INTEGER NOT NULL, \"Omissions\" INTEGER NOT NULL, " +
            "\"Insertions\" INTEGER NOT NULL, \"Accuracy\" REAL NOT NULL, " +
            "\"AudioPath\" TEXT NOT NULL, \"CreatedAt\" TEXT NOT NULL);");
    }

    /// <summary>Indexes that keep result and dashboard queries fast.</summary>
    private void EnsureIndexes()
    {
        Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS \"IX_ExamAttempts_CreatedAt\" " +
            "ON \"ExamAttempts\" (\"CreatedAt\");");
        Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS \"IX_ExamAttempts_Scope\" " +
            "ON \"ExamAttempts\" (\"Scope\");");
        Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS \"IX_SpeakingAttempts_CreatedAt\" " +
            "ON \"SpeakingAttempts\" (\"CreatedAt\");");
    }

    /// <summary>Adds one ExamAttempts column to databases from older builds.</summary>
    private void EnsureAttemptColumn(string column, string definition)
    {
        if (GetColumns("ExamAttempts").Contains(column)) return;
        // Column name and definition are fixed literals from this class, never user input.
        var sql = $"ALTER TABLE \"ExamAttempts\" ADD COLUMN \"{column}\" {definition};";
        Database.ExecuteSqlRaw(sql);
    }

    /// <summary>Adds the Violations column to databases from older builds.</summary>
    private void EnsureViolationsColumn()
        => EnsureAttemptColumn("Violations", "INTEGER NOT NULL DEFAULT 0");

    /// <summary>
    /// Housekeeping the user can run from Settings: shrink the file after
    /// deletes and drop very old speaking attempts that no screen reads.
    /// Returns a short human summary. Never throws.
    /// </summary>
    public static string CompactAndClean(int keepSpeakingDays = 180)
    {
        try
        {
            using var db = new AppDbContext();
            int removed = 0;
            if (keepSpeakingDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-keepSpeakingDays);
                removed = db.SpeakingAttempts.Count(s => s.CreatedAt < cutoff);
                if (removed > 0)
                {
                    db.Database.ExecuteSqlRaw(
                        "DELETE FROM \"SpeakingAttempts\" WHERE \"CreatedAt\" < {0};", cutoff);
                }
            }
            db.Database.ExecuteSqlRaw("VACUUM;");
            var info = new FileInfo(db._dbPath);
            long mb = info.Exists ? info.Length / 1048576 : 0;
            return removed > 0
                ? $"Database cleaned. Removed {removed} old speaking attempt(s). File size now about {mb} MB."
                : $"Database compacted. File size now about {mb} MB.";
        }
        catch (Exception)
        {
            return "Could not compact the database. Close other windows and try again.";
        }
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
