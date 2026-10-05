using System.Linq;
using IELTop.Services.Ai;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The model registry is the single list the app trusts. If a service names a
/// slot that is not here, or a slot does not declare every support file it
/// needs, the model can never load and the failure is silent. These checks are
/// file independent, so they run anywhere.
/// </summary>
public sealed class OnnxRegistryTests
{
    // Every service that loads a model declares one of these names.
    private static readonly string[] UsedSlots =
    {
        MddPhonemeService.SlotName,
        "stt-whisper-small-en",
        "stt-whisper-base-en",
        GecService.SlotName,
        PiperTtsService.SlotName,
    };

    [Fact]
    public void Every_service_slot_is_declared_in_the_registry()
    {
        foreach (var name in UsedSlots)
        {
            Assert.Contains(OnnxModelRegistry.Slots, s => s.Name == name);
        }
    }

    [Fact]
    public void IsComplete_means_every_declared_file_exists()
    {
        foreach (var slot in OnnxModelRegistry.Slots)
        {
            var missing = OnnxModelRegistry.MissingFiles(slot.Name);
            Assert.Equal(missing.Count == 0, OnnxModelRegistry.IsComplete(slot.Name));
        }
    }

    [Fact]
    public void The_gec_and_tts_slots_declare_their_support_files()
    {
        var gec = OnnxModelRegistry.Slots.Single(s => s.Name == "gec-t5-small");
        Assert.Equal("gec-t5-small-decoder-int8.onnx", gec.ExtraFile);
        Assert.Contains("gec-t5-spiece.model", gec.ExtraFiles!);

        var tts = OnnxModelRegistry.Slots.Single(s => s.Name == "tts-piper-lessac");
        Assert.Equal("tts-piper-lessac-medium.onnx.json", tts.ExtraFile);
    }

    [Fact]
    public void An_unknown_slot_is_never_complete()
    {
        Assert.Empty(OnnxModelRegistry.PathOf("no-such-slot"));
        Assert.False(OnnxModelRegistry.IsComplete("no-such-slot"));
    }

    [Theory]
    [InlineData(100, 9.0)]    [InlineData(92, 8.5)]
    [InlineData(80, 7.5)]
    [InlineData(75, 7.0)]
    [InlineData(60, 6.0)]
    [InlineData(50, 5.5)]
    [InlineData(45, 5.0)]
    [InlineData(10, 4.0)]
    public void A_measured_pronunciation_score_maps_to_a_band(double accuracy, double expected)
    {
        Assert.Equal(expected, IELTop.Services.Exam.ExamEngine.PronunciationBandFor(accuracy));
    }

    [Fact]
    public void An_unmeasured_pronunciation_score_is_left_to_the_model()
    {
        // 0 means "not measured", so it must not force the lowest band.
        Assert.Equal(4.0, IELTop.Services.Exam.ExamEngine.PronunciationBandFor(0));
    }

    [Fact]
    public void Stt_slots_are_ordered_best_first_and_resolve_to_one_of_them()
    {
        var order = OnnxModelRegistry.SttSlotsByQuality;
        Assert.Equal(new[] { "stt-whisper-small-en", "stt-whisper-base-en" }, order);

        // Whatever is installed, the resolved slot is one of the declared ones.
        var resolved = OnnxModelRegistry.ResolveSttSlot();
        Assert.Contains(resolved, order);
    }

    [Fact]
    public void Speaking_submits_and_scores_after_recording_by_default()
    {
        Assert.True(new IELTop.Services.Storage.AppSettings().SpeakingAutoSubmit);
    }
}
