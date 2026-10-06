using System;
using System.Collections.Generic;
using System.Management;
using IELTop.Services.Ai;
using IELTop.Services.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace IELTop.Desktop.Diagnostics;

/// <summary>
/// Picks a GPU execution provider for the ONNX models when the machine has a
/// usable GPU, and falls back to CPU when it does not. DirectML is used because
/// it works with any DirectX 12 GPU (NVIDIA, AMD, Intel) through one build, so
/// there is no per-vendor package.
///
/// The provider is chosen once at startup. A model still runs even if the GPU is
/// too old or the driver is missing, because the CPU provider is always added as
/// the fallback.
/// </summary>
public sealed class DirectMlExecutionProvider : IOnnxExecutionProvider
{
    private readonly IReadOnlyList<string> _gpuNames;

    public DirectMlExecutionProvider()
    {
        try
        {
            _gpuNames = DetectGpuNames();
        }
        catch (Exception ex)
        {
            AppLog.Warn("onnx", $"GPU detection failed: {ex.Message}");
            _gpuNames = Array.Empty<string>();
        }
    }

    public string Name => _gpuNames.Count == 0
        ? "CPU (no DirectX 12 GPU found)"
        : $"DirectML GPU ({string.Join(", ", _gpuNames)})";

    public bool TryAppend(SessionOptions options)
    {
        if (_gpuNames.Count == 0) return false;
        try
        {
            // Device id 0 is the default adapter DirectML picks for the process.
            // Any DirectX 12 GPU works (NVIDIA, AMD, Intel) because DirectML
            // speaks DX12, so no vendor package is needed.
            options.AppendExecutionProvider_DML(0);
            AppLog.Info("onnx", $"Using the GPU for ONNX (default adapter). Detected: {string.Join(", ", _gpuNames)}.");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("onnx", $"DirectML could not start, falling back to CPU: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Every usable GPU name, so the log and the health output are honest about
    /// what is present. Skipped: virtual display adapters and the Microsoft Basic
    /// Render Driver (not real GPUs), adapters Windows reports as broken or
    /// disabled (bad Status or non-zero ConfigManagerErrorCode, e.g. a dead card
    /// the driver already flagged), and names in IELTOP_GPU_BLOCKLIST (a ";"
    /// separated list of extra name fragments to skip without a code change).
    /// </summary>
    private static IReadOnlyList<string> DetectGpuNames()
    {
        var blocked = new List<string>();
        var extra = Environment.GetEnvironmentVariable("IELTOP_GPU_BLOCKLIST");
        if (!string.IsNullOrWhiteSpace(extra))
            blocked.AddRange(extra.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var names = new List<string>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Status, ConfigManagerErrorCode FROM Win32_VideoController");
        foreach (ManagementObject adapter in searcher.Get())
        {
            var name = adapter["Name"] as string ?? string.Empty;
            if (name.Length == 0) continue;
            if (name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;

            // A card Windows flagged as broken or disabled must never be picked:
            // DirectML device 0 could resolve to it and every inference fails.
            var status = adapter["Status"] as string ?? string.Empty;
            uint code = 0;
            try { code = Convert.ToUInt32(adapter["ConfigManagerErrorCode"]); } catch { /* unknown, treat as usable */ }
            if (code != 0 || (!status.Equals("OK", StringComparison.OrdinalIgnoreCase) && status.Length > 0))
            {
                AppLog.Warn("onnx", $"Skipping unusable GPU '{name}' (status '{status}', code {code}).");
                continue;
            }

            string? hit = blocked.Find(b => b.Length > 0 && name.Contains(b, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                AppLog.Warn("onnx", $"Skipping blocked GPU '{name}' (matched '{hit}').");
                continue;
            }

            names.Add(name);
        }
        return names;
    }
}
