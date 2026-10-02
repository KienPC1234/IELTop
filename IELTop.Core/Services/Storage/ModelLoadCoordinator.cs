using IELTop.Services.Ai;
using IELTop.Services.Storage;

namespace IELTop.Services.Storage;

/// <summary>
/// Coordinates offline ONNX model loading for the load mode switch.
/// Two modes: keep ready preloads a feature's models up front so marking
/// never stalls, on demand lets each service load at the moment of need.
/// Either way the models a single run pulled in are freed when that run
/// ends, so RAM is not held while the student reads results.
/// </summary>
public interface IModelLoadCoordinator
{
    /// <summary>Model slots needed for Writing marking: grammar.</summary>
    IReadOnlyList<string> WritingSlots { get; }
    /// <summary>Model slots needed for Speaking: transcription and pronunciation.</summary>
    IReadOnlyList<string> SpeakingSlots { get; }

    /// <summary>True while the keep ready switch is on.</summary>
    bool KeepReady { get; }

    /// <summary>
    /// Prepares the models one run needs. In keep ready mode it loads them
    /// up front, which is when the loading screen shows. In on demand mode
    /// it only records the current state and lets services load lazily.
    /// </summary>
    Task<ModelPrepResult> PrepareAsync(IEnumerable<string> slots, CancellationToken ct = default);

    /// <summary>
    /// Frees the models this run pulled in, but keeps any that were already
    /// in memory before the run started, for example manual loads.
    /// </summary>
    void ReleaseAfterUse(IEnumerable<string> slots);

    /// <summary>One short line for the UI about the load mode.</summary>
    string DescribeMode();
}

public sealed record ModelPrepResult(bool Ready, bool Loaded, string Message)
{
    public static ModelPrepResult Ok(bool loaded, string message) => new(true, loaded, message);
    public static ModelPrepResult Skip(string message) => new(false, false, message);
}

public sealed class ModelLoadCoordinator : IModelLoadCoordinator
{
    private readonly IOnnxService _onnx;
    private readonly ISettingsStore _settings;
    private readonly HashSet<string> _preRunLoaded = new(StringComparer.Ordinal);

    public ModelLoadCoordinator(IOnnxService onnx, ISettingsStore settings)
    {
        _onnx = onnx;
        _settings = settings;
    }

    public IReadOnlyList<string> WritingSlots { get; } = Array.Empty<string>();

    public IReadOnlyList<string> SpeakingSlots { get; } = new[]
        { "stt-whisper-tiny-en", "mdd-wav2vec2-base" };

    public bool KeepReady => _settings.Current.ModelAutoLoad;

    public async Task<ModelPrepResult> PrepareAsync(
        IEnumerable<string> slots, CancellationToken ct = default)
    {
        var wanted = slots
            .Where(name => OnnxModelRegistry.IsComplete(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Remember what was already in memory, so manual loads survive.
        _preRunLoaded.Clear();
        foreach (var name in wanted)
            if (_onnx.IsLoaded(name)) _preRunLoaded.Add(name);

        if (wanted.Count == 0)
            return ModelPrepResult.Skip("No model files for this feature. It will run offline.");
        if (!KeepReady)
            return ModelPrepResult.Skip("Models load only when a step needs them.");

        try
        {
            var loaded = await Task.Run(() =>
            {
                int ok = 0;
                foreach (var name in wanted)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_onnx.IsLoaded(name) || _onnx.TryLoad(name, out _)) ok++;
                }
                return ok;
            }, ct);
            return ModelPrepResult.Ok(true,
                loaded == wanted.Count
                    ? $"Models ready: {loaded}."
                    : $"Only {loaded} of {wanted.Count} models could be loaded.");
        }
        catch (OperationCanceledException)
        {
            return ModelPrepResult.Skip("Model loading was stopped.");
        }
        catch (Exception)
        {
            return ModelPrepResult.Skip("Models could not be loaded. Each step will try again when used.");
        }
    }

    public void ReleaseAfterUse(IEnumerable<string> slots)
    {
        foreach (var name in slots.Distinct(StringComparer.Ordinal))
        {
            // Keep anything that was already loaded before this run.
            if (_preRunLoaded.Contains(name)) continue;
            _onnx.Unload(name);
        }
        _preRunLoaded.Clear();
    }

    public string DescribeMode()
    {
        if (!KeepReady)
            return "On demand. Models load when a step needs them, then unload when the run ends.";
        int ready = OnnxModelRegistry.Slots.Count(s => OnnxModelRegistry.IsComplete(s.Name));
        return ready == 0
            ? "Keep ready is on, but no model files are installed yet."
            : $"Keep ready is on. {ready} model(s) load when Grade with AI starts, then unload after.";
    }
}
