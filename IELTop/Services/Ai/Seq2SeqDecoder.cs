using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IELTop.Services.Ai;

/// <summary>
/// Greedy autoregressive loop shared by whisper and T5 decoders.
/// Both optimum exports take encoder states plus the tokens so far and
/// return logits; only the input names differ, resolved by matching.
/// </summary>
public static class Seq2SeqDecoder
{
    /// <param name="maxNewTokens">Hard cap on generated tokens.</param>
    /// <param name="eosId">Generation stops after emitting this id.</param>
    /// <param name="blockFirstStep">Ids banned on the first step, like the
    /// reference decoder which never starts with end of text.</param>
    /// <param name="blockAlways">Ids banned on every step, for tokens that
    /// are never valid output.</param>
    /// <returns>Prompt plus generated ids, including the stop id.</returns>
    public static List<int> Greedy(
        InferenceSession decoder,
        float[,,] encoderHidden,
        int[] prompt,
        int eosId,
        int maxNewTokens,
        CancellationToken ct = default,
        int[]? blockFirstStep = null,
        int[]? blockAlways = null)
    {
        int encFrames = encoderHidden.GetLength(1);
        int encDim = encoderHidden.GetLength(2);

        string? hiddenName = null, idsName = null, maskName = null;
        foreach (var name in decoder.InputMetadata.Keys)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("encoder_hidden_states")) hiddenName = name;
            else if (lower.Contains("input_ids")) idsName = name;
            else if (lower.Contains("attention_mask")) maskName = name;
        }
        if (hiddenName is null || idsName is null)
            throw new InvalidOperationException("Decoder inputs were not recognized.");

        var hidden = new DenseTensor<float>(new[] { 1, encFrames, encDim });
        for (int t = 0; t < encFrames; t++)
            for (int d = 0; d < encDim; d++)
                hidden[0, t, d] = encoderHidden[0, t, d];

        long[]? mask = maskName is null ? null : Enumerable.Repeat(1L, encFrames).ToArray();

        var ids = new List<int>(prompt);
        string? logitsName = decoder.OutputMetadata.Keys.FirstOrDefault(k => k.Contains("logits"))
            ?? decoder.OutputMetadata.Keys.First();

        for (int step = 0; step < maxNewTokens; step++)
        {
            ct.ThrowIfCancellationRequested();

            var idTensor = new DenseTensor<long>(new[] { 1, ids.Count });
            for (int i = 0; i < ids.Count; i++)
                idTensor[0, i] = ids[i];

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(hiddenName, hidden),
                NamedOnnxValue.CreateFromTensor(idsName, idTensor),
            };
            if (maskName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    maskName, new DenseTensor<long>(mask!, new[] { 1, encFrames })));

            using var results = decoder.Run(inputs);
            var logits = results.First(r => r.Name == logitsName).AsTensor<float>();
            int classes = logits.Dimensions[2];
            int last = ids.Count - 1;

            int best = 0;
            float bestVal = float.NegativeInfinity;
            for (int c = 0; c < classes; c++)
            {
                if (blockAlways is not null && Array.IndexOf(blockAlways, c) >= 0)
                    continue;
                if (step == 0 && blockFirstStep is not null && Array.IndexOf(blockFirstStep, c) >= 0)
                    continue;
                float v = logits[0, last, c];
                if (v > bestVal) { bestVal = v; best = c; }
            }

            ids.Add(best);
            if (best == eosId) break;
        }
        return ids;
    }
}
