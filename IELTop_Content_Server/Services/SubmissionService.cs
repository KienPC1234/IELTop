using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// One file the contributor attached, already read into memory.
/// </summary>
public sealed record SubmissionUpload(string FileName, string ContentType, byte[] Bytes);

/// <summary>
/// A request to create a submission.
/// </summary>
public sealed record SubmissionRequest(
    int ContributorId,
    string AuthorName,
    string Title,
    string Note,
    string Source,
    string License,
    IReadOnlyList<SubmissionUpload> Files);

public sealed class SubmissionFileView
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool HasBinary { get; set; }
}

/// <summary>
/// Everything about contributor submissions: intake, validation, the
/// model review, the human decision, and promotion into the catalog and
/// the protocol store once accepted.
/// </summary>
public interface ISubmissionService
{
    Task<(bool Ok, string Error, Submission? Created)> CreateAsync(
        SubmissionRequest request, ReviewResult? reviewOverride, CancellationToken ct = default);
    Task<List<Submission>> ListForContributorAsync(int contributorId, CancellationToken ct = default);
    Task<List<Submission>> ListForReviewAsync(
        string? status, string? query, string? tier = null, string? sort = null, CancellationToken ct = default);
    Task<Submission?> GetAsync(int id, CancellationToken ct = default);
    Task<List<SubmissionFileView>> FilesAsync(int submissionId, CancellationToken ct = default);
    Task<SubmissionFile?> FileAsync(long fileId, CancellationToken ct = default);
    Task<(bool Ok, string Error)> AcceptAsync(
        int id, string decisionNote, string extraTags, string adminName, int? adminId, CancellationToken ct = default);
    Task<(bool Ok, string Error)> RejectAsync(
        int id, string reason, string adminName, int? adminId, CancellationToken ct = default);
    Task<(bool Ok, string Error)> WithdrawAsync(int id, int contributorId, CancellationToken ct = default);
    Task<int> CountByStatusAsync(SubmissionStatus status, CancellationToken ct = default);
    Task<int> CountForContributorAsync(int contributorId, CancellationToken ct = default);
    Task<int> CountRecentForContributorAsync(int contributorId, TimeSpan window, CancellationToken ct = default);
    Task<bool> SaveReviewAsync(int id, ReviewResult result, CancellationToken ct = default);
    Task<(bool Ok, string Error)> UpdatePaperFileAsync(int submissionId, long fileId, string newContent, CancellationToken ct = default);
}

