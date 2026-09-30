namespace IELTop_Content_Server;

/// <summary>
/// Small helpers shared by the pages.
/// </summary>
public static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    public static string When(DateTimeOffset? value) =>
        value is null ? "never" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static string Day(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd");

    /// <summary>A friendly label for a submission status.</summary>
    public static string StatusLabel(Models.SubmissionStatus status) => status switch
    {
        Models.SubmissionStatus.Draft => "Needs attention",
        Models.SubmissionStatus.InReview => "In review",
        Models.SubmissionStatus.Accepted => "Accepted",
        Models.SubmissionStatus.Rejected => "Rejected",
        Models.SubmissionStatus.Withdrawn => "Withdrawn",
        _ => status.ToString()
    };

    public static string StatusColor(Models.SubmissionStatus status) => status switch
    {
        Models.SubmissionStatus.Draft => "text-bg-warning",
        Models.SubmissionStatus.InReview => "text-bg-info",
        Models.SubmissionStatus.Accepted => "text-bg-success",
        Models.SubmissionStatus.Rejected => "text-bg-danger",
        Models.SubmissionStatus.Withdrawn => "text-bg-secondary",
        _ => "text-bg-light"
    };
}
