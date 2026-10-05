using System;
using System.Collections.Generic;

namespace IELTop.Services.Audio;

/// <summary>
/// What the rhythm of a recording looks like: how much of it is speech, how
/// many pauses it has, and how long they are. Used for the fluency part of a
/// speaking score, so hesitation is measured from the audio instead of guessed
/// from the transcript text.
/// </summary>
public sealed record SpeechTiming(
    double TotalSeconds,
    double SpeechSeconds,
    int PauseCount,
    double MeanPauseSeconds,
    double LongestPauseSeconds)
{
    /// <summary>Share of the clip that is speech, 0 to 1.</summary>
    public double SpeechRatio => TotalSeconds <= 0 ? 0 : SpeechSeconds / TotalSeconds;

    public static SpeechTiming Empty(double totalSeconds) =>
        new(totalSeconds, 0, 0, 0, 0);
}

/// <summary>
/// Energy voice activity detection on 16 kHz mono audio. Frames of 20 ms are
/// speech when their RMS energy clears a threshold relative to the clip peak;
/// short gaps are merged into speech (hangover) and only silences of 250 ms or
/// more count as pauses. No model, no network, deterministic.
/// </summary>
public static class SpeechTimingAnalyzer
{
    private const int SampleRate = 16000;
    private const int FrameSamples = SampleRate / 50; // 20 ms
    private const int HangoverFrames = 5; // 100 ms of dip stays speech
    private const int MinPauseFrames = 13; // ~250 ms counts as a pause

    /// <summary>Timing of 16 kHz mono samples in -1..1.</summary>
    public static SpeechTiming Analyze(IReadOnlyList<float> samples)
    {
        double totalSeconds = samples.Count / (double)SampleRate;
        if (samples.Count < FrameSamples)
            return SpeechTiming.Empty(totalSeconds);

        int frames = samples.Count / FrameSamples;
        var energy = new double[frames];
        double peak = 0;
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int i = 0; i < FrameSamples; i++)
            {
                float s = samples[f * FrameSamples + i];
                sum += s * s;
            }
            energy[f] = Math.Sqrt(sum / FrameSamples);
            if (energy[f] > peak) peak = energy[f];
        }

        // Relative threshold with a floor, so a quiet but clean recording still
        // separates speech from room tone, and pure silence stays silent.
        double threshold = Math.Max(peak * 0.05, 0.005);
        if (peak < 0.005)
            return SpeechTiming.Empty(totalSeconds);

        var speech = new bool[frames];
        for (int f = 0; f < frames; f++)
            speech[f] = energy[f] >= threshold;

        // Hangover: bridge dips shorter than 100 ms so one word with a stop
        // consonant does not split into two speech runs.
        int silent = 0;
        for (int f = 0; f < frames; f++)
        {
            if (speech[f]) { silent = 0; continue; }
            silent++;
            if (silent <= HangoverFrames)
                speech[f] = true;
        }

        double speechSeconds = 0;
        int pauses = 0;
        double pauseTotal = 0;
        double longest = 0;
        int run = 0;
        for (int f = 0; f < frames; f++)
        {
            if (speech[f])
            {
                speechSeconds += 0.02;
                if (run >= MinPauseFrames)
                {
                    pauses++;
                    double seconds = run * 0.02;
                    pauseTotal += seconds;
                    if (seconds > longest) longest = seconds;
                }
                run = 0;
            }
            else
            {
                run++;
            }
        }
        // A trailing silence is a pause too when it is long enough.
        if (run >= MinPauseFrames)
        {
            pauses++;
            double seconds = run * 0.02;
            pauseTotal += seconds;
            if (seconds > longest) longest = seconds;
        }

        return new SpeechTiming(
            totalSeconds,
            Math.Round(speechSeconds, 2),
            pauses,
            pauses == 0 ? 0 : Math.Round(pauseTotal / pauses, 2),
            Math.Round(longest, 2));
    }
}
