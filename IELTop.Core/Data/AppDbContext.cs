using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IELTop.Models;
using SQLite;

namespace IELTop.Data;

/// <summary>
/// High-performance SQLite database context powered by sqlite-net-pcl.
/// Supports both Async-first operations and synchronous fallback, with WAL mode,
/// memory page caching, and automatic retry resilience against database locks.
/// </summary>
public sealed class AppDbContext : IDisposable
{
    private static readonly object SyncLock = new();
    private static SQLiteAsyncConnection? _asyncConnection;
    private static string? _cachedDbPath;

    private readonly SQLiteConnection _db;
    private readonly string _dbPath;
    private bool _disposed;

    public TableQuery<VocabularyWord> Words => _db.Table<VocabularyWord>();
    public TableQuery<StudyRecord> StudyRecords => _db.Table<StudyRecord>();
    public TableQuery<SpeakingAttempt> SpeakingAttempts => _db.Table<SpeakingAttempt>();
    public TableQuery<ExamAttempt> ExamAttempts => _db.Table<ExamAttempt>();
    public TableQuery<StudySession> StudySessions => _db.Table<StudySession>();
    public TableQuery<ChatMessage> ChatMessages => _db.Table<ChatMessage>();
    public TableQuery<PracticeSet> PracticeSets => _db.Table<PracticeSet>();
    public TableQuery<PracticeQuestion> PracticeQuestions => _db.Table<PracticeQuestion>();
    public TableQuery<TutorSpeakingAttempt> TutorSpeakingAttempts => _db.Table<TutorSpeakingAttempt>();

    public SQLiteConnection Connection => _db;

    public AppDbContext(string? dbPath = null)
    {
        _dbPath = dbPath ?? DefaultDatabasePath();
        EnsureDirectoryExists(_dbPath);

        _db = new SQLiteConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        ApplyPragmas(_db);
    }

