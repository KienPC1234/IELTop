namespace IELTop.Services.Audio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed record AudioSentenceSegment(
    int Index,
    string Sentence,
    double StartSeconds,
    double EndSeconds,
    string AudioWavBase64,
    double Gop,
    bool NeedsPractice,
    string PracticePrompt);

public static class AudioSegmenter
{
    private const int SampleRate = 16000;

    /// <summary>
    /// Slices audio samples into sentence-aligned segments based on transcript sentences and speech timing.
    /// Returns a list of segments with embedded Base64 WAV data ready for browser playback.
    /// </summary>
    public static IReadOnlyList<AudioSentenceSegment> SegmentByTranscript(
        float[] samples, string transcript, double overallGop = 0.0)
    {
        if (samples.Length == 0 || string.IsNullOrWhiteSpace(transcript))
            return Array.Empty<AudioSentenceSegment>();

        double totalDuration = samples.Length / (double)SampleRate;

        // Split transcript into discrete sentences
        var rawSentences = Regex.Split(transcript.Trim(), @"(?<=[.?!])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        if (rawSentences.Count == 0)
        {
            rawSentences = new List<string> { transcript.Trim() };
        }

        // Calculate speech frames using 20ms energy VAD
        var speechRuns = FindSpeechRuns(samples);
        var segments = new List<AudioSentenceSegment>();

        if (rawSentences.Count == 1 || speechRuns.Count == 0)
        {
            string wavBase64 = EncodeWavBase64(samples, 0, samples.Length);
            segments.Add(new AudioSentenceSegment(
                1,
                rawSentences[0],
                0.0,
                Math.Round(totalDuration, 2),
                wavBase64,
                overallGop,
                overallGop > 0 && overallGop < 0.65,
                overallGop > 0 && overallGop < 0.65
                    ? "Shadowing: Listen to this sentence and repeat to improve phoneme clarity."
                    : "Repeat: Practice this sentence with natural rhythm and intonation."));
            return segments;
        }

        int totalWords = rawSentences.Sum(s => CountWords(s));
        if (totalWords <= 0) totalWords = 1;

        double currentStartSec = 0.0;
        for (int i = 0; i < rawSentences.Count; i++)
        {
            string sentence = rawSentences[i];
            int words = CountWords(sentence);
            double share = words / (double)totalWords;
            double targetDuration = totalDuration * share;

            double endSec = (i == rawSentences.Count - 1)
                ? totalDuration
                : Math.Min(totalDuration, currentStartSec + targetDuration);

            endSec = SnapToNearestSilence(endSec, samples, currentStartSec, totalDuration);

            int startSample = (int)Math.Clamp(currentStartSec * SampleRate, 0, samples.Length - 1);
            int endSample = (int)Math.Clamp(endSec * SampleRate, startSample + 1, samples.Length);
            int length = endSample - startSample;

            string wavBase64 = EncodeWavBase64(samples, startSample, length);

            bool needsPractice = false;
            string prompt = "Repeat: Practice speaking this sentence smoothly.";

            if (overallGop > 0 && overallGop < 0.60)
            {
                needsPractice = true;
                prompt = "Phoneme clarity: Shadow this sentence to produce clearer vowel and consonant sounds.";
            }
            else if (length / (double)SampleRate > 6.0 && words <= 8)
            {
                needsPractice = true;
                prompt = "Fluency drill: Reduce hesitation and speak this sentence at a natural pace (120-140 wpm).";
            }

            segments.Add(new AudioSentenceSegment(
                i + 1,
                sentence,
                Math.Round(currentStartSec, 2),
                Math.Round(endSec, 2),
                wavBase64,
                overallGop,
                needsPractice,
                prompt));

            currentStartSec = endSec;
        }

        return segments;
    }

    private static int CountWords(string s) =>
        s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<(int StartFrame, int EndFrame)> FindSpeechRuns(float[] samples)
    {
        const int frameSize = SampleRate / 50; // 20ms
        int frames = samples.Length / frameSize;
        if (frames == 0) return new List<(int, int)>();

        var energy = new double[frames];
        double peak = 0;
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int i = 0; i < frameSize; i++)
            {
                float s = samples[f * frameSize + i];
                sum += s * s;
            }
            energy[f] = Math.Sqrt(sum / frameSize);
            if (energy[f] > peak) peak = energy[f];
        }

        double threshold = Math.Max(peak * 0.05, 0.005);
        var runs = new List<(int, int)>();
        int runStart = -1;

        for (int f = 0; f < frames; f++)
        {
            bool isSpeech = energy[f] >= threshold;
            if (isSpeech && runStart < 0)
            {
                runStart = f;
            }
            else if (!isSpeech && runStart >= 0)
            {
                runs.Add((runStart, f));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            runs.Add((runStart, frames));
        }

        return runs;
    }

    private static double SnapToNearestSilence(double targetSec, float[] samples, double minSec, double maxSec)
    {
        const int frameSize = SampleRate / 50; // 20ms
        int centerFrame = (int)(targetSec * 50);
        int window = 25; // look +/- 500ms
        int minFrame = (int)(minSec * 50) + 10;
        int maxFrame = (int)(maxSec * 50) - 5;

        double minEnergy = double.MaxValue;
        int bestFrame = centerFrame;

        for (int f = Math.Max(minFrame, centerFrame - window); f <= Math.Min(maxFrame, centerFrame + window); f++)
        {
            int start = f * frameSize;
            if (start + frameSize > samples.Length) break;

            double sum = 0;
            for (int i = 0; i < frameSize; i++)
            {
                float s = samples[start + i];
                sum += s * s;
            }
            if (sum < minEnergy)
            {
                minEnergy = sum;
                bestFrame = f;
            }
        }

        return bestFrame * 0.02;
    }

    public static string EncodeWavBase64(float[] samples, int offset, int count)
    {
        if (offset < 0) offset = 0;
        if (count <= 0 || offset >= samples.Length) return string.Empty;
        count = Math.Min(count, samples.Length - offset);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        short channels = 1;
        short bits = 16;
        int dataSize = count * sizeof(short);
        int subChunk1Size = 16;
        short audioFormat = 1; // PCM
        int byteRate = SampleRate * channels * (bits / 8);
        short blockAlign = (short)(channels * (bits / 8));

        // Header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(subChunk1Size);
        writer.Write(audioFormat);
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bits);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        // Samples
        for (int i = 0; i < count; i++)
        {
            float s = Math.Clamp(samples[offset + i], -1.0f, 1.0f);
            short pcm = (short)(s * 32767.0f);
            writer.Write(pcm);
        }

        writer.Flush();
        return Convert.ToBase64String(ms.ToArray());
    }
}
