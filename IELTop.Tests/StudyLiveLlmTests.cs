using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Learn;
using IELTop.Services.Storage;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// Grouped so the shared static AppDbContext path and the lesson folder are not
/// changed by another test at the same time.
/// </summary>
[Collection("AppState")]
public sealed class StudyLiveLlmTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _lessonDir;
    private readonly LessonService _lessons = new();

    public StudyLiveLlmTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_live_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();

        _lessonDir = Path.Combine(Path.GetTempPath(), $"ieltop_live_lessons_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_lessonDir);
        LessonService.SetUserDirForTesting(_lessonDir);

        // A small, self contained unit so the check does not depend on the
        // shipped lessons and cannot be confused by their size.
        File.WriteAllText(Path.Combine(_lessonDir, "unit-1.json"), """
        {
          "unit": "Unit 1",
          "title": "Unit 1",
          "source": "live test",
          "sections": [
            { "id": "g", "skill": "Grammar", "title": "U1 Grammar", "isAnswerKey": false,
              "blocks": [ { "type": "text", "text": "Zorblax is a rare bird that lives only in the Andes. It builds nests from blue stones." } ] }
          ],
          "vocabulary": [],
          "slides": [], "audio": []
        }
        """, System.Text.Encoding.UTF8);

        _lessons.Reload();
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        LessonService.SetUserDirForTesting(null);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_lessonDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Reads llm.txt (base url, key, model, model2) and builds a real service.
    /// Returns false when the file is absent, so the test is skipped rather than
    /// failed on a machine that has no key.
    /// </summary>
    private static bool TryBuildLlm(out OpenAiCompatibleLlmService llm, out string model)
    {
        llm = null!;
        model = string.Empty;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        for (int i = 0; i < 6 && dir is not null; i++)
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
        // Use the first model listed, then keep any other as a fallback. A
        // reasoning model that spends its whole budget on hidden thinking returns
        // empty content, so the plain instruct model should be the primary.
        var primary = lines[2].Trim();
        var fallback = lines.Length > 3 ? lines[3].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(primary))
            return false;

        var settings = new AppSettings
        {
            LlmBaseUrl = baseUrl,
            LlmModel = primary,
            LlmApiKey = key,
            LlmTemperature = 0.2,
            LlmMaxTokens = 900,
            LlmUseStreaming = false,
            LlmTimeoutSeconds = 120,
        };
        model = fallback.Length > 0 ? $"{primary} (fallback {fallback})" : primary;
        llm = new OpenAiCompatibleLlmService(new MemorySettingsStore(settings));
        return true;
    }

    [Fact]
    public async Task Live_chat_uses_the_lesson_body_when_the_question_asks_about_it()
    {
        if (!TryBuildLlm(out var llm, out var model))
        {
            Console.WriteLine("llm.txt not found; skipping the live tutor check.");
            return;
        }

        var study = new StudyService(_lessons, llm);
        var chat = study.ChatSnapshot(0, "");
        var snapshot = await study.AskAsync(chat.SelectedSessionId, "What is Zorblax and where does it live?", "");

        var reply = snapshot.Messages.Last(m => m.Role == "assistant").Text;
        Console.WriteLine($"model={model}\n--- reply ---\n{reply}\n-------------");

        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.True(reply.Contains("Zorblax", StringComparison.OrdinalIgnoreCase), "reply should name Zorblax");
        Assert.True(reply.Contains("Andes", StringComparison.OrdinalIgnoreCase), "reply should mention the Andes");
        Assert.Contains(snapshot.Messages.Last().Sources, s => s.Section == "U1 Grammar");
    }

    [Fact]
    public async Task Live_practice_returns_parseable_questions_and_saves_them()
    {
        if (!TryBuildLlm(out var llm, out var model))
        {
            Console.WriteLine("llm.txt not found; skipping the live practice check.");
            return;
        }

        var study = new StudyService(_lessons, llm);
        var practice = study.NewPracticeSession("unit-1", "Zorblax");
        var built = await study.BuildPracticeAsync(
            practice.SelectedSessionId, "unit-1", "Grammar", "Standard", 2);

        Console.WriteLine($"model={model} status={built.StatusMessage} openSetId={built.OpenSet?.Id} selected={built.SelectedSessionId}");
        Assert.True(built.OpenSet is not null, $"the set was not saved/opened: {built.StatusMessage}");
        Assert.True(built.OpenSet!.Questions.Count >= 1,
            $"the model reply was not usable as questions: {built.StatusMessage}");
        foreach (var q in built.OpenSet.Questions)
        {
            Assert.False(string.IsNullOrWhiteSpace(q.Prompt));
            var hasAnswer = !string.IsNullOrWhiteSpace(q.GapAnswer)
                || !string.IsNullOrWhiteSpace(q.CorrectKey)
                || q.MatchRows.Count > 0;
            Assert.True(hasAnswer, $"question {q.Number} has no answer");
        }
    }

    [Fact]
    public async Task Live_chat_tool_runs_on_the_typed_text()
    {
        if (!TryBuildLlm(out var llm, out var model))
        {
            Console.WriteLine("llm.txt not found; skipping the live tool check.");
            return;
        }

        var study = new StudyService(_lessons, llm);
        var chat = study.ChatSnapshot(0, "");
        var snapshot = await study.RunToolAsync(chat.SelectedSessionId, "lookup", "Zorblax", "");

        var reply = snapshot.Messages.Last(m => m.Role == "assistant").Text;
        Console.WriteLine($"model={model} tool=lookup\n--- reply ---\n{reply}\n-------------");

        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.True(reply.Contains("Zorblax", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A settings store held in memory, so a live test never touches the user's settings.</summary>
public sealed class MemorySettingsStore : ISettingsStore
{
    public MemorySettingsStore(AppSettings settings) => Current = settings;

    public AppSettings Current { get; }
    public string FilePath => "(memory)";
    public string DataFolder => Path.GetTempPath();
    public void Save() { }
    public void Reset() { }
}

/// <summary>
/// One bucket for tests that change the shared static AppDbContext path, so they
/// run in sequence and never point the database at each other's temp file.
/// </summary>
[CollectionDefinition("AppState", DisableParallelization = true)]
public sealed class AppStateCollection
{
}