    public static string DefaultDatabasePath() =>
        _cachedDbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "ieltop.db");

    public static void SetCustomPathForTesting(string? path)
    {
        lock (SyncLock)
        {
            _asyncConnection = null;
            _cachedDbPath = path;
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static void ApplyPragmas(SQLiteConnection conn)
    {
        try
        {
            conn.ExecuteScalar<string>("PRAGMA journal_mode = WAL;");
            conn.ExecuteScalar<string>("PRAGMA synchronous = NORMAL;");
            conn.ExecuteScalar<string>("PRAGMA temp_store = MEMORY;");
            conn.ExecuteScalar<string>("PRAGMA cache_size = -64000;");
        }
        catch { }
    }

    /// <summary>
    /// Thread-safe shared async connection with serialized background execution.
    /// Eliminates concurrency lock conflicts across threads.
    /// </summary>
    public static SQLiteAsyncConnection GetAsyncConnection(string? dbPath = null)
    {
        var path = dbPath ?? DefaultDatabasePath();
        lock (SyncLock)
        {
            if (_asyncConnection is null || _cachedDbPath != path)
            {
                EnsureDirectoryExists(path);
                _cachedDbPath = path;
                _asyncConnection = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                try
                {
                    _asyncConnection.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;").Wait();
                    _asyncConnection.ExecuteScalarAsync<string>("PRAGMA synchronous = NORMAL;").Wait();
                    _asyncConnection.ExecuteScalarAsync<string>("PRAGMA temp_store = MEMORY;").Wait();
                    _asyncConnection.ExecuteScalarAsync<string>("PRAGMA cache_size = -64000;").Wait();
                }
                catch { }
            }
            return _asyncConnection;
        }
    }

    public static SQLiteAsyncConnection AsyncDb => GetAsyncConnection();

    // ==========================================
    // ASYNC DATABASE OPERATIONS
    // ==========================================

    public static Task<int> InsertAsync(object item) => AsyncDb.InsertAsync(item);
    public static Task<int> InsertAllAsync(IEnumerable items) => AsyncDb.InsertAllAsync(items);
    public static Task<int> UpdateAsync(object item) => AsyncDb.UpdateAsync(item);
    public static Task<int> DeleteAsync(object item) => AsyncDb.DeleteAsync(item);
    public static Task<int> DeleteAllAsync<T>() => AsyncDb.DeleteAllAsync<T>();
    public static Task<T> GetAsync<T>(object id) where T : new() => AsyncDb.GetAsync<T>(id);

    /// <summary>
    /// Like <see cref="GetAsync{T}"/>, but returns default instead of throwing
    /// when no row has that id. Study screens read rows a user can delete, so a
    /// stale id must not crash the screen; a real database error still surfaces.
    /// </summary>
    public static async Task<T?> FindAsync<T>(object id) where T : new()
    {
        try
        {
            return await AsyncDb.GetAsync<T>(id).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    public static AsyncTableQuery<T> TableAsync<T>() where T : new() => AsyncDb.Table<T>();
    public static Task<List<T>> QueryAsync<T>(string sql, params object[] args) where T : new() => AsyncDb.QueryAsync<T>(sql, args);
    public static Task<int> ExecuteAsync(string sql, params object[] args) => AsyncDb.ExecuteAsync(sql, args);
    public static Task RunInTransactionAsync(Action<SQLiteConnection> action) => AsyncDb.RunInTransactionAsync(action);

    public static async Task<string> CheckIntegrityAsync()
    {
        try
        {
            var result = await AsyncDb.ExecuteScalarAsync<string>("PRAGMA integrity_check;").ConfigureAwait(false);
            return string.IsNullOrEmpty(result) ? "ok" : result;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public static async Task EnsureCreatedAsync()
    {
        var db = AsyncDb;
        await db.CreateTableAsync<VocabularyWord>().ConfigureAwait(false);
        await db.CreateTableAsync<StudyRecord>().ConfigureAwait(false);
        await db.CreateTableAsync<SpeakingAttempt>().ConfigureAwait(false);
        await db.CreateTableAsync<ExamAttempt>().ConfigureAwait(false);
        await db.CreateTableAsync<StudySession>().ConfigureAwait(false);
        await db.CreateTableAsync<ChatMessage>().ConfigureAwait(false);
        await db.CreateTableAsync<PracticeSet>().ConfigureAwait(false);
        await db.CreateTableAsync<PracticeQuestion>().ConfigureAwait(false);
        await db.CreateTableAsync<TutorSpeakingAttempt>().ConfigureAwait(false);
    }

    public static void EnsureCreated()
    {
        EnsureCreatedAsync().GetAwaiter().GetResult();
    }

    // ==========================================
    // SYNCHRONOUS CONTEXT METHODS (RETRY RESILIENT)
    // ==========================================

    public int Insert(object item) => ExecuteWithRetry(() => _db.Insert(item));
    public int Update(object item) => ExecuteWithRetry(() => _db.Update(item));
    public int Delete(object item) => ExecuteWithRetry(() => _db.Delete(item));
    public int DeleteAll<T>() => ExecuteWithRetry(() => _db.DeleteAll<T>());
    public int Execute(string query, params object[] args) => ExecuteWithRetry(() => _db.Execute(query, args));
    public List<T> Query<T>(string query, params object[] args) where T : new() => ExecuteWithRetry(() => _db.Query<T>(query, args));

    private static T ExecuteWithRetry<T>(Func<T> action, int maxRetries = 3)
    {
        int attempts = 0;
        while (true)
        {
            try
            {
                lock (SyncLock)
                {
                    return action();
                }
            }
            catch (SQLiteException ex) when (ex.Result is SQLite3.Result.Busy or SQLite3.Result.Locked && attempts < maxRetries)
            {
                attempts++;
                System.Threading.Thread.Sleep(50 * attempts);
            }
        }
    }

    /// <summary>
    /// Housekeeping the user can run from Settings: shrink the file after
    /// deletes and drop very old speaking attempts.
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
                removed = db.Execute("DELETE FROM SpeakingAttempts WHERE CreatedAt < ?", cutoff);
            }
            db.Execute("VACUUM;");
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Close();
    }
}
