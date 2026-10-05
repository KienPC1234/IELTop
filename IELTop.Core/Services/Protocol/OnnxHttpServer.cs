using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IELTop.Services.Ai;
using IELTop.Services.Audio;
using IELTop.Services.Diagnostics;

namespace IELTop.Services.Protocol;

/// <summary>
/// A small, documented HTTP surface over the offline ONNX models, so any other
/// program on the same machine can use them over <c>http://127.0.0.1:&lt;port&gt;</c>
/// instead of linking this assembly and the native runtime.
///
/// Design rules:
/// - Loopback only. The listener binds 127.0.0.1 and refuses any other host.
/// - One bearer token, printed only to the console on start, checked on every
///   request. It is a convenience gate for a local tool, not a security boundary
///   against a process that is already running as the same user.
/// - Stateless: each request carries the audio path, text, or payload it needs.
///   Audio is uploaded as base64 so the server never has to trust a path.
/// - The heavy services (Whisper, wav2vec2, T5) run on the request thread pool
///   and share the one OnnxService, so a model is loaded once.
///
/// Routes:
///   GET  /health                    status of every model slot and which are ready
///   POST /v1/stt                    {audioBase64} -> {text}
///   POST /v1/tts                    {text}        -> {wavBase64, voice}
///   POST /v1/pronunciation/check    {audioBase64, targetText} -> MddResult
///   POST /v1/grammar/check          {text}        -> GecResult
/// </summary>
public sealed class OnnxHttpServer : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IOnnxService _onnx;
    private readonly ISttService _stt;
    private readonly ITtsService _tts;
    private readonly IMddPhonemeService _mdd;
    private readonly IGecService _gec;
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public OnnxHttpServer(
        IOnnxService onnx,
        ISttService stt,
        ITtsService tts,
        IMddPhonemeService mdd,
        IGecService gec,
        int port = 8770,
        string? token = null)
    {
        _onnx = onnx;
        _stt = stt;
        _tts = tts;
        _mdd = mdd;
        _gec = gec;
        _baseUrl = $"http://127.0.0.1:{port}";
        _token = string.IsNullOrWhiteSpace(token)
            ? Guid.NewGuid().ToString("N")
            : token!;
        _listener.Prefixes.Add($"{_baseUrl}/");
    }

    public string BaseUrl => _baseUrl;

    /// <summary>The bearer token. Printed once on start; never logged again.</summary>
    public string Token => _token;

    /// <summary>True when the port is bound and the accept loop is running.</summary>
    public bool IsRunning { get; private set; }

    public string Start()
    {
        if (IsRunning) return _baseUrl;

        _listener.Start();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

        AppLog.Info("onnx-http", $"ONNX model server listening on {_baseUrl} (token in console and info file).");
        WriteInfoFile();
        Console.WriteLine("==================================================");
        Console.WriteLine("IELTop ONNX model server");
        Console.WriteLine($"  url   : {_baseUrl}");
        Console.WriteLine($"  token : {_token}");
        Console.WriteLine("  header: Authorization: Bearer <token>");
        Console.WriteLine("  routes: GET /health, POST /v1/stt, /v1/tts,");
        Console.WriteLine("          /v1/pronunciation/check, /v1/grammar/check");
        Console.WriteLine("==================================================");
        return _baseUrl;
    }

    /// <summary>
    /// The app is a windowed executable, so its console is usually not visible.
    /// The url and token are written to a local file for a client on the same
    /// machine to read, and removed on stop. The token is a local convenience
    /// gate, not a secret worth hiding from the same user.
    /// </summary>
    private void WriteInfoFile()
    {
        try
        {
            File.WriteAllText(InfoFilePath, JsonSerializer.Serialize(new
            {
                url = _baseUrl,
                token = _token,
                pid = Environment.ProcessId,
            }, Json));
        }
        catch (Exception ex)
        {
            AppLog.Warn("onnx-http", $"Could not write the info file: {ex.Message}");
        }
    }

    public static string InfoFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IELTop", "model-server.json");

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Warn("onnx-http", $"Accept failed: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleAsync(context, ct), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        try
        {
            // Loopback only: a public prefix would be a shared secret away from
            // letting any network host run the models.
            var remote = context.Request.RemoteEndPoint?.Address;
            if (remote is not null && !IPAddress.IsLoopback(remote))
            {
                await WriteAsync(context, 403, new { error = "This server accepts loopback requests only." }).ConfigureAwait(false);
                return;
            }

            if (!IsAuthorized(context.Request))
            {
                await WriteAsync(context, 401, new { error = "Missing or wrong bearer token." }).ConfigureAwait(false);
                return;
            }

            switch (path)
            {
                case "/health":
                    await WriteAsync(context, 200, Health()).ConfigureAwait(false);
                    return;

                case "/v1/stt":
                    await HandleSttAsync(context, ct).ConfigureAwait(false);
                    return;

                case "/v1/tts":
                    await HandleTtsAsync(context, ct).ConfigureAwait(false);
                    return;

                case "/v1/pronunciation/check":
                    await HandlePronunciationAsync(context, ct).ConfigureAwait(false);
                    return;

                case "/v1/grammar/check":
                    await HandleGrammarAsync(context, ct).ConfigureAwait(false);
                    return;

                default:
                    await WriteAsync(context, 404, new { error = $"Unknown route {path}." }).ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("onnx-http", $"{path} failed.", ex);
            try
            {
                await WriteAsync(context, 500, new { error = "The model could not complete the request." }).ConfigureAwait(false);
            }
            catch { /* the client is gone */ }
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(header)) return false;
        const string prefix = "Bearer ";
        var value = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : header.Trim();
        return string.Equals(value, _token, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ routes

    private object Health()
    {
        var slots = _onnx.Status()
            .Select(s => new
            {
                name = s.Name,
                installed = OnnxModelRegistry.IsComplete(s.Name),
                loaded = s.Loaded,
            })
            .ToList();

        return new
        {
            status = "ok",
            url = _baseUrl,
            provider = _onnx.ProviderName,
            stt = _stt.IsAvailable(),
            tts = _tts.IsAvailable,
            ttsVoice = _tts.VoiceName,
            pronunciation = _mdd.IsModelAvailable(),
            grammar = _gec.IsAvailable(),
            slots,
        };
    }

    private async Task HandleSttAsync(HttpListenerContext context, CancellationToken ct)
    {
        var body = await ReadJsonAsync(context, ct).ConfigureAwait(false);
        var wav = await DecodeAudioAsync(context, body, ct).ConfigureAwait(false);
        if (wav is null) return;

        try
        {
            var result = await _stt.TranscribeAsync(wav, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                await WriteAsync(context, 503, new { error = result.Error }).ConfigureAwait(false);
                return;
            }
            await WriteAsync(context, 200, new { text = result.Text }).ConfigureAwait(false);
        }
        finally
        {
            TempFiles.Delete(wav);
        }
    }

    private async Task HandleTtsAsync(HttpListenerContext context, CancellationToken ct)
    {
        var body = await ReadJsonAsync(context, ct).ConfigureAwait(false);
        var text = GetString(body, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            await WriteAsync(context, 400, new { error = "Field 'text' is required." }).ConfigureAwait(false);
            return;
        }

        var wav = await _tts.SpeakToFileAsync(text, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(wav) || !File.Exists(wav))
        {
            await WriteAsync(context, 503, new { error = "The voice model is not installed." }).ConfigureAwait(false);
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(wav, ct).ConfigureAwait(false);
            await WriteAsync(context, 200, new
            {
                voice = _tts.VoiceName,
                wavBase64 = Convert.ToBase64String(bytes),
            }).ConfigureAwait(false);
        }
        finally
        {
            TempFiles.Delete(wav);
        }
    }

    private async Task HandlePronunciationAsync(HttpListenerContext context, CancellationToken ct)
    {
        var body = await ReadJsonAsync(context, ct).ConfigureAwait(false);
        var target = GetString(body, "targetText");
        if (string.IsNullOrWhiteSpace(target))
        {
            await WriteAsync(context, 400, new { error = "Field 'targetText' is required." }).ConfigureAwait(false);
            return;
        }

        var wav = await DecodeAudioAsync(context, body, ct).ConfigureAwait(false);
        if (wav is null) return;

        try
        {
            var result = await _mdd.AssessAsync(wav, target, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                await WriteAsync(context, 503, new { error = result.Error }).ConfigureAwait(false);
                return;
            }
            await WriteAsync(context, 200, result).ConfigureAwait(false);
        }
        finally
        {
            TempFiles.Delete(wav);
        }
    }

    private async Task HandleGrammarAsync(HttpListenerContext context, CancellationToken ct)
    {
        var body = await ReadJsonAsync(context, ct).ConfigureAwait(false);
        var text = GetString(body, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            await WriteAsync(context, 400, new { error = "Field 'text' is required." }).ConfigureAwait(false);
            return;
        }

        var result = await _gec.CheckAsync(text, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            await WriteAsync(context, 503, new { error = result.Error }).ConfigureAwait(false);
            return;
        }
        await WriteAsync(context, 200, result).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ helpers

    private static async Task<JsonElement> ReadJsonAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (context.Request.InputStream is null) return default;
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return default;
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static string GetString(JsonElement body, string name)
        => body.ValueKind == JsonValueKind.Object
           && body.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Writes the uploaded audio to a temp wav for the model to read. The audio
    /// arrives as base64 so the caller cannot make the server read an arbitrary
    /// path. Returns null after writing an error response.
    /// </summary>
    private static async Task<string?> DecodeAudioAsync(
        HttpListenerContext context, JsonElement body, CancellationToken ct)
    {
        var base64 = GetString(body, "audioBase64");
        if (string.IsNullOrWhiteSpace(base64))
        {
            await WriteAsync(context, 400, new { error = "Field 'audioBase64' is required." }).ConfigureAwait(false);
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var path = TempFiles.NewWavPath();
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            return path;
        }
        catch (FormatException)
        {
            await WriteAsync(context, 400, new { error = "Field 'audioBase64' is not valid base64." }).ConfigureAwait(false);
            return null;
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { /* shutting down */ }
        try { _listener.Stop(); } catch { /* shutting down */ }
        try { _listener.Close(); } catch { /* shutting down */ }
        _cts?.Dispose();
        try { if (File.Exists(InfoFilePath)) File.Delete(InfoFilePath); }
        catch { /* best effort */ }
    }
}

/// <summary>Temp wav files for the model server, removed as soon as they are used.</summary>
internal static class TempFiles
{
    public static string NewWavPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "IELTop", "onxx-http");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"clip-{Guid.NewGuid():N}.wav");
    }

    public static void Delete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { /* the temp folder is cleaned by the OS */ }
    }
}
