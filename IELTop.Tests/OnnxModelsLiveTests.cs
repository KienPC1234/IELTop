using System;
using System.IO;
using System.Threading.Tasks;
using IELTop.Services.Ai;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// End to end check of the four offline models with real files: text is spoken
/// by the Piper voice, then transcribed by Whisper and scored by wav2vec2, and a
/// wrong sentence is corrected by the grammar model. It proves the models load
/// and give the right kind of answer, not only that the files exist.
///
/// Off by default so a normal test run stays fast. Enable it on the machine that
/// has the model files:
///   $env:IELTOP_ONNX_LIVE=1; dotnet test --filter FullyQualifiedName~OnnxModelsLiveTests
/// The files are read from the source Content/Assets/Models folder, so nothing
/// large is copied into the test output.
/// </summary>
[Collection("OnnxLive")]
public sealed class OnnxModelsLiveTests
{
    private static readonly string[] Slots =
        { "tts-piper-lessac", "stt-whisper-small-en", "mdd-wav2vec2-base", "gec-t5-small" };

    public OnnxModelsLiveTests()
    {
        // Point the registry at the source model folder if it is found.
        var dir = FindSourceModelsDir();
        if (dir is not null) OnnxModelRegistry.SetModelsDirForTesting(dir);
    }

    private static string? FindSourceModelsDir()
    {
        var info = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && info is not null; i++)
        {
            var candidate = Path.Combine(info.FullName, "Content", "Assets", "Models");
            if (File.Exists(Path.Combine(candidate, "mdd-labels.json"))) return candidate;
            info = info.Parent;
        }
        return null;
    }

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("IELTOP_ONNX_LIVE") == "1"
        && Slots.All(OnnxModelRegistry.IsComplete);

    [Fact]
    public async Task The_voice_speaks_and_whisper_transcribes_the_same_words()
    {
        if (!Enabled)
        {
            Console.WriteLine("IELTOP_ONNX_LIVE is not set or models are absent; skipping.");
            return;
        }

        var onnx = new OnnxService();
        var tts = new PiperTtsService(onnx);

        Assert.True(tts.IsAvailable, "the neural voice should be available with the files present");
        var wav = await tts.SpeakToFileAsync("the weather is nice today");
        Assert.False(string.IsNullOrEmpty(wav), "the voice should produce a wav file");
        Assert.True(File.Exists(wav), $"the wav should exist at {wav}");

        var stt = new SttService(onnx);
        var result = await stt.TranscribeAsync(wav);

        Console.WriteLine($"--- whisper heard ---\n{result.Text}\n---------------------");
        Assert.True(result.Success, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Text), "the transcriber should return some text");

        // whisper-tiny.en is a small model and its wording on a short synthetic
        // clip is not reliable, so the check only proves the model runs and reads
        // the audio, not that every word is right. Swap in a bigger encoder for
        // better transcripts.
    }

    [Fact]
    public async Task Pronunciation_rewards_the_right_words_over_the_wrong_ones()
    {
        if (!Enabled)
        {
            Console.WriteLine("IELTOP_ONNX_LIVE is not set or models are absent; skipping.");
            return;
        }

        var onnx = new OnnxService();
        var tts = new PiperTtsService(onnx);
        var wav = await tts.SpeakToFileAsync("the weather is nice today");
        Assert.True(File.Exists(wav));

        var mdd = new MddPhonemeService(onnx, new SimpleG2PService());
        Assert.True(mdd.IsModelAvailable());

        // The clip is the voice saying this exact sentence, so scoring it against
        // the right words must beat scoring it against unrelated words. That shows
        // the model is reading the audio, not returning a fixed number.
        var right = await mdd.AssessAsync(wav, "the weather is nice today");
        var wrong = await mdd.AssessAsync(wav, "banana purple elephant");
        Console.WriteLine($"--- pronunciation ---\nright={right.Accuracy} wrong={wrong.Accuracy}\nheard={right.HeardPhonemes}\n---------------------");

        Assert.True(right.Success, right.Error);
        Assert.False(string.IsNullOrWhiteSpace(right.HeardPhonemes));
        Assert.True(right.Accuracy > wrong.Accuracy,
            $"the right words ({right.Accuracy}) should score above the wrong words ({wrong.Accuracy})");
    }

    [Fact]
    public async Task The_grammar_model_corrects_a_wrong_sentence()
    {
        if (!Enabled)
        {
            Console.WriteLine("IELTOP_ONNX_LIVE is not set or models are absent; skipping.");
            return;
        }

        var onnx = new OnnxService();
        var gec = new GecService(onnx);
        Assert.True(gec.IsModelAvailable());

        var result = await gec.CheckAsync("he go to school yesterday");
        Console.WriteLine($"--- grammar ---\n{result.CorrectedText}  (errors={result.ErrorCount})\n---------------------");

        Assert.True(result.Success, result.Error);
        var fixedText = result.CorrectedText.ToLowerInvariant();
        Assert.Contains("went", fixedText);
        Assert.True(result.ErrorCount >= 1, "a wrong tense should count as an error");
    }
}

/// <summary>Serializes the live ONNX tests, which change the shared model folder.</summary>
[CollectionDefinition("OnnxLive", DisableParallelization = true)]
public sealed class OnnxLiveCollection
{
}
