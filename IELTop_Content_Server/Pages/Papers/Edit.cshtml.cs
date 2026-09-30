using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Papers;

public sealed class EditModel(
    IPaperService papers,
    IAudioService audio,
    IAuditService audit) : PageModel
{
    [BindProperty]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public string Json { get; set; } = DefaultTemplate;

    [BindProperty]
    public bool Overwrite { get; set; } = true;

    public bool IsNew { get; private set; }
    public string? Error { get; private set; }
    public ExamPaper? Preview { get; private set; }
    public List<string> AudioFiles { get; private set; } = new();
    public HashSet<string> MissingAudio { get; private set; } = new();

    private const string DefaultTemplate = """
        {
          "title": "New paper",
          "source": "Where this content comes from and its license",
          "category": "Academic",
          "level": "5.0-7.0",
          "tags": [],
          "parts": [
            {
              "id": "R1",
              "skill": "Reading",
              "title": "Reading Passage 1",
              "topic": "",
              "taskType": "Passage 1",
              "minutes": 12,
              "instructions": "",
              "material": "",
              "audioFile": "",
              "questions": []
            }
          ]
        }
        """;

    public async Task<IActionResult> OnGetAsync(string? id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id))
        {
            IsNew = true;
            return Page();
        }

        var row = await papers.GetAsync(id, ct);
        if (row is null)
            return RedirectToPage("/Papers/Index");

        IsNew = false;
        Id = row.Id;
        Json = row.Json;
        Preview = row;
        await LoadAudioAsync(id, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var existing = await papers.GetAsync(Id, ct);
        IsNew = existing is null;

        var (ok, error) = await papers.SaveRawAsync(Id, Json, Overwrite, ct);
        if (!ok)
        {
            Error = error;
            Preview = existing;
            await LoadAudioAsync(Id, ct);
            return Page();
        }

        await audit.WriteAsync(Actor, "paper.save", Id,
            $"{Json.Length} bytes", Ip, ct);
        TempData["Message"] = $"Paper {Id} was saved.";
        return RedirectToPage("/Papers/Index");
    }

    private async Task LoadAudioAsync(string id, CancellationToken ct)
    {
        var row = await papers.GetAsync(id, ct);
        if (row is null)
            return;

        AudioFiles = PaperService.Deserialize(row.AudioFilesJson);
        if (AudioFiles.Count == 0)
            return;

        var known = await audio.NamesAsync(ct);
        MissingAudio = new HashSet<string>(
            AudioFiles.Where(f => !known.Contains(f) || audio.ResolveExisting(f) is null),
            StringComparer.OrdinalIgnoreCase);
    }

    private string Actor => User.Identity?.Name ?? "unknown";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
