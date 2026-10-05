using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Diagnostics;
using IELTop.Services.Exam;
using IELTop.Services.Learn;
using IELTop.Services.App;
using IELTop.Services.Storage;

namespace IELTop.Desktop.Diagnostics;

/// <summary>
/// The headless self test. Started with --selftest, it runs the checks that
/// normally need a person clicking around: build the services, open the
/// database, load the lessons, call a sample of bridge routes, and report each
/// one. It opens no window, prints one line per check, and exits with 0 when
/// everything passed or 2 when something failed.
///
/// This is the point of the whole logging work: one run says whether the app is
/// healthy, so the window does not have to be opened again for every check.
/// </summary>
public sealed class SelfTestRunner
{
    private readonly Bridge.BridgeRouter _router;
    private readonly LessonService _lessons;
    private readonly ExamEngine _engine;
    private readonly SettingsService _settings;
    private readonly IExamRepository _papers;

    public SelfTestRunner(
        Bridge.BridgeRouter router,
        LessonService lessons,
        ExamEngine engine,
        SettingsService settings,
        IExamRepository papers)
    {
        _router = router;
        _lessons = lessons;
        _engine = engine;
        _settings = settings;
        _papers = papers;
    }

    private sealed record Result(string Name, bool Ok, string Detail);

    public bool Run()
    {
        var results = new List<Result>();
        var watch = Stopwatch.StartNew();

        results.Add(Run("database", () =>
        {
            var integrity = AppDbContext.CheckIntegrityAsync().GetAwaiter().GetResult();
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"integrity check said: {integrity}");
            var path = AppDbContext.DefaultDatabasePath();
            return $"{path}, {new FileInfo(path).Length / 1024} KB";
        }));

        results.Add(Run("settings", () =>
        {
            var snapshot = _settings.Snapshot();
            return $"theme={snapshot.Theme}, fontScale={snapshot.FontScale}";
        }));

        results.Add(Run("lessons", () =>
        {
            _lessons.Reload();
            var units = _lessons.Units;
            if (units.Count == 0)
                throw new InvalidOperationException(
                    "no lesson units found. Run tools/lesson-ingest and build again.");
            int sections = units.Sum(u => u.Sections.Count);
            int words = units.Sum(u => u.Vocabulary.Count);
            return $"{units.Count} unit(s), {sections} section(s), {words} word(s)";
        }));

        results.Add(Run("lesson search", () =>
        {
            var hits = _lessons.Search("grammar relative clause nouns", max: 3);
            if (hits.Count == 0)
                throw new InvalidOperationException("a sample query returned no lesson hits.");
            // The model is sent the section body, so a hit must carry one that is
            // longer than the short badge preview.
            var best = hits[0];
            if (best.Body.Length <= best.Snippet.Length)
                throw new InvalidOperationException(
                    "a hit carried no body beyond the snippet; the tutor would answer from a preview.");
            return $"{hits.Count} hit(s), best body {best.Body.Length} chars";
        }));

        results.Add(Run("papers", () =>
        {
            var papers = _papers.LoadPapers();
            if (papers.Count == 0)
                throw new InvalidOperationException("no exam papers found under Assets/Exams.");
            return $"{papers.Count} paper(s), first '{papers[0].Title}'";
        }));

        results.Add(Run("exam engine setup", () =>
        {
            var setup = _engine.Setup;
            if (setup is null)
                throw new InvalidOperationException("the engine returned no setup.");
            return $"{setup.Papers.Count} paper(s) selectable";
        }));

        results.Add(Run("models registry", () =>
        {
            var slots = OnnxModelRegistry.Slots;
            int installed = slots.Count(s => OnnxModelRegistry.IsComplete(s.Name));
            var required = new[] { "mdd-wav2vec2-base", "gec-t5-small", "tts-piper-lessac" }
                .Concat(OnnxModelRegistry.SttSlotsByQuality);
            var missing = required.Where(n => slots.All(s => s.Name != n)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"these service slots are not in the registry: {string.Join(", ", missing)}.");
            return $"{slots.Count} slot(s), {installed} installed, STT={OnnxModelRegistry.ResolveSttSlot()}";
        }));

