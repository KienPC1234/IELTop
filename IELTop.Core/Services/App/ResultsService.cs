using System.Globalization;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;

namespace IELTop.Services.App;

/// <summary>One finished test, ready for the Results list.</summary>
public sealed record ResultRow(
    int Id,
    string PaperTitle,
    string Scope,
    string Strictness,
    string BandLabel,
    int Correct,
    int Total,
    string Summary,
    int Violations,
    string WritingBand,
    string SpeakingBand,
    string AiFeedback,
    string CreatedAt);

/// <summary>The Results screen state.</summary>
public sealed class ResultsSnapshot
{
    public IReadOnlyList<ResultRow> Attempts { get; init; } = Array.Empty<ResultRow>();
    public IReadOnlyList<ResultRow> Filtered { get; init; } = Array.Empty<ResultRow>();
    public IReadOnlyList<string> ScopeFilters { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WritingTable { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SpeakingTable { get; init; } = Array.Empty<string>();
    public string ScopeFilter { get; init; } = "All scopes";
    public string SearchText { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
    public string CriteriaHint { get; init; } = string.Empty;
    public bool HasAttempts { get; init; }
    public bool HasFiltered { get; init; }
    public string SummaryLabel { get; init; } = string.Empty;
}

/// <summary>
/// Past mock test results, read from the local database. Bands are practice
/// estimates, never official scores. Filtering matches a skill inside the
/// scope string, so one skill filter still catches a multi skill attempt.
/// </summary>
public sealed class ResultsService
{
    private string _scopeFilter = "All scopes";
    private string _search = string.Empty;

    public ResultsSnapshot Snapshot()
    {
        var attempts = new List<ResultRow>();
        bool readFailed = false;
        try
        {
            using var db = new AppDbContext();
            foreach (var a in db.ExamAttempts.OrderByDescending(x => x.CreatedAt).Take(100).ToList())
                attempts.Add(ToRow(a));
        }
        catch (Exception)
        {
            readFailed = true;
        }

        var filtered = attempts.Where(Matches).ToList();
        bool hasAttempts = attempts.Count > 0;

        return new ResultsSnapshot
        {
            Attempts = attempts,
            Filtered = filtered,
            ScopeFilters = new[] { "All scopes", "Full test", "Listening", "Reading", "Writing", "Speaking" },
            WritingTable = IeltsBanding.WritingBandTable(),
            SpeakingTable = IeltsBanding.SpeakingBandTable(),
            ScopeFilter = _scopeFilter,
            SearchText = _search,
            CriteriaHint = IeltsBanding.WritingCriteriaHint + " " + IeltsBanding.SpeakingCriteriaHint,
            HasAttempts = hasAttempts,
            HasFiltered = filtered.Count > 0,
            StatusMessage = readFailed
                ? "Could not read past results. The local database may be locked."
                : hasAttempts
                    ? $"{attempts.Count} test(s) finished. Bands are practice estimates, not official scores."
                    : "No finished tests yet. Run a mock test first.",
            SummaryLabel = hasAttempts
                ? $"{attempts.Count} test(s) finished. Bands are practice estimates, not official scores."
                : "No finished tests yet. Run a mock test first.",
        };
    }

    public ResultsSnapshot SetScopeFilter(string value)
    {
        _scopeFilter = string.IsNullOrWhiteSpace(value) ? "All scopes" : value;
        return Snapshot();
    }

    public ResultsSnapshot SetSearch(string value)
    {
        _search = value ?? string.Empty;
        return Snapshot();
    }

    /// <summary>Deletes every finished result. The UI confirms first.</summary>
    public ResultsSnapshot ClearAll()
    {
        try
        {
            using var db = new AppDbContext();
            db.ExamAttempts.RemoveRange(db.ExamAttempts);
            db.SaveChanges();
        }
        catch (Exception)
        {
            // Reported through the snapshot status line on the next read.
            return Snapshot();
        }
        return Snapshot();
    }

    private bool Matches(ResultRow row)
    {
        if (_scopeFilter != "All scopes"
            && !row.Scope.Contains(_scopeFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        var query = _search.Trim();
        if (query.Length > 0
            && !row.PaperTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static ResultRow ToRow(ExamAttempt a) => new(
        a.Id,
        a.PaperTitle,
        a.Scope,
        a.Strictness,
        string.Create(CultureInfo.InvariantCulture, $"{a.BandLow:0.0} to {a.BandHigh:0.0}"),
        a.Correct,
        a.Total,
        a.Summary,
        a.Violations,
        a.WritingBand > 0 ? a.WritingBand.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty,
        a.SpeakingBand > 0 ? a.SpeakingBand.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty,
        a.AiFeedback,
        a.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
}