public sealed class SubmissionService(
    IDbContextFactory<AppDbContext> dbFactory,
    IPaperService papers,
    IAudioService audio,
    ILlmReviewService review,
    INotificationService notify,
    IContentCache cache,
    IOptions<StorageOptions> storage,
    IOptions<ContributeOptions> contribute,
    IS3StorageService s3,
    ILogger<SubmissionService> logger) : ISubmissionService
{
    private static readonly string[] AudioTypes =
        { ".m4a", ".opus", ".ogg", ".wav", ".mp3", ".aac", ".flac" };

    private readonly StorageOptions _storage = storage.Value;
    private readonly ContributeOptions _limits = contribute.Value;

    public async Task<(bool Ok, string Error, Submission? Created)> CreateAsync(
        SubmissionRequest request, ReviewResult? reviewOverride, CancellationToken ct = default)
    {
        if (request.Files.Count == 0)
            return (false, "Attach at least one file.", null);
        if (request.Files.Count > Math.Max(1, _limits.MaxFilesPerSubmission))
            return (false, $"At most {_limits.MaxFilesPerSubmission} files per submission.", null);
        if (string.IsNullOrWhiteSpace(request.Title))
            return (false, "Give the submission a title.", null);

        long totalBytes = request.Files.Sum(f => (long)f.Bytes.Length);
        long cap = Math.Max(1, _limits.MaxSubmissionMb) * 1024L * 1024L;
        if (totalBytes > cap)
            return (false, $"The submission is larger than {_limits.MaxSubmissionMb} MB.", null);
        if (totalBytes == 0)
            return (false, "The attached files are empty.", null);

        string reference = NewReference();
        string folder = Path.Combine(SubmissionPaths.Root(_storage), reference);
        Directory.CreateDirectory(folder);

        var staged = new List<SubmissionFile>();
        string? paperText = null;

        foreach (var upload in request.Files)
        {
            ct.ThrowIfCancellationRequested();
            string safe = SafeFileName(upload.FileName);
            if (safe.Length == 0)
                continue;

            string ext = Path.GetExtension(safe).ToLowerInvariant();
            string kind = ext == ".json" ? "paper"
                : AudioTypes.Contains(ext) ? "audio"
                : FileTextExtractor.IsSupported(safe) ? "text"
                : "other";

            if (kind == "other")
            {
                // Reject the whole submission rather than silently drop
                // a file the author believes was sent.
                Cleanup(folder);
                return (false, $"The file {safe} is not a supported type. "
                    + "Use json, txt, md, csv, docx, pdf, or a listening clip.", null);
            }

            string storedName = $"{staged.Count + 1:D2}-{safe}";
            string target = Path.Combine(folder, storedName);
            await File.WriteAllBytesAsync(target, upload.Bytes, ct);

            if (s3.Enabled)
            {
                try
                {
                    using var s3Stream = new MemoryStream(upload.Bytes);
                    await s3.UploadAsync($"submissions/{reference}/{storedName}", s3Stream, upload.ContentType, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to upload submission file {File} to S3", storedName);
                }
            }

            string hash = Convert.ToHexString(SHA256.HashData(upload.Bytes)).ToLowerInvariant();
            var file = new SubmissionFile
            {
                FileName = safe,
                Kind = kind,
                ContentType = string.IsNullOrWhiteSpace(upload.ContentType)
                    ? "application/octet-stream"
                    : upload.ContentType,
                SizeBytes = upload.Bytes.Length,
                Sha256 = hash,
                StoredName = storedName
            };

            if (kind is "paper" or "text")
            {
                if (!FileTextExtractor.TryExtract(target, out string text, out string error))
                {
                    Cleanup(folder);
                    return (false, $"{safe}: {error}", null);
                }
                file.Text = text;
                if (kind == "paper")
                {
                    paperText = string.IsNullOrEmpty(paperText) || text.Length > paperText.Length
                        ? text
                        : paperText;
                }
                else if (kind == "text")
                {
                    paperText = string.IsNullOrEmpty(paperText) ? text : paperText + "\n\n---\n\n" + text;
                }
            }

            staged.Add(file);
        }

        if (staged.Count == 0)
        {
            Cleanup(folder);
            return (false, "None of the attached files could be read.", null);
        }

        bool hasJsonPaper = staged.Any(f => f.Kind == "paper");
        bool hasTextFiles = staged.Any(f => f.Kind == "text");

        if (!hasJsonPaper && !hasTextFiles)
        {
            Cleanup(folder);
            return (false, "Attach the paper as JSON, or as txt, md, csv, docx, or pdf text.", null);
        }

        ParsedPaper? parsed = null;
        if (hasJsonPaper)
        {
            // A JSON paper must parse as a paper before it is stored, so a
            // reviewer never has to open a broken file.
            var paperFile = staged.First(f => f.Kind == "paper");
            var (paperOk, paperError, p) = PaperService.ParseText(paperFile.Text);
            if (!paperOk || p is null)
            {
                Cleanup(folder);
                return (false, $"The paper JSON is not valid: {paperError}", null);
            }
            parsed = p;
        }
        else
        {
            // Synthesize a draft paper JSON from the submitted text/docx/pdf/md content
            string combinedText = string.Join("\n\n---\n\n", staged.Where(f => f.Kind == "text").Select(f => f.Text));
            bool hasAudio = staged.Any(f => f.Kind == "audio");
            string skill = hasAudio ? "listening" : "reading";

            var draftObj = new
            {
                title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled practice paper" : request.Title.Trim(),
                skill = skill,
                category = "academic",
                level = "B2-C1",
                source = request.Source,
                license = request.License,
                parts = new object[]
                {
                    new
                    {
                        part = 1,
                        skill = skill,
                        material = combinedText.Length > 24000 ? combinedText[..24000] : combinedText,
                        minutes = 30,
                        questions = Array.Empty<object>()
                    }
                }
            };

            string draftJson = JsonSerializer.Serialize(draftObj, new JsonSerializerOptions { WriteIndented = true });
            string draftStoredName = "00-draft-paper.json";
            string draftPath = Path.Combine(folder, draftStoredName);
            await File.WriteAllTextAsync(draftPath, draftJson, ct);

            var draftFile = new SubmissionFile
            {
                FileName = "draft-paper.json",
                Kind = "paper",
                ContentType = "application/json",
                SizeBytes = Encoding.UTF8.GetByteCount(draftJson),
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(draftJson))).ToLowerInvariant(),
                StoredName = draftStoredName,
                Text = draftJson,
                Note = "Auto-generated draft from submitted documents"
            };

            if (s3.Enabled)
            {
                try
                {
                    using var s3Stream = new MemoryStream(Encoding.UTF8.GetBytes(draftJson));
                    await s3.UploadAsync($"submissions/{reference}/{draftStoredName}", s3Stream, "application/json", ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to upload generated draft paper to S3");
                }
            }

            staged.Insert(0, draftFile);
            paperText = combinedText;
            var (_, _, p) = PaperService.ParseText(draftJson);
            parsed = p;
        }

        // Run the model review unless the caller already supplied one.
        var verdict = reviewOverride;
        if (verdict is null)
        {
            if (review.Enabled && _limits.AutoReview)
            {
                string textToReview = paperText ?? staged.First(f => f.Kind == "paper").Text;
                verdict = await review.ReviewAsync(
                    request.Title, request.Source, request.License, textToReview, ct);
            }
            else
            {
                // Review is not configured, not an error. The submission
                // waits for a human with no score.
                verdict = new ReviewResult { Ran = false };
            }
        }

        var submission = new Submission
        {
            Reference = reference,
            ContributorId = request.ContributorId,
            AuthorName = Clamp(request.AuthorName, 200),
            Title = (parsed is not null && parsed.Title.Length > 0) ? Clamp(parsed.Title, 300) : Clamp(request.Title.Trim(), 300),
            Note = Clamp(request.Note, 2000),
            Source = (parsed is not null && parsed.Source.Length > 0) ? Clamp(parsed.Source, 500) : Clamp(request.Source, 500),
            License = Clamp(request.License, 200),
            Status = SubmissionStatus.Draft
        };

        bool modelFailed = verdict is { Ran: false, Error.Length: > 0 };
        bool autoRejected = false;

        if (verdict is not null && verdict.Ran)
        {
            submission.ReviewScore = verdict.Score;
            submission.ReviewJson = verdict.RawJson;
            submission.ReviewSummary = verdict.Summary;
            submission.TagsJson = JsonSerializer.Serialize(verdict.Tags);

            // Only auto reject when the operator set a floor and the
            // model both failed the paper and gave it a low score.
            autoRejected = !verdict.Passed
                           && _limits.AutoRejectBelowScore > 0
                           && verdict.Score >= 0
                           && verdict.Score < _limits.AutoRejectBelowScore;
        }

        submission.Status = autoRejected
            ? SubmissionStatus.Rejected
            : modelFailed ? SubmissionStatus.Draft : SubmissionStatus.InReview;
        if (autoRejected)
        {
            var reasons = verdict!.Problems.Count > 0
                ? verdict.Problems
                : new List<string> { verdict.Summary };
            submission.DecisionReason = "The automated review marked this submission as not ready: "
                + string.Join("; ", reasons.Where(r => r.Length > 0));
            submission.DecidedAt = DateTimeOffset.UtcNow;
            submission.DecidedByName = "Automated review";
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Submissions.Add(submission);
            await db.SaveChangesAsync(ct);

            foreach (var file in staged)
            {
                file.SubmissionId = submission.Id;
                db.SubmissionFiles.Add(file);
            }
            await db.SaveChangesAsync(ct);
        }

        if (autoRejected)
        {
            await notify.QueueAsync(
                await EmailOfAsync(submission.ContributorId, ct),
                $"Submission {reference} was not accepted",
                $"Your submission \"{submission.Title}\" was reviewed automatically and did not pass.\n\n"
                + $"Reason: {submission.DecisionReason}\n\n"
                + "You can improve it and submit again from your dashboard.",
                ct);
        }

        logger.LogInformation("Submission {Reference} created with status {Status}",
            reference, submission.Status);
        return (true, string.Empty, submission);
    }

    public async Task<List<Submission>> ListForContributorAsync(
        int contributorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Submissions
            .Where(s => s.ContributorId == contributorId)
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<Submission>> ListForReviewAsync(
        string? status, string? query, string? tier = null, string? sort = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<Submission> rows = db.Submissions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<SubmissionStatus>(status, ignoreCase: true, out var parsed))
            rows = rows.Where(s => s.Status == parsed);

        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(s => s.Title.Contains(query)
                || s.Reference.Contains(query)
                || s.AuthorName.Contains(query));

        if (!string.IsNullOrWhiteSpace(tier))
        {
            rows = tier.ToLowerInvariant() switch
            {
                "strong" => rows.Where(s => s.ReviewScore >= 70),
                "borderline" => rows.Where(s => s.ReviewScore >= 40 && s.ReviewScore < 70),
                "weak" => rows.Where(s => s.ReviewScore >= 0 && s.ReviewScore < 40),
                "unreviewed" => rows.Where(s => s.ReviewScore < 0),
                _ => rows
            };
        }

        rows = (sort?.ToLowerInvariant()) switch
        {
            "score_desc" => rows.OrderByDescending(s => s.ReviewScore).ThenByDescending(s => s.CreatedAt),
            "score_asc" => rows.OrderBy(s => s.ReviewScore >= 0 ? s.ReviewScore : 999).ThenByDescending(s => s.CreatedAt),
            "oldest" => rows.OrderBy(s => s.CreatedAt),
            _ => rows.OrderBy(s => s.Status == SubmissionStatus.InReview ? 0 : 1)
                     .ThenByDescending(s => s.CreatedAt)
        };

        return await rows.AsNoTracking().ToListAsync(ct);
    }

    public async Task<Submission?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Submissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<List<SubmissionFileView>> FilesAsync(int submissionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var files = await db.SubmissionFiles
            .Where(f => f.SubmissionId == submissionId)
            .OrderBy(f => f.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        return files.Select(f => new SubmissionFileView
        {
            Id = f.Id,
            FileName = f.FileName,
            Kind = f.Kind,
            SizeBytes = f.SizeBytes,
            Text = f.Text,
            Note = f.Note,
            HasBinary = f.StoredName.Length > 0
        }).ToList();
    }

    public async Task<SubmissionFile?> FileAsync(long fileId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SubmissionFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
    }

    public async Task<(bool Ok, string Error)> AcceptAsync(
        int id, string decisionNote, string extraTags, string adminName, int? adminId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null)
            return (false, "That submission no longer exists.");
        if (submission.Status == SubmissionStatus.Accepted)
            return (false, "That submission was already accepted.");
        if (submission.Status is SubmissionStatus.Rejected or SubmissionStatus.Withdrawn)
            return (false, "That submission was already decided.");

        var files = await db.SubmissionFiles
            .Where(f => f.SubmissionId == id)
            .OrderBy(f => f.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        var paperFile = files.FirstOrDefault(f => f.Kind == "paper");
        if (paperFile is null)
            return (false, "The submission has no paper file to publish.");

        var (ok, error, parsed) = PaperService.ParseText(paperFile.Text);
        if (!ok || parsed is null)
            return (false, $"The stored paper is no longer valid: {error}");

        // Copy any attached listening clips into the served audio store
        // so the paper resolves when a client downloads it.
        foreach (var file in files.Where(f => f.Kind == "audio"))
        {
            string? full = StoredPath(submission.Reference, file.StoredName);
            if (full is null || !File.Exists(full))
                continue;
            await using var stream = File.OpenRead(full);
            await audio.SaveAsync(file.FileName, stream, overwrite: true, ct);
        }

        var existingTags = PaperService.Deserialize(submission.TagsJson);
        var extra = (extraTags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant());
        var tags = existingTags.Concat(extra).Distinct().Take(20).ToList();

        // Allocate an id and save without overwrite, so two submissions
        // with the same title can never replace each other. On a clash
        // the next suffix is tried.
        var enriched = parsed with
        {
            Tags = tags,
            Source = string.IsNullOrWhiteSpace(parsed.Source) ? submission.Source : parsed.Source
        };
        var taken = await ExistingIdsAsync(db, ct);
        string paperId = string.Empty;
        string paperError = string.Empty;
        bool savedPaper = false;
        for (int attempt = 0; attempt < 25 && !savedPaper; attempt++)
        {
            paperId = UniquePaperId(parsed, submission.Reference, taken);
            var (okSave, errorSave) = await papers.SaveAsync(paperId, enriched, overwrite: false, ct);
            savedPaper = okSave;
            paperError = errorSave;
            taken.Add(paperId);
        }
        if (!savedPaper)
            return (false, paperError.Length > 0 ? paperError : "The paper could not be saved.");

        submission.Status = SubmissionStatus.Accepted;
        submission.PaperId = paperId;
        submission.TagsJson = JsonSerializer.Serialize(tags);
        submission.DecisionReason = decisionNote;
        submission.DecidedAt = DateTimeOffset.UtcNow;
        submission.DecidedByAdminId = adminId;
        submission.DecidedByName = adminName;
        submission.UpdatedAt = DateTimeOffset.UtcNow;

        db.Catalog.Add(new CatalogPaper
        {
            PaperId = paperId,
            Title = enriched.Title,
            Category = enriched.Category,
            Level = enriched.Level,
            Source = enriched.Source,
            License = submission.License,
            TagsJson = JsonSerializer.Serialize(tags),
            SkillsJson = JsonSerializer.Serialize(enriched.Skills),
            AudioFilesJson = JsonSerializer.Serialize(enriched.AudioFiles),
            Json = enriched.Json,
            PartCount = enriched.PartCount,
            QuestionCount = enriched.QuestionCount,
            SizeBytes = Encoding.UTF8.GetByteCount(enriched.Json),
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enriched.Json))).ToLowerInvariant(),
            AuthorContributorId = submission.ContributorId,
            AuthorName = submission.AuthorName,
            SubmissionId = submission.Id
        });

        await db.SaveChangesAsync(ct);
        await cache.BumpVersionAsync(ct);

        await notify.QueueAsync(
            await EmailOfAsync(submission.ContributorId, ct),
            $"Submission {submission.Reference} was accepted",
            $"Good news. Your submission \"{submission.Title}\" was accepted and is now "
            + $"published as paper {paperId}.\n\n"
            + (string.IsNullOrWhiteSpace(decisionNote) ? string.Empty : $"Editor note: {decisionNote}\n\n")
            + $"Tags: {(tags.Count > 0 ? string.Join(", ", tags) : "none")}\n\n"
            + "Thank you for contributing to the IELTop library.",
            ct);

        logger.LogInformation("Submission {Reference} accepted as paper {PaperId}",
            submission.Reference, paperId);
        return (true, string.Empty);
    }

    public async Task<(bool Ok, string Error)> RejectAsync(
        int id, string reason, string adminName, int? adminId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "Give a reason so the author knows what to fix.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null)
            return (false, "That submission no longer exists.");

        submission.Status = SubmissionStatus.Rejected;
        submission.DecisionReason = reason.Trim();
        submission.DecidedAt = DateTimeOffset.UtcNow;
        submission.DecidedByAdminId = adminId;
        submission.DecidedByName = adminName;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await notify.QueueAsync(
            await EmailOfAsync(submission.ContributorId, ct),
            $"Submission {submission.Reference} was not accepted",
            $"Your submission \"{submission.Title}\" was reviewed and not accepted.\n\n"
            + $"Reason: {submission.DecisionReason}\n\n"
            + "You can fix the points above and submit again from your dashboard.",
            ct);

        logger.LogInformation("Submission {Reference} rejected", submission.Reference);
        return (true, string.Empty);
    }

    public async Task<(bool Ok, string Error)> WithdrawAsync(
        int id, int contributorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null)
            return (false, "That submission no longer exists.");
        if (submission.ContributorId != contributorId)
            return (false, "You can only withdraw your own submission.");
        if (submission.Status is SubmissionStatus.Accepted or SubmissionStatus.Rejected)
            return (false, "That submission was already decided.");

        submission.Status = SubmissionStatus.Withdrawn;
        submission.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, string.Empty);
    }

    public async Task<int> CountByStatusAsync(SubmissionStatus status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Submissions.CountAsync(s => s.Status == status, ct);
    }

    public async Task<int> CountForContributorAsync(int contributorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Submissions.CountAsync(s => s.ContributorId == contributorId, ct);
    }

    public async Task<int> CountRecentForContributorAsync(
        int contributorId, TimeSpan window, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow - window;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Submissions
            .CountAsync(s => s.ContributorId == contributorId && s.CreatedAt >= since, ct);
    }

    private async Task<string> EmailOfAsync(int contributorId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Contributors
            .Where(c => c.Id == contributorId)
            .Select(c => c.Email)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
    }

    private static async Task<HashSet<string>> ExistingIdsAsync(AppDbContext db, CancellationToken ct)
    {
        var ids = await db.Papers.Select(p => p.Id).AsNoTracking().ToListAsync(ct);
        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A stable, url safe paper id from the title, made unique with a
    /// numeric suffix when it clashes.
    /// </summary>
    private static string UniquePaperId(ParsedPaper paper, string reference, HashSet<string> taken)
    {
        var builder = new StringBuilder();
        foreach (char c in paper.Title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (c is ' ' or '-' or '_' && builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
            if (builder.Length >= 48)
                break;
        }
        string baseId = builder.ToString().Trim('-');
        if (baseId.Length < 3)
            baseId = "paper-" + reference.ToLowerInvariant();
        if (baseId.Length > 64)
            baseId = baseId[..64];

        string id = baseId;
        int suffix = 2;
        while (taken.Contains(id))
        {
            id = $"{baseId}-{suffix}";
            suffix++;
        }
        return id;
    }

    private static string NewReference()
    {
        string random = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
        return $"SUB-{DateTime.UtcNow:yyyyMMdd}-{random}";
    }

    private static string Clamp(string value, int max)
    {
        value = (value ?? string.Empty).Trim();
        return value.Length <= max ? value : value[..max];
    }

    private string? StoredPath(string reference, string storedName)
    {
        string safeRef = new string(reference.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        string safeName = SafeFileName(storedName);
        if (safeRef.Length == 0 || safeName.Length == 0)
            return null;

        string root = Path.GetFullPath(SubmissionPaths.Root(_storage));
        string full = Path.GetFullPath(Path.Combine(root, safeRef, safeName));
        if (!IsInside(root, full))
            return null;
        return full;
    }

    /// <summary>
    /// True only when the target is inside the root, with the separator
    /// so a sibling folder like "Submissions-old" cannot pass.
    /// </summary>
    internal static bool IsInside(string root, string full)
    {
        string withSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return full.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static void Cleanup(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // The temp folder is harmless if it stays.
        }
    }

    public static string SafeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string name = Path.GetFileName(raw.Trim());
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ')
                builder.Append(c);
        }
        name = builder.ToString().Trim().TrimStart('.');
        return name.Length > 180 ? name[..180] : name;
    }

    public async Task<bool> SaveReviewAsync(int id, ReviewResult result, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Submissions.FindAsync(new object[] { id }, ct);
        if (row is null) return false;

        row.ReviewScore = result.Score;
        row.ReviewJson = result.RawJson;
        row.ReviewSummary = result.Summary;
        if (result.Tags.Count > 0)
            row.TagsJson = System.Text.Json.JsonSerializer.Serialize(result.Tags);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(bool Ok, string Error)> UpdatePaperFileAsync(
        int submissionId, long fileId, string newContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newContent))
            return (false, "Content cannot be empty.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null)
            return (false, "Submission not found.");

        if (submission.Status is SubmissionStatus.Accepted or SubmissionStatus.Rejected or SubmissionStatus.Withdrawn)
            return (false, "Decided submissions cannot be edited.");

        var file = await db.SubmissionFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.SubmissionId == submissionId, ct);
        if (file is null)
            return (false, "Submission file not found.");

        if (file.Kind == "paper" || file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var (paperOk, paperError, parsed) = PaperService.ParseText(newContent);
            if (!paperOk || parsed is null)
                return (false, $"Invalid paper JSON: {paperError}");

            if (!string.IsNullOrWhiteSpace(parsed.Title))
                submission.Title = Clamp(parsed.Title, 300);
            if (!string.IsNullOrWhiteSpace(parsed.Source))
                submission.Source = Clamp(parsed.Source, 500);
        }

        file.Text = newContent;
        file.SizeBytes = Encoding.UTF8.GetByteCount(newContent);
        file.Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newContent))).ToLowerInvariant();

        string? diskPath = StoredPath(submission.Reference, file.StoredName);
        if (diskPath is not null)
        {
            await File.WriteAllTextAsync(diskPath, newContent, ct);
        }

        if (s3.Enabled)
        {
            try
            {
                using var s3Stream = new MemoryStream(Encoding.UTF8.GetBytes(newContent));
                await s3.UploadAsync($"submissions/{submission.Reference}/{file.StoredName}", s3Stream, file.ContentType, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update edited file {File} on S3", file.StoredName);
            }
        }

        submission.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, string.Empty);
    }
}

/// <summary>
/// The on disk layout for submissions, kept in one place.
/// </summary>
public static class SubmissionPaths
{
    public static string Root(StorageOptions options) =>
        Path.Combine(StoragePaths.Root(options), "Submissions");
}
