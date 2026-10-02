namespace IELTop_Content_Server.Models;

/// <summary>
/// Someone who can sign in to the admin portal.
/// </summary>
public sealed class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// A shared access code handed to a class. The code is stored as typed
/// on purpose so an admin can read it back and hand it out again.
/// </summary>
public sealed class AccessCode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public long UseCount { get; set; }
}

/// <summary>
/// A username and password used by POST /api/login. The password is
/// hashed with PBKDF2, never stored as typed.
/// </summary>
public sealed class LoginAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// One mock test paper. Id is the stable public name used in
/// /api/papers/{id}. Json holds the whole paper exactly as served.
/// </summary>
public sealed class ExamPaper
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public string SkillsJson { get; set; } = "[]";
    public string AudioFilesJson { get; set; } = "[]";
    public int PartCount { get; set; }
    public int QuestionCount { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsPublished { get; set; } = true;
    public string Json { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long DownloadCount { get; set; }
    public bool IsExclusive { get; set; } = false;
    public string ExclusiveCode { get; set; } = string.Empty;
}

/// <summary>
/// One audio clip served from /api/audio/{file}. The bytes live on
/// disk, the row keeps the metadata.
/// </summary>
public sealed class AudioAsset
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Format { get; set; } = "m4a";
    public bool IsStandardized { get; set; } = true;
    public string S3Url { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsPublished { get; set; } = true;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public long DownloadCount { get; set; }
}

/// <summary>
/// One admin action, kept so the portal can show what changed and when.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// An IP address that is blocked from accessing the server.
/// </summary>
public sealed class BlockedIp
{
    public int Id { get; set; }
    public string Ip { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset BlockedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string CreatedBy { get; set; } = "System";
}

/// <summary>
/// Small key and value table for runtime settings and the content
/// version that drives cache invalidation.
/// </summary>
public sealed class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The state of one submission as it moves through review.
/// </summary>
public enum SubmissionStatus
{
    Draft = 0,
    InReview = 1,
    Accepted = 2,
    Rejected = 3,
    Withdrawn = 4
}

/// <summary>
/// Someone outside the team who signs in to submit content. Separate
/// from AdminUser on purpose: a contributor can never open the portal.
/// </summary>
public sealed class Contributor
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public string Bio { get; set; } = string.Empty;
}

/// <summary>
/// A person asking to become a reviewing editor.
/// </summary>
public enum EditorApplicationStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2
}

public sealed class EditorApplication
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PortfolioUrl { get; set; } = string.Empty;
    public string Languages { get; set; } = string.Empty;
    public EditorApplicationStatus Status { get; set; } = EditorApplicationStatus.Pending;
    public string DecisionNote { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public int? ContributorId { get; set; }
    public int? DecidedByAdminId { get; set; }
    public DateTimeOffset? InviteTokenExpiresAt { get; set; }
    public string Role { get; set; } = "Editor";
}

/// <summary>
/// One contributor submission. Holds the accepted paper id once it is
/// promoted, so the author link and the audit trail survive.
/// </summary>
public sealed class Submission
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int ContributorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public string PaperId { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public int ReviewScore { get; set; } = -1;
    public string ReviewJson { get; set; } = string.Empty;
    public string ReviewSummary { get; set; } = string.Empty;
    public string DecisionReason { get; set; } = string.Empty;
    public int? DecidedByAdminId { get; set; }
    public string DecidedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>
/// A file attached to a submission: the pasted or uploaded paper JSON
/// and any audio clips. Text is inlined so a reviewer sees everything
/// without opening the storage folder.
/// </summary>
public sealed class SubmissionFile
{
    public long Id { get; set; }
    public int SubmissionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Kind { get; set; } = "json";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string StoredName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// One notification email, queued and delivered by a background worker
/// so a slow or down SMTP host never blocks a request.
/// </summary>
public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

public sealed class Notification
{
    public long Id { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// A paper in the catalog. Separate from ExamPaper so the public
/// protocol keeps serving only approved content and the catalog can
/// carry tags, an author, and a license.
/// </summary>
public sealed class CatalogPaper
{
    public int Id { get; set; }
    public string PaperId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public string SkillsJson { get; set; } = "[]";
    public string AudioFilesJson { get; set; } = "[]";
    public string Json { get; set; } = string.Empty;
    public int PartCount { get; set; }
    public int QuestionCount { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public bool IsPublished { get; set; } = true;
    public int? AuthorContributorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public int? SubmissionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long DownloadCount { get; set; }
    public bool IsExclusive { get; set; } = false;
    public string ExclusiveCode { get; set; } = string.Empty;
}

/// <summary>
/// Counters for the admin dashboard.
/// </summary>
public sealed class DailyStat
{
    public string Day { get; set; } = string.Empty;
    public long Downloads { get; set; }
    public long AudioServed { get; set; }
    public long ApiCalls { get; set; }
    public long Submissions { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
