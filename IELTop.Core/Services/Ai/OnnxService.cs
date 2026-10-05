using System.IO;
using Microsoft.ML.OnnxRuntime;

namespace IELTop.Services.Ai;

/// <summary>
/// Appends an execution provider to a session. The host implements this so it can
/// use a GPU (DirectML) without Core taking a Windows-only dependency. Core keeps
/// a CPU provider so the app always runs without a GPU.
/// </summary>
public interface IOnnxExecutionProvider
{
    /// <summary>A short name for the UI, for example "CPU" or "DirectML GPU".</summary>
    string Name { get; }

    /// <summary>
    /// Tries to add the provider to the options. Returns false when the hardware
    /// or runtime is not there, and the service falls back to CPU.
    /// </summary>
    bool TryAppend(SessionOptions options);
}

/// <summary>The default: CPU only, no GPU dependency.</summary>
public sealed class CpuOnnxExecutionProvider : IOnnxExecutionProvider
{
    public string Name => "CPU";
    public bool TryAppend(SessionOptions options)
    {
        try { options.AppendExecutionProvider_CPU(0); } catch { /* CPU is always there */ }
        return true;
    }
}

/// <summary>
/// Service nền ONNX: quản lý SessionOptions, load lazy, dispose đúng cách.
/// UI/ViewModel chỉ gọi qua đây, không new InferenceSession lung tung.
/// </summary>
public interface IOnnxService : IDisposable
{
    IReadOnlyList<(string Name, bool Loaded, string Path)> Status();
    bool IsLoaded(string name);
    InferenceSession? Get(string name);
    /// <summary>
    /// Second session for encoder-decoder models whose companion file is
    /// another .onnx file (whisper, T5). Null for single file slots.
    /// </summary>
    InferenceSession? GetExtra(string name);
    bool TryLoad(string name, out string error);
    /// <summary>Loads only when the files are complete, for the auto load path.</summary>
    bool EnsureLoaded(string name, out string error);
    void Unload(string name);
    void UnloadAll();
    /// <summary>Bytes on disk for models currently in memory.</summary>
    long LoadedBytes();
    /// <summary>Which execution provider is in use: CPU or a GPU.</summary>
    string ProviderName { get; }
}

public sealed class OnnxService : IOnnxService
{
    private readonly Dictionary<string, InferenceSession> _sessions = new();
    private readonly object _gate = new();
    private readonly SessionOptions _baseOptions = new();
    private readonly IOnnxExecutionProvider _provider;
    private volatile bool _disposed;

    public OnnxService() : this(new CpuOnnxExecutionProvider())
    {
    }

    public OnnxService(IOnnxExecutionProvider provider)
    {
        _provider = provider;
        ProviderName = provider.Name;

        // The graph runs on the provider the host picked (a GPU when present, CPU
        // otherwise). Memory pattern and full graph optimization stay on.
        _baseOptions.EnableMemoryPattern = true;
        _baseOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        try
        {
            if (!_provider.TryAppend(_baseOptions))
                new CpuOnnxExecutionProvider().TryAppend(_baseOptions);
        }
        catch
        {
            new CpuOnnxExecutionProvider().TryAppend(_baseOptions);
        }

        Directory.CreateDirectory(OnnxModelRegistry.ModelsDir);
    }

    public string ProviderName { get; }

    public IReadOnlyList<(string Name, bool Loaded, string Path)> Status()
        => OnnxModelRegistry.Slots
            .Select(s =>
            {
                var path = Path.Combine(OnnxModelRegistry.ModelsDir, s.FileName);
                return (s.Name, IsLoaded(s.Name) && File.Exists(path), path);
            })
            .ToList();

    public bool IsLoaded(string name)
    {
        lock (_gate)
            return _sessions.ContainsKey(name)
                   || _sessions.ContainsKey(name + "#extra");
    }

    public InferenceSession? Get(string name)
    {
        lock (_gate)
            return _sessions.TryGetValue(name, out var s) ? s : null;
    }