        results.Add(RunBridge("bridge.dashboard", "dashboard.get", null));
        results.Add(RunBridge("bridge.lessons", "study.chat.snapshot", new { sessionId = 0, unit = "", query = "" }));
        results.Add(RunBridge("bridge.practice", "study.practice.snapshot", new { sessionId = 0, setId = 0, scope = "All", query = "" }));
        results.Add(RunBridge("bridge.vocab", "study.vocab.snapshot", new { query = "arctic", unit = "" }));
        results.Add(RunBridge("bridge.diagnostics", "diagnostics.snapshot", new { tailLines = 5 }));

        results.Add(Run("log tail via bridge", () =>
        {
            // The Diagnostics tab reads the log while the app still holds it open
            // for writing. This checks that read works, which a plain snapshot
            // call would not catch because a failed read is returned, not thrown.
            var marker = "selftest-tail-" + Guid.NewGuid().ToString("N");
            AppLog.Info("selftest", marker);
            AppLog.Flush();

            var args = JsonDocument.Parse(JsonSerializer.Serialize(new { tailLines = 500 })).RootElement.Clone();
            var response = _router.HandleDirectAsync("selftest", "diagnostics.snapshot", args, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (response.Error is not null)
                throw new InvalidOperationException(response.Error);

            var json = JsonSerializer.Serialize(response.Result, Bridge.BridgeJson.Options);
            if (!json.Contains(marker))
                throw new InvalidOperationException(
                    "diagnostics.snapshot did not return the log tail; the log may be locked by its writer.");
            return "tail read while the log is open";
        }));

        results.Add(Run("log file", () =>
        {
            AppLog.Info("selftest", "Self test reached the log check.");
            AppLog.Flush();
            if (string.IsNullOrEmpty(AppLog.LatestFile) || !File.Exists(AppLog.LatestFile))
                throw new InvalidOperationException("the log file was not opened.");
            return AppLog.LatestFile;
        }));

        // --live adds two checks that call a real model from llm.txt. Off by
        // default so the normal self test stays fast and offline.
        if (LiveRequested())
        {
            results.Add(Run("live model", CheckLiveModel));
            results.Add(Run("live grounding", CheckLiveGrounding));
        }

        watch.Stop();
        return Report(results, watch.ElapsedMilliseconds);
    }

    private static bool LiveRequested() =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--live", StringComparison.OrdinalIgnoreCase));

    /// <summary>Opens a real connection and asks for one fixed reply.</summary>
    private static string CheckLiveModel()
    {
        if (!TryBuildLiveLlm(out var llm, out var model)) return "skipped: llm.txt not found";
        var result = llm.CompleteAsync(new[] { LlmMessage.User("Reply with exactly: OK") }).GetAwaiter().GetResult();
        if (!result.Success) throw new InvalidOperationException(result.Error);
        if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidOperationException("the model returned nothing.");
        return $"{model} answered in {result.ElapsedMs} ms";
    }

    /// <summary>
    /// Asks about a real lesson word and expects the reply to use the lesson
    /// source. It does not write to the database: the sources and messages are
    /// built the same way the tutor builds them, then sent straight to the model.
    /// </summary>
    private string CheckLiveGrounding()
    {
        if (!TryBuildLiveLlm(out var llm, out var model)) return "skipped: llm.txt not found";
        var lesson = _lessons.Units.FirstOrDefault();
        if (lesson is null) return "skipped: no lesson units";

        var word = lesson.Vocabulary.FirstOrDefault(v => v.Word.Length > 5)?.Word;
        var question = word is null
            ? "Summarise the main grammar point of this unit."
            : $"Explain the word '{word}' using the lesson.";

        var hits = _lessons.Search(word ?? "grammar", max: 3, unitSlug: lesson.Slug);
        var sourceText = StudyService.BuildSourceText(hits);
        var messages = StudyService.BuildChatMessages(question, sourceText, Array.Empty<StudyMessageRow>());
        var result = llm.CompleteAsync(messages).GetAwaiter().GetResult();
        if (!result.Success) throw new InvalidOperationException(result.Error);
        if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidOperationException("the model returned nothing.");

        if (word is not null
            && !result.Text.Contains(word, StringComparison.OrdinalIgnoreCase)
            && hits.Count > 0
            && !hits.Any(h => result.Text.Contains(h.SectionTitle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"the reply did not use the lesson source for '{word}': {Shorten(result.Text, 120)}");
        }

        return $"{model} grounded reply for '{word ?? lesson.Slug}': {Shorten(result.Text, 80)}";
    }

    private static string Shorten(string text, int max)
    {
        var flat = text.Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max] + "...";
    }

