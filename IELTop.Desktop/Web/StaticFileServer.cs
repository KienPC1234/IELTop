using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using IELTop.Desktop.Bridge;

namespace IELTop.Desktop.Web;

/// <summary>
/// High-performance, secure local HTTP server that serves the UI and handles Bridge RPC.
/// Features:
/// - Collision-resistant local port binding with automatic retry.
/// - An access key that every page and every call must carry, so a page opened
///   elsewhere on the machine cannot drive the bridge.
/// - Strict loopback origin validation and defense-in-depth security headers (anti-XSS, anti-clickjacking).
/// - Path traversal mitigation and hidden file protection.
/// - Streaming FileStream transfers with HTTP 206 Partial Content (Range requests) for lag-free audio seeking.
/// - Asynchronous request pipeline with SSE heartbeat resilience.
/// </summary>
public sealed class StaticFileServer : IDisposable
{
    private const int MaxRequestBodyLength = 50 * 1024 * 1024; // 50 MB max payload for audio uploads
    private const int StreamBufferSize = 64 * 1024; // 64 KB streaming buffer
    private const string AccessCookieName = "ieltop-access";

    // A loopback server, so the ceiling is high enough that a normal page never
    // feels it and low enough that one stuck client cannot pile up work.
    private const int MaxConcurrentRequests = 32;
    private static readonly TimeSpan SlotWaitTimeout = TimeSpan.FromSeconds(10);

    // Event streams stay open for the whole test, so they are counted apart
    // from the request slots: they would sit on a slot for minutes otherwise.
    private const int MaxEventStreams = 4;

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

    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _accessKey;
    private readonly object _mediaGate = new();
    private readonly SemaphoreSlim _requestSlots = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private (string Prefix, string Dir)[] _media = Array.Empty<(string, string)>();
    private int _eventStreams;
    private readonly ConcurrentDictionary<Guid, Func<string, Task>> _sseClients = new();
    private bool _disposed;

    public string BaseUrl { get; }

    /// <summary>
    /// Random key every page and every bridge call must carry. The window gets
    /// it in the URL it opens, then the browser keeps it in a cookie.
    /// </summary>
    public string AccessKey { get; }

    public BridgeRouter? Router { get; set; }

    public StaticFileServer(string root)
    {
        _root = Path.GetFullPath(root);
        _accessKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        (_listener, _port) = BindListenerWithRetry(maxAttempts: 20);
        BaseUrl = $"http://127.0.0.1:{_port}";
        AccessKey = _accessKey;
    }

    /// <summary>Adds a folder served at /media/{prefix}/. Order matters: first match wins.</summary>
    public void AddMediaFolder(string prefix, string directory)
    {
        var cleanPrefix = prefix.Trim('/');
        var fullDir = Path.GetFullPath(directory);
        lock (_mediaGate)
        {
            _media = [.. _media, (cleanPrefix, fullDir)];
        }
    }

