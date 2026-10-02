using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Submissions;

public sealed class IndexModel(ISubmissionService submissions) : PageModel
{
    public List<Submission> Rows { get; private set; } = new();
    public string? Status { get; private set; }
    public string? Query { get; private set; }
    public string? Tier { get; private set; }
    public string? Sort { get; private set; }
    public int InReview { get; private set; }
    public int Total { get; private set; }
    public int StrongCount { get; private set; }
    public int BorderlineCount { get; private set; }
    public int WeakCount { get; private set; }

    public async Task OnGetAsync(string? status, string? q, string? tier, string? sort, CancellationToken ct)
    {
        Status = status;
        Query = q;
        Tier = tier;
        Sort = sort;
        Rows = await submissions.ListForReviewAsync(status, q, tier, sort, ct);
        InReview = await submissions.CountByStatusAsync(SubmissionStatus.InReview, ct);
        Total = Rows.Count;
        StrongCount = Rows.Count(r => r.ReviewScore >= 70);
        BorderlineCount = Rows.Count(r => r.ReviewScore >= 40 && r.ReviewScore < 70);
        WeakCount = Rows.Count(r => r.ReviewScore >= 0 && r.ReviewScore < 40);
    }

    public List<string> TagsOf(Submission row) => PaperService.Deserialize(row.TagsJson);
}
