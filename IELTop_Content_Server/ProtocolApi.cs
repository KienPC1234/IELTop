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

        // Greeting is always open. The app reads it to learn how to
        // authenticate, so it must never require authentication.
        api.MapGet("/info", async (IProtocolService protocol, IStatsService stats, CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var greeting = await protocol.GreetingAsync(ct);
            return Results.Json(greeting, Json);
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

        // One full paper, guarded.
        api.MapGet("/papers/{id}", async (
            string id, HttpRequest request, IProtocolService protocol,
            IPaperService papers, IStatsService stats, CancellationToken ct) =>
        {
            stats.RecordApiCall();
            var decision = await protocol.AuthorizeAsync(request, ct);
            if (!decision.Allowed)
                return Denied();

            RateLimitKeys.RememberFromRequest(request.HttpContext);
            string safe = PaperService.SafeId(id);
            string? json = await papers.GetJsonAsync(safe, ct);
            if (json is null)
                return Results.Json(new { error = "not found" }, Json,
                    statusCode: StatusCodes.Status404NotFound);

            stats.RecordPaperDownload();
            await papers.RecordDownloadAsync(safe, ct);
            return Results.Content(json, "application/json; charset=utf-8");
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
            string? path = audio.ResolveExisting(file);
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
        request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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
