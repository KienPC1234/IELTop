using System.IO;

namespace IELTop.Services.Audio;

/// <summary>
/// Đọc WAV thành mảng float mono 16kHz cho model âm vị.
/// Tự trộn kênh và hạ/tăng mẫu bằng nội suy tuyến tính, không cần lib ngoài.
/// </summary>
public static class WavLoader
{
    public const int TargetSampleRate = 16000;

    public static float[] LoadMono16k(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Not a WAV file.");

        reader.ReadInt32(); // file size, not needed
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Not a WAV file.");

        short channels = 1;
        int sampleRate = TargetSampleRate;
        short bits = 16;
        short format = 1;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length) break;

            if (chunkId == "fmt ")
            {
                format = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadInt16(); // block align
                bits = reader.ReadInt16();
                if (chunkSize > 16) reader.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }
            else
            {
                reader.ReadBytes(chunkSize);
            }

            if (chunkSize % 2 != 0) stream.Position++; // odd chunks are padded by one byte
        }

        if (data is null || data.Length == 0)
            throw new InvalidDataException("The WAV file has no audio data.");
        if (format != 1 || bits != 16)
            throw new InvalidDataException("Only 16-bit PCM WAV files are supported.");

        int frames = data.Length / 2 / channels;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            int sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int idx = (i * channels + c) * 2;
                sum += (short)(data[idx] | (data[idx + 1] << 8));
            }
            mono[i] = sum / (float)channels / 32768f;
        }

        return sampleRate == TargetSampleRate
            ? mono
            : Resample(mono, sampleRate, TargetSampleRate);
    }

    private static float[] Resample(float[] input, int from, int to)
    {
        if (input.Length == 0) return input;
        int outLen = (int)Math.Round(input.Length * (double)to / from);
        var output = new float[outLen];
        double step = (double)from / to;
        for (int i = 0; i < outLen; i++)
        {
            double pos = i * step;
            int i0 = (int)pos;
            int i1 = Math.Min(i0 + 1, input.Length - 1);
            double frac = pos - i0;
            output[i] = (float)(input[i0] * (1 - frac) + input[i1] * frac);
        }
        return output;
    }
}