    /// <summary>Maps an absolute file path to a served URL, or null if outside every root.</summary>
    public string? UrlForFile(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return null;
        var full = Path.GetFullPath(absolutePath);
        foreach (var (prefix, dir) in MediaFolders)
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

    private (string Prefix, string Dir)[] MediaFolders
    {
        get { lock (_mediaGate) { return _media; } }
    }

    public void Start()
    {
        _ = Task.Run(LoopAsync);
        _ = Task.Run(SseHeartbeatLoopAsync);
    }

    private static (HttpListener Listener, int Port) BindListenerWithRetry(int maxAttempts)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int port = PickFreePort();
            var listener = new HttpListener();
            try
            {
                listener.IgnoreWriteExceptions = true;
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                return (listener, port);
            }
            catch (Exception)
            {
                try { listener.Close(); } catch { }
            }
        }
        throw new InvalidOperationException($"Could not bind StaticFileServer to any loopback port after {maxAttempts} attempts.");
    }

    private static int PickFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => ProcessRequestAsync(context));
            }
            catch (Exception) when (_cts.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[StaticFileServer] Accept error: {ex.Message}");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        if (IsEventStreamRequest(context.Request))
        {
            if (Interlocked.Increment(ref _eventStreams) > MaxEventStreams)
            {
                Interlocked.Decrement(ref _eventStreams);
                ApplySecurityHeaders(context.Response);
                WritePlainText(context, HttpStatusCode.ServiceUnavailable, "Too many test windows are already open.");
                return;
            }

            try
            {
                await ProcessCoreAsync(context).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _eventStreams);
            }

            return;
        }

        bool gotSlot;
        try
        {
            gotSlot = await _requestSlots.WaitAsync(SlotWaitTimeout, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!gotSlot)
        {
            ApplySecurityHeaders(context.Response);
            WritePlainText(context, HttpStatusCode.ServiceUnavailable, "The app is busy. Try again in a moment.");
            return;
        }

        try
        {
            await ProcessCoreAsync(context).ConfigureAwait(false);
        }
        finally
        {
            _requestSlots.Release();
        }
    }

    private static bool IsEventStreamRequest(HttpListenerRequest request)
        => request.Url?.AbsolutePath.TrimEnd('/').EndsWith("/api/events", StringComparison.OrdinalIgnoreCase) == true;

    private async Task ProcessCoreAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Security: strictly validate Origin to prevent unauthorized cross-origin access and DNS rebinding
            if (!IsAllowedOrigin(request))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                response.Close();
                return;
            }

            ApplySecurityHeaders(response);

            // Handle preflight CORS OPTIONS requests
            if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;
            }

            var rawPath = request.Url?.AbsolutePath ?? "/";
            var relative = Uri.UnescapeDataString(rawPath).TrimStart('/');

            // Path traversal defense
            if (relative.Contains("..") || relative.Contains("/.") || relative.Contains("\\."))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                response.Close();
                return;
            }

            // Direct low-latency Bridge RPC and SSE events
            if (relative.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAuthorized(context, out bool granted))
                {
                    WritePlainText(context, HttpStatusCode.Forbidden, "IELTop is already running. Open the app window instead of this page.");
                    return;
                }

                if (granted)
                {
                    GrantAccess(response);
                }

                await HandleApiAsync(context, relative["api/".Length..]).ConfigureAwait(false);
                return;
            }

            // Media folder streaming (audio clips, diagrams, exam images)
            if (relative.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleMediaAsync(context, relative["media/".Length..]).ConfigureAwait(false);
                return;
            }

            // The UI document itself needs the key. Hashed bundle files do not,
            // because the page cannot be read without the key in the first place.
            if (IsDocumentRequest(relative))
            {
                if (!IsAuthorized(context, out bool granted))
                {
                    WritePlainText(context, HttpStatusCode.Forbidden, "IELTop is already running. Open the app window instead of this page.");
                    return;
                }

                if (granted)
                {
                    GrantAccess(response);
                }
            }

            // Static UI bundle assets
            await HandleStaticContentAsync(context, relative).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Close();
            }
            catch { }
            Console.Error.WriteLine($"[StaticFileServer] Request processing error: {ex.Message}");
        }
    }

    private bool IsAllowedOrigin(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin))
        {
            // Direct WebView navigation or non-CORS request from local host.
            // The access key is what stops those, not this check.
            return true;
        }

        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            bool isLoopback = uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                           || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

            if (isLoopback && uri.Port == _port)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the caller proved it was opened by this window. The key is
    /// accepted in the URL of the first page, in the cookie the page then
    /// keeps, or in a header for callers that have no browser cookie jar.
    /// </summary>
    private bool IsAuthorized(HttpListenerContext context, out bool newlyAuthorized)
    {
        var request = context.Request;
        newlyAuthorized = false;

        // A key in the URL is the window handing it over for the first time, so
        // it is worth handing straight back as a cookie the page can carry on
        // every call from then on.
        if (Matches(request.QueryString["k"]))
        {
            newlyAuthorized = true;
            return true;
        }

        return Matches(request.Headers["X-ILTop-Key"])
            || Matches(request.Cookies[AccessCookieName]?.Value);
    }

    private bool Matches(string? candidate)
        => !string.IsNullOrEmpty(candidate) && string.Equals(candidate, _accessKey, StringComparison.Ordinal);

    /// <summary>
    /// Hands the key to the browser as a cookie so every later fetch, audio
    /// request and event stream from the page carries it without the page
    /// having to know the key at all.
    /// </summary>
    private void GrantAccess(HttpListenerResponse response)
    {
        var header = $"{AccessCookieName}={_accessKey}; Path=/; HttpOnly; SameSite=Strict";
        try
        {
            // Set through Headers rather than the cookie collection: the
            // collection drops the HttpOnly flag when HttpListener serialises it.
            response.Headers.Add("Set-Cookie", header);
        }
        catch (Exception)
        {
            try
            {
                response.Cookies.Add(new Cookie(AccessCookieName, _accessKey, "/") { HttpOnly = true });
            }
            catch (Exception)
            {
                // Headers already sent. The current page still works, the next
                // navigation carries the key in the URL again.
            }
        }
    }

    /// <summary>
    /// A document is any request that runs the app code: the two pages and the
    /// extensionless routes the single page app navigates to. Hashed bundle
    /// files and media are not documents.
    /// </summary>
    private static bool IsDocumentRequest(string relative)
        => relative.Length == 0
            || !Path.HasExtension(relative)
            || relative.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    private static void WritePlainText(HttpListenerContext context, HttpStatusCode status, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private void ApplySecurityHeaders(HttpListenerResponse response)
    {
        // Restrict CORS precisely to loopback origin on this instance
        response.Headers["Access-Control-Allow-Origin"] = BaseUrl;
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Range, Authorization";
        response.Headers["Access-Control-Expose-Headers"] = "Content-Range, Content-Length, Accept-Ranges";
        response.Headers["Vary"] = "Origin";

        // Anti-XSS, anti-clickjacking, and MIME sniffing protection
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // Content Security Policy restricting script execution and network calls strictly to local resources.
        // The built bundle has no inline script and no eval, so neither is allowed here.
        response.Headers["Content-Security-Policy"] =
            "default-src 'self' " + BaseUrl + "; " +
            "script-src 'self' " + BaseUrl + "; " +
            "style-src 'self' 'unsafe-inline' " + BaseUrl + "; " +
            "img-src 'self' data: blob: " + BaseUrl + "; " +
            "media-src 'self' blob: " + BaseUrl + "; " +
            "connect-src 'self' " + BaseUrl + "; " +
            "frame-ancestors 'none'; object-src 'none'; base-uri 'self';";
    }

    private async Task HandleMediaAsync(HttpListenerContext context, string pathAfterMedia)
    {
        var response = context.Response;
        int slash = pathAfterMedia.IndexOf('/');
        if (slash <= 0)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        var prefix = pathAfterMedia[..slash];
        var name = pathAfterMedia[(slash + 1)..];

        var dirs = _media.Where(m => m.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Dir).ToList();

        string? mediaPath = null;
        foreach (var dir in dirs)
        {
            var root = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
            var candidatePath = Path.GetFullPath(Path.Combine(dir, name));
            if (candidatePath.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidatePath))
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

        await StreamFileAsync(context, mediaPath, isMedia: true).ConfigureAwait(false);
    }

    private async Task HandleStaticContentAsync(HttpListenerContext context, string relative)
    {
        var response = context.Response;
        if (string.IsNullOrEmpty(relative)) relative = "index.html";

        var candidate = Path.GetFullPath(Path.Combine(_root, relative));
        var inOutputRoot = candidate.StartsWith(_root, StringComparison.OrdinalIgnoreCase);

#if DEBUG
        // In development the bundle may not be copied next to the app yet, so
        // fall back to the project folder the app was built from. This stays
        // out of Release builds: a released app must never serve files from
        // outside its own folder.
        var devRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));

        if (!File.Exists(candidate))
        {
            var devCandidate = Path.GetFullPath(Path.Combine(devRoot, relative));
            if (File.Exists(devCandidate))
            {
                candidate = devCandidate;
                inOutputRoot = candidate.StartsWith(devRoot, StringComparison.OrdinalIgnoreCase);
            }
        }
