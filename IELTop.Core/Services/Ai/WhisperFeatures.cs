namespace IELTop.Services.Ai;

/// <summary>
/// Log-mel features exactly like the whisper feature extractor:
/// 16 kHz mono, reflect pad, 400 point DFT with periodic Hann window,
/// 80 Slaney mel filters with area norm, log10, top 8 dB kept, scaled.
/// Output is [80, 3000] for a 30 second window.
/// </summary>
public static class WhisperFeatures
{
    public const int SampleRate = 16000;
    public const int WindowSeconds = 30;
    public const int SamplesPerWindow = SampleRate * WindowSeconds;
    public const int FramesPerWindow = 3000;

    private const int NFft = 400;
    private const int Hop = 160;
    private const int NMel = 80;
    private const int NBins = NFft / 2 + 1;

    private static readonly double[,] Mel = BuildMel();
    private static readonly double[] Hann = BuildHann();
    private static readonly double[,] Cos = new double[NBins, NFft];
    private static readonly double[,] Sin = new double[NBins, NFft];

    static WhisperFeatures()
    {
        for (int k = 0; k < NBins; k++)
            for (int n = 0; n < NFft; n++)
            {
                double angle = 2.0 * Math.PI * k * n / NFft;
                Cos[k, n] = Math.Cos(angle);
                Sin[k, n] = Math.Sin(angle);
            }
    }

    /// <summary>Pads or trims PCM to one 30 second window, returns [80, 3000].</summary>
    public static float[,] LogMel(float[] pcm)
    {
        var window = new float[SamplesPerWindow];
        Array.Copy(pcm, window, Math.Min(pcm.Length, SamplesPerWindow));

        // Reflect pad 200 samples on each side, like center=True framing.
        int paddedLen = SamplesPerWindow + NFft;
        var padded = new double[paddedLen];
        for (int i = 0; i < 200; i++) padded[i] = window[200 - i];
        for (int i = 0; i < SamplesPerWindow; i++) padded[200 + i] = window[i];
        for (int j = 0; j < 200; j++) padded[200 + SamplesPerWindow + j] = window[SamplesPerWindow - 2 - j];

        int rawFrames = 1 + (paddedLen - NFft) / Hop; // 3001, last one is dropped.
        var mel = new double[NMel, rawFrames];
        var frame = new double[NFft];
        for (int f = 0; f < rawFrames; f++)
        {
            int at = f * Hop;
            for (int n = 0; n < NFft; n++)
                frame[n] = padded[at + n] * Hann[n];

            for (int m = 0; m < NMel; m++)
            {
                double sum = 0;
                for (int k = 0; k < NBins; k++)
                {
                    double w = Mel[m, k];
                    if (w == 0) continue;
                    double re = 0, im = 0;
                    for (int n = 0; n < NFft; n++)
                    {
                        re += frame[n] * Cos[k, n];
                        im -= frame[n] * Sin[k, n];
                    }
                    sum += (re * re + im * im) * w;
                }
                mel[m, f] = Math.Max(sum, 1e-10);
            }
        }

        var result = new float[NMel, FramesPerWindow];
        double peak = double.NegativeInfinity;
        for (int m = 0; m < NMel; m++)
            for (int f = 0; f < FramesPerWindow; f++)
            {
                double v = Math.Log10(mel[m, f]);
                result[m, f] = (float)v;
                if (v > peak) peak = v;
            }
        for (int m = 0; m < NMel; m++)
            for (int f = 0; f < FramesPerWindow; f++)
                result[m, f] = (float)((Math.Max(result[m, f], peak - 8.0) + 4.0) / 4.0);
        return result;
    }

    private static double[] BuildHann()
    {
        // Periodic Hann: hanning(401) without the last point.
        var w = new double[NFft];
        for (int n = 0; n < NFft; n++)
            w[n] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / (NFft + 1 - 1));
        return w;
    }

    private static double HertzToMel(double freq)
    {
        double mels = 3.0 * freq / 200.0;
        if (freq >= 1000.0)
            mels = 15.0 + Math.Log(freq / 1000.0) * (27.0 / Math.Log(6.4));
        return mels;
    }

    private static double MelToHertz(double mels)
    {
        double freq = 200.0 * mels / 3.0;
        if (mels >= 15.0)
            freq = 1000.0 * Math.Exp((Math.Log(6.4) / 27.0) * (mels - 15.0));
        return freq;
    }

    private static double[,] BuildMel()
    {
        double melMin = HertzToMel(0.0);
        double melMax = HertzToMel(8000.0);
        var filterFreqs = new double[NMel + 2];
        for (int i = 0; i < filterFreqs.Length; i++)
            filterFreqs[i] = MelToHertz(melMin + (melMax - melMin) * i / (NMel + 1));

        var fftFreqs = new double[NBins];
        for (int i = 0; i < NBins; i++)
            fftFreqs[i] = 8000.0 * i / (NBins - 1);

        var bank = new double[NMel, NBins];
        for (int m = 0; m < NMel; m++)
        {
            double f0 = filterFreqs[m], f1 = filterFreqs[m + 1], f2 = filterFreqs[m + 2];
            for (int k = 0; k < NBins; k++)
            {
                double f = fftFreqs[k];
                double rise = (f - f0) / (f1 - f0);
                double fall = (f2 - f) / (f2 - f1);
                bank[m, k] = Math.Max(0.0, Math.Min(rise, fall));
            }
        }

        // Slaney area norm: scale each filter by 2 / bandwidth.
        for (int m = 0; m < NMel; m++)
        {
            double enorm = 2.0 / (filterFreqs[m + 2] - filterFreqs[m]);
            for (int k = 0; k < NBins; k++)
                bank[m, k] *= enorm;
        }
        return bank;
    }
}
