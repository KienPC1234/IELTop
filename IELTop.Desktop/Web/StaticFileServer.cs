using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IELTop.Desktop.Web;

/// <summary>
/// Serves the built web UI from a local port. The WebView refuses ES modules
/// over file://, so the UI is served over HTTP on the loopback address only.
/// No ASP.NET, no external process, and nothing leaves the machine.
/// </summary>
public sealed class StaticFileServer : IDisposable
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".wasm"] = "application/wasm",
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
        [".txt"] = "text/plain; charset=utf-8",
    };

    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>
    /// Extra read only folders served under /media/. The WebView refuses to
    /// load local files from an http page, so exam audio and Writing charts
    /// are copied through here instead of a file:// path.
    /// </summary>
    private readonly List<(string Prefix, string Dir)> _media = new();

    public string BaseUrl { get; }

    public StaticFileServer(string root)
    {
        _root = Path.GetFullPath(root);
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
    }

    /// <summary>Adds a folder served at /media/{prefix}/. Order matters: first match wins.</summary>
    public void AddMediaFolder(string prefix, string directory)
        => _media.Add((prefix.Trim('/'), Path.GetFullPath(directory)));

    /// <summary>Maps an absolute file path to a served URL, or null if outside every root.</summary>
    public string? UrlForFile(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return null;
        var full = Path.GetFullPath(absolutePath);
        foreach (var (prefix, dir) in _media)
        {
            var root = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var name = full[root.Length..].Replace('\\', '/');
                return $"{BaseUrl}/media/{prefix}/{name}";
            }
        }
        return null;
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                // The listener was stopped; end the loop.
                break;
            }

            _ = Task.Run(() => Respond(context));
        }
    }

    private void Respond(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var relative = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/")
                .TrimStart('/');

            // Exam media is served from the read only source folders.
            if (relative.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = relative["media/".Length..];
                int slash = rest.IndexOf('/');
                if (slash <= 0) { response.StatusCode = (int)HttpStatusCode.NotFound; response.Close(); return; }
                var prefix = rest[..slash];
                var name = rest[(slash + 1)..];
                var dirs = _media.Where(m => m.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Dir).ToList();
                string? mediaPath = null;
                foreach (var dir in dirs)
                {
                    var candidatePath = Path.GetFullPath(Path.Combine(dir, name));
                    if (candidatePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && File.Exists(candidatePath))
                    {
                        mediaPath = candidatePath;
                        break;
                    }
                }
                if (mediaPath is null)
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                    return;
                }
                WriteFile(response, mediaPath);
                return;
            }

            if (string.IsNullOrEmpty(relative)) relative = "index.html";

            var candidate = Path.GetFullPath(Path.Combine(_root, relative));

            // A single page app routes on the client, so an unknown path with
            // no file extension falls back to index.html rather than a 404.
            if (!candidate.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            {
                if (Path.HasExtension(relative))
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                    return;
                }
                candidate = Path.Combine(_root, "index.html");
            }

            if (!File.Exists(candidate))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
                return;
            }

            WriteFile(response, candidate);
        }
        catch (Exception)
        {
            try { response.StatusCode = (int)HttpStatusCode.InternalServerError; } catch { /* client gone */ }
        }
        finally
        {
            try { response.Close(); } catch { /* client gone */ }
        }
    }

    private static void WriteFile(HttpListenerResponse response, string path)
    {
        var extension = Path.GetExtension(path);
        response.ContentType = MimeTypes.TryGetValue(extension, out var mime)
            ? mime
            : "application/octet-stream";
        response.Headers["Cache-Control"] = "no-store";

        var bytes = File.ReadAllBytes(path);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        _listener.Close();
        _cts.Dispose();
    }
}
