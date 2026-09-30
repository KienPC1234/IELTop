using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Submissions;

public sealed class IndexModel(ISubmissionService submissions) : PageModel
{
    public List<Submission> Rows { get; private set; } = new();
    public string? Status { get; private set; }
    public string? Query { get; private set; }
    public int InReview { get; private set; }
    public int Total { get; private set; }

    public async Task OnGetAsync(string? status, string? q, CancellationToken ct)
    {
        Status = status;
        Query = q;
        Rows = await submissions.ListForReviewAsync(status, q, ct);
        InReview = await submissions.CountByStatusAsync(SubmissionStatus.InReview, ct);
        Total = Rows.Count;
    }

    public List<string> TagsOf(Submission row) => PaperService.Deserialize(row.TagsJson);
}