    /// <summary>Loads only when every file for the slot is present.</summary>
    public bool EnsureLoaded(string name, out string error)
    {
        if (!OnnxModelRegistry.IsComplete(name))
        {
            error = string.Empty;
            return false;
        }
        return TryLoad(name, out error);
    }

    public bool TryLoad(string name, out string error)
    {
        error = string.Empty;
        var slot = OnnxModelRegistry.Slots.FirstOrDefault(s => s.Name == name);
        if (slot is null) { error = $"Unknown model slot '{name}'."; return false; }

        var path = Path.Combine(OnnxModelRegistry.ModelsDir, slot.FileName);
        if (!File.Exists(path))
        {
            error = $"Slot {slot.Name} needs {slot.FileName} at {path}. Copy the file there, then press Load.";
            return false;
        }

        if (!File.Exists(OnnxModelRegistry.PathOfExtra(name)) && slot.ExtraFile is not null)
        {
            error = $"Slot {slot.Name} also needs {slot.ExtraFile} at {OnnxModelRegistry.PathOfExtra(name)}.";
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                error = "The model service is shutting down.";
                return false;
            }
            if (_sessions.ContainsKey(name))
                return slot.ExtraFile is { } extra && extra.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                    ? _sessions.ContainsKey(name + "#extra")
                    : true;

            if (!MemoryCheck(slot, out error))
                return false;

            try
            {
                // Clone options per session so loads stay thread safe.
                _sessions[name] = OpenSession(path);
                if (slot.ExtraFile is { } extraFile
                    && extraFile.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                {
                    _sessions[name + "#extra"] = OpenSession(OnnxModelRegistry.PathOfExtra(name));
                }
                return true;
            }
            catch (Exception)
            {
                // Raw runtime errors can leak internal detail, so UI gets a short message.
                UnloadLocked(name);
                error = $"Could not open {slot.FileName}. The file may be corrupt or missing its companion file.";
                return false;
            }
        }
    }

    /// <summary>
    /// Refuses to load when free memory cannot cover the files plus
    /// session overhead, so a weak machine gets a clear message instead
    /// of an out of memory crash. Best effort, inference can still spike.
    /// </summary>
    private static bool MemoryCheck(OnnxModelSlot slot, out string error)
    {
        error = string.Empty;
        long files;
        try
        {
            files = new FileInfo(Path.Combine(OnnxModelRegistry.ModelsDir, slot.FileName)).Length;
            if (slot.ExtraFile is { } extra
                && extra.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                files += new FileInfo(OnnxModelRegistry.PathOfExtra(slot.Name)).Length;
        }
        catch
        {
            return true;
        }

        long need = files * 2;
        long free;
        try
        {
            free = (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch
        {
            return true;
        }
        if (free < need)
        {
            error = $"Not enough free memory to load {slot.FileName} " +
                $"(needs about {need / 1048576} MB). Unload another model or close other apps.";
            return false;
        }
        return true;
    }

    public long LoadedBytes()
    {
        long total = 0;
        foreach (var slot in OnnxModelRegistry.Slots)
        {
            if (IsLoaded(slot.Name))
                total += FileSize(OnnxModelRegistry.PathOf(slot.Name));
            if (GetExtra(slot.Name) is not null)
                total += FileSize(OnnxModelRegistry.PathOfExtra(slot.Name));
        }
        return total;
    }

    private static long FileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }
    private static InferenceSession OpenSession(string path)
    {
        var opts = new SessionOptions();
        opts.EnableMemoryPattern = true;
        opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        return new InferenceSession(modelPath: path, options: opts);
    }

    public InferenceSession? GetExtra(string name)
    {
        lock (_gate)
            return _sessions.TryGetValue(name + "#extra", out var s) ? s : null;
    }

    public void Unload(string name)
    {
        lock (_gate)
            UnloadLocked(name);
    }

    private void UnloadLocked(string name)
    {
        if (_sessions.Remove(name, out var s)) s.Dispose();
        if (_sessions.Remove(name + "#extra", out var extra)) extra.Dispose();
    }

    public void UnloadAll()
    {
        lock (_gate)
        {
            foreach (var s in _sessions.Values) s.Dispose();
            _sessions.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadAll();
        _baseOptions.Dispose();
    }
}
