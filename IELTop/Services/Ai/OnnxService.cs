using System.IO;
using Microsoft.ML.OnnxRuntime;

namespace IELTop.Services.Ai;

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
    void Unload(string name);
}

public sealed class OnnxService : IOnnxService
{
    private readonly Dictionary<string, InferenceSession> _sessions = new();
    private readonly SessionOptions _baseOptions = new();
    private bool _disposed;

    public OnnxService()
    {
        // Dùng CPU, arena mở để chạy mượt trên máy yếu.
        // Sau này bật DirectML/CUDA ở đây nếu có GPU.
        _baseOptions.EnableMemoryPattern = true;
        _baseOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        try { _baseOptions.AppendExecutionProvider_CPU(0); } catch { /* bỏ qua */ }

        Directory.CreateDirectory(OnnxModelRegistry.ModelsDir);
    }

    public IReadOnlyList<(string Name, bool Loaded, string Path)> Status()
        => OnnxModelRegistry.Slots
            .Select(s =>
            {
                var path = Path.Combine(OnnxModelRegistry.ModelsDir, s.FileName);
                return (s.Name, _sessions.ContainsKey(s.Name) && File.Exists(path), path);
            })
            .ToList();

    public bool IsLoaded(string name) => _sessions.ContainsKey(name);

    public InferenceSession? Get(string name)
        => _sessions.TryGetValue(name, out var s) ? s : null;

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

        if (_sessions.ContainsKey(name))
            return slot.ExtraFile is { } extra && extra.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                ? _sessions.ContainsKey(name + "#extra")
                : true;

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
            Unload(name);
            error = $"Could not open {slot.FileName}. The file may be corrupt or missing its companion file.";
            return false;
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
        => _sessions.TryGetValue(name + "#extra", out var s) ? s : null;

    public void Unload(string name)
    {
        if (_sessions.Remove(name, out var s)) s.Dispose();
        if (_sessions.Remove(name + "#extra", out var extra)) extra.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var s in _sessions.Values) s.Dispose();
        _sessions.Clear();
        _baseOptions.Dispose();
    }
}
