using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IELTop.Services.Ai;
using IELTop.Services.Learn;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The lesson tests share one static lesson folder, so they run in sequence.
/// </summary>
[CollectionDefinition("LessonContent", DisableParallelization = true)]
public sealed class LessonContentCollection
{
}

/// <summary>
/// Checks what the tutor and the practice builder actually send to the model.
/// The point of Study is that it is grounded in the normalized lessons, so these
/// tests pin the context: the real section body, the history, the answer key of
/// the matching skill. They need no model and no window.
/// </summary>
[Collection("LessonContent")]
public sealed class StudyServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly LessonService _lessons = new();

    public StudyServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ieltop_study_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        LessonService.SetUserDirForTesting(_dir);

        var longGrammar = string.Join(" ",
            Enumerable.Repeat("A relative clause adds information about a noun.", 20)) +
            " The final note is the woman who called was my aunt.";

        File.WriteAllText(Path.Combine(_dir, "unit-1.json"), JsonSerializer.Serialize(new
        {
            unit = "Unit 1",
            title = "Unit 1",
            source = "test source",
            sections = new object[]
            {
                new
                {
                    id = "u1-grammar", skill = "Grammar", title = "U1 Grammar", isAnswerKey = false,
                    blocks = new object[] { new { type = "text", text = longGrammar } },
                },
                new
                {
                    id = "u1-grammar-keys", skill = "Grammar", title = "U1 Grammar Keys", isAnswerKey = true,
                    blocks = new object[] { new { type = "text", text = "Task: underline the verb. Answer: play, has, can." } },
                },
                new
                {
                    id = "u1-listening", skill = "Listening", title = "U1 Listening", isAnswerKey = false,
                    blocks = new object[] { new { type = "text", text = "Listen and fill in the cue card." } },
                },
                new
                {
                    id = "u1-listening-keys", skill = "Listening", title = "U1 Listening Keys", isAnswerKey = true,
                    blocks = new object[] { new { type = "text", text = "Transcript: the call was about a tracking number." } },
                },
            },
            vocabulary = new object[]
            {
                new { word = "aunt", form = "noun", meaning = "co, di", example = "my aunt called", ipa = "/aːnt/", derivatives = "-" },
            },
            slides = Array.Empty<object>(),
            audio = Array.Empty<string>(),
        }), System.Text.Encoding.UTF8);

        _lessons.Reload();
    }

    public void Dispose()
    {
        LessonService.SetUserDirForTesting(null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Search_hit_carries_the_whole_section_body_not_only_the_preview()
    {
        var hit = _lessons.Search("relative clause").First();

        // The preview is a short window; the body is what the model receives.
        Assert.True(hit.Body.Length > hit.Snippet.Length,
            "the body should be longer than the 320 character preview");
        Assert.Contains("the woman who called was my aunt", hit.Body);
        Assert.True(hit.Body.Length > 600, "the long section should not be cut at the preview width");
    }

    [Fact]
    public void SourceText_sends_the_section_body_to_the_model()
    {
        var hits = _lessons.Search("relative clause");
        var text = StudyService.BuildSourceText(hits);

        Assert.Contains("U1 Grammar", text);
        Assert.Contains("the woman who called was my aunt", text);
    }

    [Fact]
    public void Chat_messages_carry_history_and_the_question_with_sources()
    {
        var history = new List<StudyMessageRow>
        {
            new(1, "user", "What is a relative clause?", Array.Empty<StudySource>(), DateTime.UtcNow),
            new(2, "assistant", "It adds information about a noun.", Array.Empty<StudySource>(), DateTime.UtcNow),
        };

        var messages = StudyService.BuildChatMessages("Give an example", "SOURCE BODY HERE", history);

        Assert.Equal("system", messages[0].Role);
        Assert.Equal(4, messages.Count);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("assistant", messages[2].Role);
        Assert.Contains("SOURCE BODY HERE", messages[3].Content);
        Assert.Contains("Give an example", messages[3].Content);
    }

    [Fact]
    public void Practice_material_for_grammar_includes_the_answer_key_of_the_same_skill()
    {
        var unit = _lessons.GetUnit("unit-1")!;
        var material = StudyService.BuildPracticeMaterial(_lessons, unit, "unit-1", "Grammar");

        Assert.Contains("U1 Grammar", material);
        // The grammar key holds the original task and its answers: it must be sent.
        Assert.Contains("U1 Grammar Keys", material);
        Assert.Contains("play, has, can", material);
        // A key for another skill must not leak in.
        Assert.DoesNotContain("U1 Listening Keys", material);
        Assert.DoesNotContain("tracking number", material);
    }

    [Fact]
    public void Practice_material_for_vocabulary_uses_the_vocabulary_sheet()
    {
        var unit = _lessons.GetUnit("unit-1")!;
        var material = StudyService.BuildPracticeMaterial(_lessons, unit, "unit-1", "Vocabulary");

        Assert.Contains("aunt", material);
        Assert.Contains("co, di", material);
    }
}