    /// <summary>
    /// Builds a real model client from llm.txt (base url, key, model). Returns
    /// false when the file is missing, so --live is skipped rather than failed on
    /// a machine that has no key. The file is gitignored and never logged.
    /// </summary>
    private static bool TryBuildLiveLlm(out OpenAiCompatibleLlmService llm, out string model)
    {
        llm = null!;
        model = string.Empty;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "llm.txt");
            if (File.Exists(candidate)) { path = candidate; break; }
            dir = dir.Parent;
        }
        if (path is null) return false;

        var lines = File.ReadAllLines(path);
        if (lines.Length < 3) return false;
        var baseUrl = lines[0].Trim();
        var key = lines[1].Trim();
        // The first listed model is the primary; a reasoning model that returns
        // empty content is kept only as a fallback name.
        model = lines[2].Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model))
            return false;

        var settings = new AppSettings
        {
            LlmBaseUrl = baseUrl,
            LlmModel = model,
            LlmApiKey = key,
            LlmTemperature = 0.2,
            LlmMaxTokens = 500,
            LlmUseStreaming = false,
            LlmTimeoutSeconds = 120,
        };
        llm = new OpenAiCompatibleLlmService(new LiveSettingsStore(settings));
        return true;
    }

    /// <summary>Settings held in memory for a live check, so the user's file is untouched.</summary>
    private sealed class LiveSettingsStore : ISettingsStore
    {
        public LiveSettingsStore(AppSettings settings) => Current = settings;
        public AppSettings Current { get; }
        public string FilePath => "(live)";
        public string DataFolder => Path.GetTempPath();
        public void Save() { }
        public void Reset() { }
    }

    private static Result Run(string name, Func<string> action)
    {
        try
        {
            var detail = action();
            AppLog.Info("selftest", $"{name}: ok ({detail})");
            return new Result(name, true, detail);
        }
        catch (Exception ex)
        {
            AppLog.Error("selftest", $"{name}: FAILED", ex);
            return new Result(name, false, ex.Message);
        }
    }

    private Result RunBridge(string name, string method, object? args)
    {
        try
        {
            JsonElement? element = args is null
                ? null
                : JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement.Clone();
            var response = _router.HandleDirectAsync("selftest", method, element, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (response.Error is not null)
                throw new InvalidOperationException(response.Error);
            var summary = response.Result is null ? "null" : response.Result.GetType().Name;
            AppLog.Info("selftest", $"{name}: ok ({summary})");
            return new Result(name, true, $"{method} -> {summary}");
        }
        catch (Exception ex)
        {
            AppLog.Error("selftest", $"{name}: FAILED", ex);
            return new Result(name, false, ex.Message);
        }
    }

    private static bool Report(List<Result> results, long elapsedMs)
    {
        int failed = results.Count(r => !r.Ok);
        var summary = new System.Text.StringBuilder();
        summary.AppendLine();
        summary.AppendLine("================ IELTop self test ================");
        foreach (var r in results)
        {
            summary.Append(r.Ok ? "  PASS  " : "  FAIL  ");
            summary.Append(r.Name.PadRight(22));
            summary.Append(' ');
            summary.AppendLine(r.Detail);
        }
        summary.AppendLine(new string('-', 52));
        summary.AppendLine($"  {results.Count - failed}/{results.Count} passed in {elapsedMs} ms");
        summary.AppendLine($"  log: {AppLog.LatestFile}");
        summary.AppendLine("==================================================");

        var text = summary.ToString();
        Console.Out.WriteLine(text);
        AppLog.Info("selftest", failed == 0
            ? $"All {results.Count} checks passed in {elapsedMs} ms."
            : $"{failed} of {results.Count} checks failed.");
        AppLog.Flush();
        return failed == 0;
    }
}
