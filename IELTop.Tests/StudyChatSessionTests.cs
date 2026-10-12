using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Services.Ai;
using IELTop.Services.Learn;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// Chat session management and the no-model paths: pin, rename, and tools
/// without a configured model answer offline instead of failing late.
/// </summary>
[Collection("AppState")]
public sealed class StudyChatSessionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _lessonDir;
    private readonly LessonService _lessons = new();
    private readonly StudyService _study;

    public StudyChatSessionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_chat_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();

        _lessonDir = Path.Combine(Path.GetTempPath(), $"ieltop_chat_lessons_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_lessonDir);
        LessonService.SetUserDirForTesting(_lessonDir);
        File.WriteAllText(Path.Combine(_lessonDir, "unit-1.json"), """
        {
          "unit": "Unit 1",
          "title": "Unit 1: Grammar",
          "source": "chat test",
          "sections": [
            { "id": "g1", "skill": "Grammar", "title": "Relative Clauses", "topic": "Clauses",
              "isAnswerKey": false,
              "blocks": [ { "type": "text", "text": "A relative clause starts with who, which or that." } ] }
          ],
          "vocabulary": [], "slides": [], "audio": []
        }
        """, System.Text.Encoding.UTF8);
        _lessons.Reload();

        _study = new StudyService(_lessons, new LlmUnconfigured());
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        LessonService.SetUserDirForTesting(null);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        try { Directory.Delete(_lessonDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void PinSession_PinsAndSortsFirst()
    {
        var first = _study.NewChatSession(string.Empty);
        var second = _study.NewChatSession(string.Empty);

        var pinned = _study.PinSession(second.SelectedSessionId);
        var row = pinned.Sessions.First(s => s.Id == second.SelectedSessionId);
        Assert.True(row.Pinned);
        Assert.Equal(second.SelectedSessionId, pinned.Sessions.First().Id);

        var unpinned = _study.PinSession(second.SelectedSessionId);
        Assert.False(unpinned.Sessions.First(s => s.Id == second.SelectedSessionId).Pinned);
        _ = first;
    }

    [Fact]
    public void RenameSession_ChangesTitle()
    {
        var created = _study.NewChatSession(string.Empty);
        var renamed = _study.RenameSession(created.SelectedSessionId, "My grammar thread");
        Assert.Equal("My grammar thread", renamed.SelectedTitle);
    }

    [Fact]
    public async Task RunToolAsync_WithoutModel_ExplainsInsteadOfFailing()
    {
        var created = _study.NewChatSession(string.Empty);
        var snapshot = await _study.RunToolAsync(created.SelectedSessionId, "lookup", "relative clause", string.Empty);
        Assert.Contains("language model", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatSnapshot_EmptyQuestion_KeepsSession()
    {
        var created = _study.NewChatSession(string.Empty);
        var snapshot = await _study.AskAsync(created.SelectedSessionId, "   ", string.Empty);
        Assert.Equal(created.SelectedSessionId, snapshot.SelectedSessionId);
    }

    private sealed class LlmUnconfigured : ILlmService
    {
        public bool IsConfigured => false;
        public bool UseStreaming => false;
        public bool VisionEnabled => false;
        public Task<LlmResult> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => Task.FromResult(LlmResult.Fail("off"));
        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<LlmMessage> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<LlmResult> CompleteWithToolsAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            LlmToolExecutorAsync toolExecutor,
            Action<string, string>? onToolInvoked = null,
            Action<string>? onTokenChunk = null,
            CancellationToken ct = default)
            => Task.FromResult(LlmResult.Fail("off"));
        public Task<LlmResult> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(LlmResult.Fail("off"));
    }
}
