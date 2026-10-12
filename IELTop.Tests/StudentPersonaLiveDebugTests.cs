using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Learn;
using IELTop.Services.Storage;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// End-to-end debug verification simulating a real student learning from the shipped curriculum.
/// </summary>
[Collection("AppState")]
public sealed class StudentPersonaLiveDebugTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LessonService _lessons = new();

    public StudentPersonaLiveDebugTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_student_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();
        // Uses real shipped lessons
        LessonService.SetUserDirForTesting(null);
        _lessons.Reload();
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        try { File.Delete(_dbPath); } catch { }
    }

    private static bool TryBuildLlm(out OpenAiCompatibleLlmService llm, out string modelName)
    {
        llm = null!;
        modelName = string.Empty;

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
            LlmMaxTokens = 1200,
            LlmUseStreaming = false,
            LlmTimeoutSeconds = 120,
        };
        modelName = fallback.Length > 0 ? $"{primary} (fallback {fallback})" : primary;
        llm = new OpenAiCompatibleLlmService(new MemorySettingsStore(settings));
        return true;
    }

    [Fact]
    public async Task Student_Asks_Unit6_WritingMaps_Consultation()
    {
        if (!TryBuildLlm(out var llm, out var model)) return;

        var study = new StudyService(_lessons, llm);
        var chat = study.ChatSnapshot(0, "unit-6");
        var snapshot = await study.AskAsync(
            chat.SelectedSessionId,
            "In IELTS Writing Task 1 Maps (Unit 6), how should I combine sentences to describe position and change using relative clauses?",
            "unit-6");

        var lastMsg = snapshot.Messages.LastOrDefault(m => m.Role == "assistant");
        Assert.NotNull(lastMsg);
        var reply = lastMsg.Text;
        var exercise = lastMsg.ExerciseJson;
        var fullContent = reply + " " + exercise;
        Console.WriteLine($"[STUDENT TEST 1 - MAPS CONSULTATION]\nModel: {model}\nText: {reply}\nExercise: {exercise}\n");

        Assert.False(string.IsNullOrWhiteSpace(fullContent));
        Assert.True(lastMsg.Sources.Count > 0, "AI Tutor must cite real Unit 6 lesson sources");
        Assert.True(fullContent.Contains("relative", StringComparison.OrdinalIgnoreCase)
                    || fullContent.Contains("clause", StringComparison.OrdinalIgnoreCase)
                    || fullContent.Contains("map", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Student_Requests_Grammar_Rule_With_Latex_Symbols()
    {
        if (!TryBuildLlm(out var llm, out var model)) return;

        var study = new StudyService(_lessons, llm);
        var chat = study.ChatSnapshot(0, "unit-1");
        var snapshot = await study.AskAsync(
            chat.SelectedSessionId,
            "Explain relative clauses in Unit 1 and show the grammar transformation formula using LaTeX math syntax like $\\rightarrow$.",
            "unit-1");

        var lastMsg = snapshot.Messages.LastOrDefault(m => m.Role == "assistant");
        Assert.NotNull(lastMsg);
        var reply = lastMsg.Text;
        var exercise = lastMsg.ExerciseJson;
        var fullContent = reply + " " + exercise;
        Console.WriteLine($"[STUDENT TEST 2 - LATEX / KATEX FORMULA]\nModel: {model}\nText: {reply}\nExercise: {exercise}\n");

        Assert.False(string.IsNullOrWhiteSpace(fullContent));
        Assert.True(fullContent.Contains("$") || fullContent.Contains("rightarrow") || fullContent.Contains("clause", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Student_Builds_And_Answers_Real_Practice_Set()
    {
        if (!TryBuildLlm(out var llm, out var model)) return;

        var study = new StudyService(_lessons, llm);
        var practice = study.NewPracticeSession("unit-6", "Unit 6 Maps Practice");

        // Build practice set from real Unit 6
        var built = await study.BuildPracticeAsync(
            practice.SelectedSessionId, "unit-6", "Writing", "Standard", 2);

        Console.WriteLine($"[STUDENT TEST 3 - PRACTICE SET BUILD]\nStatus: {built.StatusMessage}");
        Assert.NotNull(built.OpenSet);
        Assert.True(built.OpenSet.Questions.Count >= 1, "Must generate at least 1 practice question from Unit 6");

        var firstQ = built.OpenSet.Questions[0];
        Console.WriteLine($"Question 1 ({firstQ.Kind}): {firstQ.Prompt}");

        // Student submits answer
        string testAnswer = firstQ.Kind == "single" && firstQ.Options.Count > 0
            ? firstQ.Options[0].Key
            : (firstQ.Kind == "gap" ? (firstQ.GapAnswer ?? "transformed") : "Answer");

        var answeredSnap = study.Answer(built.OpenSet.Id, firstQ.Id, testAnswer, false, "All");
        var submittedSnap = study.SubmitSet(built.OpenSet.Id, "All");
        Console.WriteLine($"Score after submit: {submittedSnap.OpenSet?.Score}/{submittedSnap.OpenSet?.Total}");

        // Student requests AI detailed explanation
        var explanation = await study.ExplainAsync(firstQ.Id);
        Console.WriteLine($"AI Explanation for Q1: {explanation}");
        Assert.False(string.IsNullOrWhiteSpace(explanation));
    }

    [Fact]
    public async Task Student_Runs_Curriculum_Tool_Lookup()
    {
        if (!TryBuildLlm(out var llm, out var model)) return;

        var study = new StudyService(_lessons, llm);
        var chat = study.ChatSnapshot(0, "");
        var snapshot = await study.RunToolAsync(chat.SelectedSessionId, "lookup", "Easter Island", "");

        var reply = snapshot.Messages.Last(m => m.Role == "assistant").Text;
        Console.WriteLine($"[STUDENT TEST 4 - TOOL LOOKUP EASTER ISLAND]\n{reply}\n");

        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.True(reply.Contains("Easter", StringComparison.OrdinalIgnoreCase)
                    || reply.Contains("Island", StringComparison.OrdinalIgnoreCase));
    }
}
