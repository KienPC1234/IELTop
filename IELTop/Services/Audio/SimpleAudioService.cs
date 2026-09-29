using System.IO;
using NAudio.Wave;

namespace IELTop.Services.Audio;

/// <summary>
/// Plays and records audio for Listening and Speaking.
/// Recordings are WAV 16 kHz mono, the input format the models expect.
/// </summary>
public interface IAudioService : IDisposable
{
    bool IsRecording { get; }
    double Volume { get; set; }
    Task PlayAsync(string filePath, CancellationToken ct = default);
    Task<string> RecordAsync(int seconds = 5, CancellationToken ct = default);
    void StopRecording();
    void StopPlayback();
}

public sealed class SimpleAudioService : IAudioService
{
    private WaveOutEvent? _player;
    private WaveInEvent? _capture;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<string>? _recordCompletion;

    public bool IsRecording => _capture is not null;

    public double Volume { get; set; } = 0.8;

    public async Task PlayAsync(string filePath, CancellationToken ct = default)
    {
        StopPlayback();
        using WaveStream reader = OpenReader(filePath);
        var done = new TaskCompletionSource();
        using var registration = ct.Register(() => done.TrySetCanceled());

        _player = new WaveOutEvent { Volume = (float)Math.Clamp(Volume, 0, 1) };
        _player.Init(reader);
        _player.PlaybackStopped += (_, _) => done.TrySetResult();
        _player.Play();
        await done.Task;
    }

    /// <summary>
    /// Opens a real exam clip. WAV, MP3, and AIFF are read directly.
    /// M4A, Opus, and other containers go through Media Foundation, which
    /// decodes them on Windows. A synthetic voice is never used for Listening.
    /// </summary>
    private static WaveStream OpenReader(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".wav" => new WaveFileReader(filePath),
            ".mp3" => new Mp3FileReader(filePath),
            ".aiff" or ".aif" => new AiffFileReader(filePath),
            _ => new MediaFoundationReader(filePath)
        };
    }

    public Task<string> RecordAsync(int seconds = 5, CancellationToken ct = default)
    {
        if (IsRecording)
            return Task.FromResult(string.Empty);

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IELTop", "recordings");
        Directory.CreateDirectory(dir);
        var outputPath = Path.Combine(dir, $"speaking_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

        _recordCompletion = new TaskCompletionSource<string>();
        _writer = new WaveFileWriter(outputPath, new WaveFormat(16000, 16, 1));
        _capture = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100
        };
        _capture.DataAvailable += OnDataAvailable;

        try
        {
            _capture.StartRecording();
        }
        catch (Exception)
        {
            // No microphone or the device is busy. Clean up so the next try is clean.
            DisposeRecording();
            throw;
        }

        return WaitForRecordingAsync(seconds, ct);
    }

    private async Task<string> WaitForRecordingAsync(int seconds, CancellationToken ct)
    {
        try
        {
            // Stop after the limit, or sooner if the caller cancels.
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        }
        catch (OperationCanceledException)
        {
            // Cancellation just ends recording early.
        }
        finally
        {
            StopRecording();
        }

        return await _recordCompletion!.Task;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _writer?.Write(e.Buffer, 0, e.BytesRecorded);

    public void StopRecording()
    {
        if (!IsRecording) return;

        var completion = _recordCompletion;
        var path = _writer?.Filename ?? string.Empty;

        DisposeRecording();
        completion?.TrySetResult(path);
    }

    private void DisposeRecording()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch (Exception) { /* device already gone */ }
            _capture.Dispose();
            _capture = null;
        }

        _writer?.Dispose();
        _writer = null;
    }

    public void StopPlayback()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
    }

    public void Dispose()
    {
        StopRecording();
        StopPlayback();
        _recordCompletion?.TrySetResult(string.Empty);
    }
}
