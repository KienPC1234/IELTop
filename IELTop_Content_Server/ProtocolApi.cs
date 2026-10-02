using System.Text.Json;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace IELTop_Content_Server;

/// <summary>
/// The public content API, protocol ieltop/1. This is what the app
/// talks to. Everything here is async, guarded by the protocol auth
/// modes, and served through the cache.
/// </summary>
public static class ProtocolApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapProtocolApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting("protocol");

        // Health endpoint for clients and monitoring.
        api.MapGet("/health", async (
            IProtocolService protocol,
            IPaperService papers,
            IAudioService audio,
            IContentCache cache,
            IS3StorageService s3,
            Microsoft.Extensions.Options.IOptions<IELTop_Content_Server.Options.DatabaseOptions> dbOptions,
            Microsoft.Extensions.Options.IOptions<IELTop_Content_Server.Options.CacheOptions> cacheOptions,
            CancellationToken ct) =>
        {
            bool cacheOk = await cache.PingAsync(ct);
            bool s3Ok = !s3.Enabled || await s3.PingAsync(ct);
            int paperCount = await papers.CountAsync(ct);
            int audioCount = (await audio.ListAsync(ct)).Count;
            var greeting = await protocol.GreetingAsync(ct);

            var status = cacheOk && s3Ok && greeting.Auth.Count > 0 ? "healthy" : "degraded";
            var response = new
            {
                status,
                protocol = greeting.Protocol,
                database = dbOptions.Value.Provider,
                cache = new
                {
                    provider = cacheOptions.Value.Provider,
                    healthy = cacheOk
                },
                storage = new
                {
                    provider = s3.Enabled ? "S3" : "Local Disk",
                    bucket = s3.Enabled ? s3.BucketName : string.Empty,
                    healthy = s3Ok
                },
                authModes = greeting.Auth,
                papers = paperCount,
                audio = audioCount,
                timestamp = DateTimeOffset.UtcNow
            };

            return Results.Json(response, Json,
                statusCode: status == "healthy" ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        // Greeting is always open. The app reads it to learn how to
        // authenticate, so it must never require authentication.
        api.MapGet("/info", async (IProtocolService protocol, IStatsService stats, CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var greeting = await protocol.GreetingAsync(ct);
            return Results.Json(greeting, Json);
        });

        // Client configuration profile for branded desktop apps and web clients.
        api.MapGet("/client/profile", async (ICustomClientService clientService, IStatsService stats, CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var profile = await clientService.GetProfileAsync(ct);
            return Results.Json(profile, Json);
        });

        // Login is open but carries its own tighter per address limit,
        // so the password endpoint cannot be brute forced.
        api.MapPost("/login", async (
            HttpRequest request, IProtocolService protocol, IAuditService audit, IStatsService stats,
            CancellationToken ct) =>
        {
            stats.RecordApiCall();
            LoginRequest? body;
            try
            {
                body = await request.ReadFromJsonAsync<LoginRequest>(ct);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "The request could not be read." }, Json,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (ok, error, token) = await protocol.LoginAsync(
                body?.Username ?? string.Empty, body?.Password ?? string.Empty, ct);

            if (!ok)
            {
                await audit.WriteAsync("anonymous", "login.failed",
                    body?.Username ?? string.Empty, error, ClientIp(request), ct);
                return Results.Json(new { error }, Json, statusCode: StatusCodes.Status401Unauthorized);
            }

            await audit.WriteAsync(body?.Username ?? string.Empty, "login.ok",
                body?.Username ?? string.Empty, string.Empty, ClientIp(request), ct);
            RateLimitKeys.Remember("token:" + token);
            return Results.Json(new { token }, Json);
        }).RequireRateLimiting("login");

        // Paper list, guarded.
        api.MapGet("/papers", async (
            HttpRequest request, string? skill, string? category, string? q,
            IProtocolService protocol, IPaperService papers, IStatsService stats,
            CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var decision = await protocol.AuthorizeAsync(request, ct);
            if (!decision.Allowed)
                return Denied();

            RateLimitKeys.RememberFromRequest(request.HttpContext);
            var list = await papers.ListAsync(skill, category, q, ct);
            return Results.Json(list, Json);
        });

        // One full paper, guarded. Supports deferred answer hiding (mode=exam)
        // and proprietary access authorization for exclusive academy materials.
        api.MapGet("/papers/{id}", async (
            string id,
            HttpRequest request,
            IProtocolService protocol,
            IPaperService papers,
            IExamProtectionService protection,
            IStatsService stats,
            string? mode,
            bool? hideAnswers,
            string? code,
            CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var decision = await protocol.AuthorizeAsync(request, ct);
            if (!decision.Allowed)
                return Denied();

            RateLimitKeys.RememberFromRequest(request.HttpContext);
            string safe = PaperService.SafeId(id);
            var meta = await papers.GetAsync(safe, ct);
            if (meta is null)
                return Results.Json(new { error = "not found" }, Json,
                    statusCode: StatusCodes.Status404NotFound);

            // Exclusive exam protection check:
            // Must have matching exclusive license code (header or query parameter) or master administrator rights.
            if (meta.IsExclusive)
            {
                string providedCode = code
                    ?? request.Headers["X-Exclusive-Code"].ToString()
                    ?? request.Headers["X-Access-Code"].ToString()
                    ?? string.Empty;

                bool isAuthorizedExclusive =
                    (!string.IsNullOrWhiteSpace(meta.ExclusiveCode) && string.Equals(meta.ExclusiveCode, providedCode.Trim(), StringComparison.Ordinal))
                    || decision.MasterCode
                    || decision.AccountId is not null;

                if (!isAuthorizedExclusive)
                {
                    return Results.Json(new
                    {
                        error = "Exclusive proprietary exam paper. Valid center access code or administrator account required.",
                        isExclusive = true
                    }, Json, statusCode: StatusCodes.Status403Forbidden);
                }
            }

            string? json = await papers.GetJsonAsync(safe, ct);
            if (json is null)
                return Results.Json(new { error = "not found" }, Json,
                    statusCode: StatusCodes.Status404NotFound);

            stats.RecordPaperDownload();
            await papers.RecordDownloadAsync(safe, ct);

            bool hideAnswersFlag = string.Equals(mode, "exam", StringComparison.OrdinalIgnoreCase) || hideAnswers == true;
            string clientIp = ClientIp(request);
            string protectedJson = protection.ProtectPaperJson(json, safe, decision.Identity, clientIp, hideAnswersFlag);

            return Results.Content(protectedJson, "application/json; charset=utf-8");
        });

        // Verifies digital anti-leak watermark signature from any exported or leaked exam JSON.
        api.MapPost("/papers/verify-watermark", async (
            HttpRequest request,
            IProtocolService protocol,
            IExamProtectionService protection,
            IStatsService stats,
            CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var decision = await protocol.AuthorizeAsync(request, ct);
            if (!decision.Allowed)
                return Denied();

            using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8);
            string bodyText = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(bodyText))
                return Results.Json(new { error = "Request body is empty." }, Json,
                    statusCode: StatusCodes.Status400BadRequest);

            string jsonToInspect = bodyText;
            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("paperJson", out var prop))
                {
                    jsonToInspect = prop.GetString() ?? bodyText;
                }
            }
            catch
            {
                // bodyText may be raw paper JSON
            }

            var (valid, licensedTo, clientIp, issuedAt, error) = protection.VerifyWatermark(jsonToInspect);
            var result = new
            {
                valid,
                licensedTo,
                clientIp,
                issuedAt,
                error = valid ? null : error
            };

            return Results.Json(result, Json,
                statusCode: valid ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
        });

        // One audio clip, guarded. Range requests and caching headers
        // are handled by Results.File so a player can seek.
        api.MapGet("/audio/{file}", async (
            string file, HttpRequest request, IProtocolService protocol,
            IAudioService audio, IStatsService stats, CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var decision = await protocol.AuthorizeAsync(request, ct);
            if (!decision.Allowed)
                return Denied();

            RateLimitKeys.RememberFromRequest(request.HttpContext);
            string? path = await audio.EnsureLocalAsync(file, ct);
            if (path is null)
                return Results.Json(new { error = "not found" }, Json,
                    statusCode: StatusCodes.Status404NotFound);

            string safe = AudioService.SafeFileName(file);
            stats.RecordAudioServed();
            await audio.RecordDownloadAsync(safe, ct);

            return Results.File(
                path,
                contentType: audio.ContentTypeFor(safe),
                enableRangeProcessing: true,
                lastModified: File.GetLastWriteTimeUtc(path),
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
                    '"' + ShortHash(path) + '"'));
        });
    }

    private static IResult Denied() =>
        Results.Json(new { error = "access denied" }, Json,
            statusCode: StatusCodes.Status401Unauthorized);

    private static string ClientIp(HttpRequest request) =>
        request.HttpContext.GetClientIp();

    private static string ShortHash(string path)
    {
        try
        {
            var info = new FileInfo(path);
            string seed = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(seed)))[..16];
        }
        catch (IOException)
        {
            return "0";
        }
    }

    private sealed class LoginRequest
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
