using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Storage;
using Xunit;

namespace IELTop.Tests;

[Collection("AppState")]
public sealed class ExamRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _tempUserDbPath;
    private readonly ExamRepository _repository = new();

    public ExamRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"ieltop_papers_{Guid.NewGuid():N}.db");
        _tempUserDbPath = Path.Combine(Path.GetTempPath(), $"ieltop_userpapers_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_tempDbPath);
        AppDbContext.SetCustomUserPapersPathForTesting(_tempUserDbPath);
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        AppDbContext.SetCustomUserPapersPathForTesting(null);
        foreach (var path in new[] { _tempDbPath, _tempUserDbPath })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                var shm = path + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
                var wal = path + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
            }
            catch { }
        }
    }

    private static ExamPaper SamplePaper(string title) => new()
    {
        Title = title,
        Source = "test",
        Category = "Academic",
        Level = "5.0-6.0",
        Tags = new List<string> { "test" },
        Parts = new List<ExamPart>
        {
            new()
            {
                Id = "R1",
                Skill = "Reading",
                Title = "Reading Passage 1",
                Topic = "Cities",
                TaskType = "Passage 1",
                Material = "Parks help cities breathe.",
                Instructions = "Read and answer.",
                Minutes = 10,
                Questions = new List<ExamQuestion>
                {
                    new()
                    {
                        Number = 1,
                        Kind = "choice",
                        Prompt = "What helps cities breathe?",
                        Options = new List<ExamOption>
                        {
                            new() { Key = "A", Text = "Parks" },
                            new() { Key = "B", Text = "Roads" },
                        },
                        CorrectKey = "A",
                        Explanation = "The passage says parks help cities breathe.",
                    },
                    new()
                    {
                        Number = 2,
                        Kind = "match",
                        Prompt = "Match each item.",
                        Bank = new List<string> { "Parks", "Roads" },
                        MatchRows = new List<ExamMatchRow>
                        {
                            new() { Label = "Green", Answer = "Parks" },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public void SavePaper_GetPaper_RoundTrip_PreservesContent()
    {
        var saved = _repository.SavePaper(SamplePaper("Repo Test Paper"));
        Assert.True(saved.Success);

        var loaded = _repository.GetPaper("repo test paper");
        Assert.NotNull(loaded);
        Assert.Equal("Repo Test Paper", loaded.Title);
        var part = Assert.Single(loaded.Parts);
        Assert.Equal("R1", part.Id);
        Assert.Equal(2, part.Questions.Count);
        var choice = part.Questions.First(q => q.Number == 1);
        Assert.Equal("A", choice.CorrectKey);
        Assert.Equal(2, choice.Options.Count);
        var match = part.Questions.First(q => q.Number == 2);
        Assert.Equal(new[] { "Parks", "Roads" }, match.Bank.ToArray());
        var row = Assert.Single(match.MatchRows);
        Assert.Equal("Green", row.Label);
        Assert.True(_repository.IsUserPaper("Repo Test Paper"));
    }

    [Fact]
    public void UpdatePaper_Rename_ReplacesOriginal()
    {
        Assert.True(_repository.SavePaper(SamplePaper("Old Title")).Success);
        var updated = SamplePaper("New Title");
        Assert.True(_repository.UpdatePaper("Old Title", updated).Success);

        Assert.Null(_repository.GetPaper("Old Title"));
        Assert.NotNull(_repository.GetPaper("New Title"));
    }

    [Fact]
    public void DeleteUserPaper_RemovesPaper()
    {
        Assert.True(_repository.SavePaper(SamplePaper("Delete Me")).Success);
        Assert.True(_repository.DeleteUserPaper("delete me"));
        Assert.Null(_repository.GetPaper("Delete Me"));
        Assert.False(_repository.DeleteUserPaper("Delete Me"));
    }

    [Fact]
    public void ImportPaper_InvalidJson_FailsWithReason()
    {
        var result = _repository.ImportPaper("not json");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void UserPapers_LiveApartFromMainDatabase()
    {
        Assert.True(_repository.SavePaper(SamplePaper("Separate File Paper")).Success);

        // The user file holds the paper...
        using var user = new AppDbContext(_tempUserDbPath);
        Assert.Equal(1, user.Connection.Table<StoredPaper>().Count());

        // ...while the main file keeps no paper tables at all.
        using var main = new AppDbContext(_tempDbPath);
        var tables = main.Connection
            .Query<SqliteMaster>("SELECT name AS Name FROM sqlite_master WHERE type = 'table'")
            .Select(t => t.Name)
            .ToList();
        Assert.DoesNotContain("ExamPapers", tables);
        Assert.DoesNotContain("ExamParts", tables);
        Assert.DoesNotContain("ExamQuestions", tables);
    }

    private sealed class SqliteMaster
    {
        public string Name { get; set; } = string.Empty;
    }
}
