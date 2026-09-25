using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IELTop.Models;

namespace IELTop.Services.Storage;

/// <summary>
/// Writes practice content and mock papers back out as JSON or Markdown, so a
/// user can share one item, one paper, or a whole loaded set.
/// </summary>
public interface IPaperExporter
{
    Task<ExportResult> ExportPapersAsync(IReadOnlyList<ExamPaper> papers, string folder, ExportFormat format, CancellationToken ct = default);
    Task<ExportResult> ExportReadingAsync(IReadOnlyList<ReadingPassage> items, string folder, ExportFormat format, CancellationToken ct = default);
    Task<ExportResult> ExportListeningAsync(IReadOnlyList<ListeningItem> items, string folder, ExportFormat format, CancellationToken ct = default);
    Task<ExportResult> ExportWritingAsync(IReadOnlyList<WritingTask> items, string folder, ExportFormat format, CancellationToken ct = default);
}

public enum ExportFormat
{
    Json,
    Markdown
}

public sealed record ExportResult(bool Success, string Error, int FileCount, string Folder)
{
    public static ExportResult Fail(string error) => new(false, error, 0, string.Empty);
}

public sealed class PaperExporter : IPaperExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<ExportResult> ExportPapersAsync(IReadOnlyList<ExamPaper> papers, string folder, ExportFormat format, CancellationToken ct = default)
        => WriteManyAsync(papers, p => SafeName(p.Title), folder, format, ct);

    public Task<ExportResult> ExportReadingAsync(IReadOnlyList<ReadingPassage> items, string folder, ExportFormat format, CancellationToken ct = default)
        => WriteManyAsync(items, p => SafeName(p.Title), folder, format, ct);

    public Task<ExportResult> ExportListeningAsync(IReadOnlyList<ListeningItem> items, string folder, ExportFormat format, CancellationToken ct = default)
        => WriteManyAsync(items, p => SafeName(p.Title), folder, format, ct);

    public Task<ExportResult> ExportWritingAsync(IReadOnlyList<WritingTask> items, string folder, ExportFormat format, CancellationToken ct = default)
        => WriteManyAsync(items, p => SafeName(p.Title), folder, format, ct);

    private static async Task<ExportResult> WriteManyAsync<T>(
        IReadOnlyList<T> items, Func<T, string> nameOf, string folder, ExportFormat format, CancellationToken ct)
    {
        if (items.Count == 0)
            return ExportResult.Fail("There is nothing to export.");

        try
        {
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(folder, $"IELTop-export-{stamp}");
            Directory.CreateDirectory(target);

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                var baseName = nameOf(item);
                if (format == ExportFormat.Json)
                {
                    var json = JsonSerializer.Serialize(item, Options);
                    await File.WriteAllTextAsync(Path.Combine(target, baseName + ".json"), json, ct);
                }
                else
                {
                    var markdown = ToMarkdown(item!);
                    await File.WriteAllTextAsync(Path.Combine(target, baseName + ".md"), markdown, ct);
                }
            }

            return new ExportResult(true, string.Empty, items.Count, target);
        }
        catch (OperationCanceledException)
        {
            return ExportResult.Fail("Export was cancelled.");
        }
        catch (Exception)
        {
            return ExportResult.Fail("The files could not be written. Check the folder and try again.");
        }
    }

    private static string ToMarkdown(object item) => item switch
    {
        ExamPaper p => ExamToMarkdown(p),
        ReadingPassage r => ReadingToMarkdown(r),
        ListeningItem l => ListeningToMarkdown(l),
        WritingTask w => WritingToMarkdown(w),
        _ => string.Empty
    };

    private static string ExamToMarkdown(ExamPaper paper)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {paper.Title}").AppendLine();
        sb.AppendLine($"> Source: {paper.Source}").AppendLine();
        foreach (var part in paper.Parts)
        {
            sb.AppendLine($"## {part.Title} ({part.Skill}, {part.Minutes} min)").AppendLine();
            if (!string.IsNullOrWhiteSpace(part.Instructions))
                sb.AppendLine(part.Instructions).AppendLine();
            if (!string.IsNullOrWhiteSpace(part.Material))
                sb.AppendLine(part.Material).AppendLine();
            foreach (var q in part.Questions)
            {
                sb.AppendLine($"{q.Number}. {q.Prompt}");
                foreach (var opt in q.Options)
                    sb.AppendLine($"   - {opt.Key}. {opt.Text}");
                sb.AppendLine($"   Answer: {q.CorrectKey}").AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string ReadingToMarkdown(ReadingPassage p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {p.Title}").AppendLine();
        sb.AppendLine($"> Source: {p.Source}").AppendLine();
        sb.AppendLine(p.Body).AppendLine();
        AppendQuestions(sb, p.Questions);
        return sb.ToString();
    }

    private static string ListeningToMarkdown(ListeningItem l)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {l.Title}").AppendLine();
        sb.AppendLine($"> Source: {l.Source}").AppendLine();
        sb.AppendLine($"Audio file: {l.AudioFile}").AppendLine();
        if (!string.IsNullOrWhiteSpace(l.Transcript))
            sb.AppendLine("## Transcript").AppendLine().AppendLine(l.Transcript).AppendLine();
        AppendQuestions(sb, l.Questions);
        return sb.ToString();
    }

    private static string WritingToMarkdown(WritingTask w)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {w.Title}").AppendLine();
        sb.AppendLine($"> Source: {w.Source}").AppendLine();
        sb.AppendLine($"{w.TaskType}, {w.Minutes} minutes, at least {w.MinimumWords} words.").AppendLine();
        sb.AppendLine(w.Prompt).AppendLine();
        foreach (var tip in w.Tips)
            sb.AppendLine($"- {tip}");
        return sb.ToString();
    }

    private static void AppendQuestions(StringBuilder sb, List<ExamQuestion> questions)
    {
        if (questions.Count == 0) return;
        sb.AppendLine("## Questions").AppendLine();
        foreach (var q in questions)
        {
            sb.AppendLine($"{q.Number}. {q.Prompt}");
            foreach (var opt in q.Options)
                sb.AppendLine($"   - {opt.Key}. {opt.Text}");
            sb.AppendLine($"   Answer: {q.CorrectKey}").AppendLine();
        }
    }

    /// <summary>Keep file names safe on Windows without over-sanitising them.</summary>
    private static string SafeName(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "untitled";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (cleaned.Length > 60) cleaned = cleaned[..60];
        return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
    }
}
