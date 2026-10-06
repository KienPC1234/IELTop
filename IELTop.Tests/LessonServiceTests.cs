using System;
using System.IO;
using System.Text.Json;
using IELTop.Services.Learn;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The lesson reader and its search are pure logic, so they are tested without
/// a window. A tiny unit file is written to a temp folder that the service is
/// told to read, so the shipped lesson folder is never touched.
/// </summary>
[Collection("LessonContent")]
public sealed class LessonServiceTests : IDisposable
{    private readonly string _dir;
    private readonly LessonService _lessons = new();

    public LessonServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ieltop_lesson_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        LessonService.SetUserDirForTesting(_dir);
        _lessons.Reload();
    }

    public void Dispose()
    {
        LessonService.SetUserDirForTesting(null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void WriteUnit(string slug, object unit) =>
        File.WriteAllText(
            Path.Combine(_dir, slug + ".json"),
            JsonSerializer.Serialize(unit),
            System.Text.Encoding.UTF8);

    private static object SampleUnit() => new
    {
        unit = "Unit 9",
        title = "Unit 9",
        source = "test",
        sections = new object[]
        {
            new
            {
                id = "u9-grammar",
                skill = "Grammar",
                title = "U9 Grammar",
                isAnswerKey = false,
                blocks = new object[]
                {
                    new { type = "text", text = "Relative clauses add information about a noun. The woman who called was my aunt." },
                },
            },
            new
            {
                id = "u9-keys",
                skill = "Grammar",
                title = "U9 Grammar Keys",
                isAnswerKey = true,
                blocks = new object[] { new { type = "text", text = "Answers here." } },
            },
        },
        vocabulary = new object[]
        {
            new { word = "aunt", form = "noun", meaning = "co, di", example = "my aunt", ipa = "/aːnt/", derivatives = "-" },
        },
        slides = new object[]
        {
            new
            {
                file = "Lesson 9.pptx",
                slides = new object[]
                {
                    new { index = 1, text = "Photosynthesis converts light into chemical energy in green leaves." },
                },
            },
        },
        audio = new[] { "Lessons/unit-9/Audio 9.1.mp3" },
    };

    [Fact]
    public void Units_LoadFromTheUserFolder()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        Assert.Single(_lessons.Units);
        var unit = _lessons.Units[0];
        Assert.Equal("Unit 9", unit.Unit);
        Assert.Equal("unit-9", unit.Slug);
        Assert.Equal(2, unit.Sections.Count);
        Assert.Single(unit.Vocabulary);
        Assert.Equal("Lessons/unit-9/Audio 9.1.mp3", unit.Audio[0]);
    }

    [Fact]
    public void Search_IsCaseInsensitive_AndFindsTheSection()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        var hits = _lessons.Search("RELATIVE CLAUSES");

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.SectionTitle == "U9 Grammar");
    }

    [Fact]
    public void Search_ReturnsNothingForAnUnrelatedWord()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        Assert.Empty(_lessons.Search("xenotransplantation"));
    }

    [Fact]
    public void Search_CanBeLimitedToOneUnit()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        Assert.NotEmpty(_lessons.Search("relative", unitSlug: "unit-9"));
        Assert.Empty(_lessons.Search("relative", unitSlug: "unit-99"));
    }

    [Fact]
    public void Vocabulary_IsFoundByWordAndByMeaning()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        Assert.Single(_lessons.Vocabulary("aunt"));
        Assert.Single(_lessons.Vocabulary("di"));
        Assert.Empty(_lessons.Vocabulary("zebra"));
    }

    [Fact]
    public void Slides_are_searchable_and_carry_their_text_as_the_body()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        var hits = _lessons.Search("photosynthesis");
        var slideHit = hits.FirstOrDefault(h => h.Skill == "Slides");

        Assert.NotNull(slideHit);
        Assert.Contains("Lesson 9", slideHit!.SectionTitle);
        Assert.Contains("Photosynthesis", slideHit.Body);
    }

    [Fact]
    public void The_section_really_about_the_query_outranks_a_passing_mention()
    {
        WriteUnit("unit-9", SampleUnit());
        _lessons.Reload();

        // "aunt" appears once in the vocabulary sheet; the grammar section is
        // entirely about relative clauses, so it must rank first.
        var hits = _lessons.Search("relative clauses noun");
        Assert.NotEmpty(hits);
        Assert.Equal("U9 Grammar", hits[0].SectionTitle);
    }

    [Fact]
    public void CosineSimilarity_ComputesCorrectDotProduct()
    {
        var v1 = new float[] { 1f, 0f, 0f };
        var v2 = new float[] { 1f, 0f, 0f };
        var v3 = new float[] { 0f, 1f, 0f };

        Assert.Equal(1f, LessonService.CosineSimilarity(v1, v2));
        Assert.Equal(0f, LessonService.CosineSimilarity(v1, v3));
    }

    [Fact]
    public void GetSection_ResolvesSectionAndPairedKey()
    {
        var unit = new
        {
            unit = "Unit 9",
            title = "Unit 9",
            sections = new object[]
            {
                new { id = "u9-read", title = "U9 Reading", isAnswerKey = false, keySectionId = "u9-read-keys", blocks = Array.Empty<object>() },
                new { id = "u9-read-keys", title = "U9 Reading Keys", isAnswerKey = true, targetSectionId = "u9-read", blocks = Array.Empty<object>() }
            }
        };
        WriteUnit("unit-9", unit);
        _lessons.Reload();

        var (sec, key, loadedUnit) = _lessons.GetSection("unit-9", "u9-read");
        Assert.NotNull(sec);
        Assert.NotNull(key);
        Assert.Equal("u9-read-keys", key!.Id);
    }
}
