using System;
using System.Collections.Generic;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using Xunit;

namespace IELTop.Tests;

/// <summary>
/// The two measurements behind the speaking score that do not come from text:
/// voice activity (how much of the clip is speech, how many pauses) and GOP
/// (how sure the phoneme model was about each sound). Both are deterministic
/// on synthetic audio, so they are tested without any model file.
/// </summary>
public sealed class SpeechMeasurementTests
{
    private static float[] Tone(double seconds, double freq = 440.0, double amplitude = 0.5)
    {
        int n = (int)(16000 * seconds);
        var samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / 16000));
        return samples;
    }

    private static float[] Silence(double seconds) => new float[(int)(16000 * seconds)];

    private static float[] Concat(params float[][] parts)
    {
        var all = new List<float>();
        foreach (var part in parts) all.AddRange(part);
        return all.ToArray();
    }

    [Fact]
    public void Silence_has_no_speech_and_no_pauses()
    {
        var timing = SpeechTimingAnalyzer.Analyze(Silence(2.0));

        Assert.Equal(0, timing.SpeechSeconds);
        Assert.Equal(0, timing.PauseCount);
        Assert.Equal(0, timing.SpeechRatio);
    }

    [Fact]
    public void Continuous_tone_is_all_speech_with_no_pause()
    {
        var timing = SpeechTimingAnalyzer.Analyze(Tone(2.0));

        Assert.True(timing.SpeechSeconds >= 1.9, $"speech was {timing.SpeechSeconds}s of 2s");
        Assert.Equal(0, timing.PauseCount);
        Assert.True(timing.SpeechRatio > 0.95);
    }

    [Fact]
    public void Two_tone_bursts_with_a_gap_count_one_pause()
    {
        // 1s tone, 0.6s silence, 1s tone: one pause of about 0.6s.
        var timing = SpeechTimingAnalyzer.Analyze(Concat(Tone(1.0), Silence(0.6), Tone(1.0)));

        Assert.Equal(1, timing.PauseCount);
        Assert.True(timing.MeanPauseSeconds >= 0.4 && timing.MeanPauseSeconds <= 0.8,
            $"mean pause was {timing.MeanPauseSeconds}s");
        Assert.True(timing.SpeechSeconds >= 1.8 && timing.SpeechSeconds <= 2.2,
            $"speech was {timing.SpeechSeconds}s");
    }

    [Fact]
    public void A_short_dip_does_not_count_as_a_pause()
    {
        // A 100 ms dip is coarticulation, not hesitation.
        var timing = SpeechTimingAnalyzer.Analyze(Concat(Tone(1.0), Silence(0.1), Tone(1.0)));

        Assert.Equal(0, timing.PauseCount);
    }

    [Fact]
    public void Gop_is_one_when_the_sound_won_every_frame()
    {
        // Gaps of 0 mean the expected sound was the best every frame.
        Assert.Equal(1.0, MddPhonemeService.SegmentGop(new[] { 0.0, 0.0, 0.0 }));
    }

    [Fact]
    public void Gop_falls_as_the_sound_loses_frames()
    {
        double sure = MddPhonemeService.SegmentGop(new[] { 0.0, -0.1, 0.0 });
        double shaky = MddPhonemeService.SegmentGop(new[] { -1.0, -2.0, -3.0 });
        double absent = MddPhonemeService.SegmentGop(new[] { -10.0, -25.0 });

        Assert.True(sure > 0.9, $"sure was {sure}");
        Assert.True(shaky > 0 && shaky < 0.5, $"shaky was {shaky}");
        Assert.Equal(0, absent);
        Assert.Equal(0, MddPhonemeService.SegmentGop(Array.Empty<double>()));
    }

    [Fact]
    public void Alignment_marks_substitutions_omissions_and_insertions()
    {
        var edits = MddPhonemeService.Align(
            new[] { "h", "ə", "l", "oʊ" },
            new[] { "h", "æ", "l", "oʊ", "w" });

        // h correct, ə->æ substitution, l correct, oʊ correct, w insertion.
        Assert.Equal(5, edits.Count);
        Assert.Contains(edits, e => e.Type == PhonemeErrorType.Correct && e.Expected == "h");
        Assert.Contains(edits, e => e.Type == PhonemeErrorType.Substitution && e.Expected == "ə" && e.Heard == "æ");
        Assert.Contains(edits, e => e.Type == PhonemeErrorType.Insertion && e.Heard == "w");
    }
}