#endif

        // SPA routing: non-extension paths fall back to index.html
        if (!File.Exists(candidate))
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

        // The candidate must sit inside a folder this server is allowed to read.
        if (!inOutputRoot)
        {
            response.StatusCode = (int)HttpStatusCode.Forbidden;
            response.Close();
            return;
        }

        await StreamFileAsync(context, candidate, isMedia: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams a file asynchronously supporting HTTP 206 Partial Content (Range requests)
    /// to provide fast, lag-free audio seeking and optimal memory usage.
    /// </summary>
    private static async Task StreamFileAsync(HttpListenerContext context, string filePath, bool isMedia)
    {
        var request = context.Request;
        var response = context.Response;

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        long fileLength = fileInfo.Length;
        var extension = Path.GetExtension(filePath);
        response.ContentType = MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
        response.Headers["Accept-Ranges"] = "bytes";

        // Caching policy
        if (isMedia)
        {
            response.Headers["Cache-Control"] = "public, max-age=86400";
        }
        else if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        }
        else
        {
            // Immutable hashed build assets
            response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        }

        var rangeHeader = request.Headers["Range"];
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            // Standard full response (200 OK)
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentLength64 = fileLength;

            if (request.HttpMethod.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                response.Close();
                return;
            }

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, useAsync: true);
            await fs.CopyToAsync(response.OutputStream, StreamBufferSize).ConfigureAwait(false);
            response.Close();
            return;
        }

        // Parse Range: bytes=start-end
        var rangeSpec = rangeHeader["bytes=".Length..].Trim();
        var dashIndex = rangeSpec.IndexOf('-');
        if (dashIndex < 0)
        {
            response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
            response.Headers["Content-Range"] = $"bytes */{fileLength}";
            response.Close();
            return;
        }

        long start = 0;
        long end = fileLength - 1;

        var startPart = rangeSpec[..dashIndex];
        var endPart = rangeSpec[(dashIndex + 1)..];

        if (!string.IsNullOrEmpty(startPart))
        {
            if (!long.TryParse(startPart, out start) || start < 0 || start >= fileLength)
            {
                response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                response.Headers["Content-Range"] = $"bytes */{fileLength}";
                response.Close();
                return;
            }
        }

        if (!string.IsNullOrEmpty(endPart))
        {
            if (string.IsNullOrEmpty(startPart))
            {
                // Suffix byte range: bytes=-500 (last 500 bytes)
                if (long.TryParse(endPart, out var suffix) && suffix > 0)
                {
                    start = Math.Max(0, fileLength - suffix);
                }
            }
            else if (long.TryParse(endPart, out var specifiedEnd) && specifiedEnd >= start)
            {
                end = Math.Min(specifiedEnd, fileLength - 1);
            }
        }

        long rangeLength = end - start + 1;
        response.StatusCode = (int)HttpStatusCode.PartialContent;
        response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileLength}";
        response.ContentLength64 = rangeLength;

        if (request.HttpMethod.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            response.Close();
            return;
        }

        await using var rangeStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, useAsync: true);
        rangeStream.Seek(start, SeekOrigin.Begin);

        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            long bytesRemaining = rangeLength;
            while (bytesRemaining > 0)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, bytesRemaining);
                int bytesRead = await rangeStream.ReadAsync(buffer.AsMemory(0, bytesToRead)).ConfigureAwait(false);
                if (bytesRead == 0) break;

                await response.OutputStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                bytesRemaining -= bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            response.Close();
        }
    }

    private async Task HandleApiAsync(HttpListenerContext context, string endpoint)
    {
        var request = context.Request;
        var response = context.Response;

        // Health check
        if (endpoint.Equals("health", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            response.Close();
            return;
        }

        // Bridge RPC: single call
        if (endpoint.Equals("call", StringComparison.OrdinalIgnoreCase))
        {
            if (Router is null)
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.Close();
                return;
            }

            var req = await ReadJsonBodyAsync<BridgeRequest>(context).ConfigureAwait(false);
            if (req is null) return;

            var res = await Router.HandleDirectAsync(req.Id, req.Method, req.Args, _cts.Token).ConfigureAwait(false);
            await WriteJsonAsync(context, res).ConfigureAwait(false);
            return;
        }

        // Server-Sent Events (SSE) stream for real-time exam and clock updates
        if (endpoint.Equals("events", StringComparison.OrdinalIgnoreCase))
        {
            await HandleSseConnectionAsync(context).ConfigureAwait(false);
            return;
        }

        response.StatusCode = (int)HttpStatusCode.NotFound;
        response.Close();
    }

    /// <summary>
    /// Reads and parses a JSON request body, answering the request itself when
    /// the body is missing, malformed or past the size limit. The size is
    /// counted while reading rather than trusted from the Content-Length
    /// header, so a chunked body cannot slip past it.
    /// </summary>
    private static async Task<T?> ReadJsonBodyAsync<T>(HttpListenerContext context)
    {
        var response = context.Response;

        if (context.Request.ContentLength64 > MaxRequestBodyLength)
        {
            response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
            response.Close();
            return default;
        }

        var body = new MemoryStream();
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
            try
            {
                int read;
                while ((read = await context.Request.InputStream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    if (body.Length + read > MaxRequestBodyLength)
                    {
                        response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                        response.Close();
                        return default;
                    }

                    body.Write(buffer, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var parsed = JsonSerializer.Deserialize<T>(body.GetBuffer().AsSpan(0, (int)body.Length), BridgeJson.Options);
            if (parsed is null)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Close();
            }

            return parsed;
        }
        catch (JsonException)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.Close();
            return default;
        }
        finally
        {
            body.Dispose();
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BridgeJson.Options);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private async Task HandleSseConnectionAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        response.SendChunked = true;

        var clientId = Guid.NewGuid();
        var clientDisconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeLock = new SemaphoreSlim(1, 1);

        Func<string, Task> sendRawAsync = async (rawMessage) =>
        {
            await writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var data = Encoding.UTF8.GetBytes(rawMessage);
                await response.OutputStream.WriteAsync(data).ConfigureAwait(false);
                await response.OutputStream.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                clientDisconnectedTcs.TrySetResult();
            }
            finally
            {
                writeLock.Release();
            }
        };

        _sseClients[clientId] = sendRawAsync;

        Action<string, object?> broadcastHandler = (evt, payload) =>
        {
            try
            {
                string msg;
                if (payload is string s && s.TrimStart().StartsWith('{'))
                {
                    msg = s;
                }
                else
                {
                    msg = JsonSerializer.Serialize(new { @event = evt, payload }, BridgeJson.Options);
                }

                _ = sendRawAsync($"data: {msg}\n\n");
            }
            catch
            {
                clientDisconnectedTcs.TrySetResult();
            }
        };

        if (Router != null) Router.OnEventBroadcast += broadcastHandler;

        // Initial connect ping
        _ = sendRawAsync(": ping\n\n");

        using var reg = _cts.Token.Register(() => clientDisconnectedTcs.TrySetResult());
        await clientDisconnectedTcs.Task.ConfigureAwait(false);

        _sseClients.TryRemove(clientId, out _);
        if (Router != null) Router.OnEventBroadcast -= broadcastHandler;

        // The semaphore is deliberately not disposed: the heartbeat loop can be
        // mid write for this client, and disposing it under that write throws
        // and used to take the whole heartbeat loop down with it.
        try { response.Close(); } catch { }
    }

    private async Task SseHeartbeatLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!_cts.IsCancellationRequested && await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
        {
            if (_sseClients.IsEmpty) continue;

            foreach (var (_, sender) in _sseClients)
            {
                try
                {
                    await sender(": ping\n\n").ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // One client went away between the check and the write. The
                    // other clients must keep their stream alive, so the failure
                    // is swallowed here instead of ending this loop.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        _requestSlots.Dispose();
        _cts.Dispose();
    }
}

