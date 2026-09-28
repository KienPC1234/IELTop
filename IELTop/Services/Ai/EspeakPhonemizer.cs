using System.Diagnostics;
using System.IO;

namespace IELTop.Services.Ai;

/// <summary>
/// Turns English text into Piper phoneme id lists using the espeak-ng
/// command line tool, following the reference standalone ONNX recipe:
/// espeak-ng -v en-us -q --ipa=2 -x, one output line per clause, phonemes
/// mapped to ids through the voice config, wrapped with ^ _ $.
/// </summary>
public static class EspeakPhonemizer
{
    public static string ExePath => Path.Combine(
        OnnxModelRegistry.ModelsDir, "espeak-ng", "espeak-ng.exe");

    public static bool IsAvailable => File.Exists(ExePath);

    private static string DataPath => Path.Combine(
        OnnxModelRegistry.ModelsDir, "espeak-ng", "espeak-ng-data");

    /// <returns>One id list per clause. Empty when espeak fails.</returns>
    public static List<int[]> Phonemize(
        string text, string voice, IReadOnlyDictionary<string, int[]> map)
    {
        var result = new List<int[]>();
        string output;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = $"-v {voice} -q --ipa=2 -x \"{text.Replace("\"", "")}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            // An extracted copy finds its voice data through this variable.
            process.StartInfo.Environment["ESPEAK_DATA_PATH"] = DataPath;
            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            if (process.ExitCode != 0) return result;
        }
        catch
        {
            return result;
        }

        if (!map.TryGetValue("^", out var bos)
            || !map.TryGetValue("_", out var pad)
            || !map.TryGetValue("$", out var eos))
            return result;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim().Replace("_", " ");
            if (line.Length == 0) continue;
            var ids = new List<int> { bos[0], pad[0] };
            foreach (var ch in line)
            {
                var key = ch.ToString();
                if (map.TryGetValue(key, out var mapped))
                    ids.AddRange(mapped);
                ids.Add(pad[0]);
            }
            ids.Add(eos[0]);
            if (ids.Count > 4)
                result.Add(ids.ToArray());
        }
        return result;
    }
}
