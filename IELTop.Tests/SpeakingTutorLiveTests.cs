using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Data;
using IELTop.Models;
using IELTop.Services.Ai;
using IELTop.Services.App;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The tutor scoring pipeline with real models: the Piper voice reads a cue,
/// the tutor scores it like a student recording (transcript, rhythm, GOP),
/// and the attempt lands in the database. Gated like the other live tests:
///   $env:IELTOP_ONNX_LIVE=1; dotnet test --filter FullyQualifiedName~SpeakingTutorLiveTests
/// </summary>
[Collection("OnnxLive")]
public sealed class SpeakingTutorLiveTests : IDisposable
{
    private readonly string _dbPath;

    public SpeakingTutorLiveTests()
    {
        var info = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && info is not null; i++)
        {
            var candidate = Path.Combine(info.FullName, "Content", "Assets", "Models");
            if (File.Exists(Path.Combine(candidate, "mdd-labels.json")))
            {
                OnnxModelRegistry.SetModelsDirForTesting(candidate);
                break;
            }
            info = info.Parent;
        }

        _dbPath = Path.Combine(Path.GetTempPath(), $"ieltop_tutorlive_{Guid.NewGuid():N}.db");
        AppDbContext.SetCustomPathForTesting(_dbPath);
        AppDbContext.EnsureCreatedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        AppDbContext.SetCustomPathForTesting(null);
        OnnxModelRegistry.SetModelsDirForTesting(null);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("IELTOP_ONNX_LIVE") == "1"
        && OnnxModelRegistry.IsComplete("tts-piper-lessac")
        && OnnxModelRegistry.IsComplete(OnnxModelRegistry.ResolveSttSlot())
        && OnnxModelRegistry.IsComplete("mdd-wav2vec2-base");

    [Fact]
    public async Task The_tutor_scores_a_spoken_line_end_to_end()
    {
        if (!Enabled)
        {
            Console.WriteLine("IELTOP_ONNX_LIVE is not set or models are absent; skipping.");
            return;
        }

        using var onnx = new OnnxService();
        var tutor = new SpeakingTutorService(
            new LlmOff(), new SttService(onnx), new MddPhonemeService(onnx, new SimpleG2PService()));
        var session = tutor.NewSession("ReadAloud");

        const string line = "The weather is nice today.";
        var tts = new PiperTtsService(onnx);
        var wavPath = await tts.SpeakToFileAsync(line, CancellationToken.None);
        Assert.True(File.Exists(wavPath), "the voice should produce a wav file");
        string base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(wavPath));

        var (snap, words) = await tutor.SubmitReadAloudAsync(
            session.SelectedSessionId, line, base64, CancellationToken.None);

        Assert.Empty(snap.StatusMessage);
        Assert.NotNull(snap.OpenAttempt);
        Assert.True(snap.OpenAttempt.PronunciationAccuracy > 0);
        Assert.NotEmpty(words);
        var saved = await AppDbContext.TableAsync<TutorSpeakingAttempt>().ToListAsync();
        Assert.Single(saved);

        var answer = await tutor.SubmitAnswerAsync(
            session.SelectedSessionId, "Part1", "Weather: Is it nice today?", base64, CancellationToken.None);
        Assert.Empty(answer.StatusMessage);
        Assert.NotNull(answer.OpenAttempt);
        Assert.False(string.IsNullOrWhiteSpace(answer.OpenAttempt.Transcript));
        Assert.True(answer.OpenAttempt.Clarity > 0);
        Assert.True(answer.OpenAttempt.SpeechSeconds > 0);
    }
}
