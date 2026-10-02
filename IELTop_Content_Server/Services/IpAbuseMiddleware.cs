using IELTop_Content_Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Intercepts incoming HTTP requests to block restricted IPs and detect
/// automated vulnerability scanner probes.
/// </summary>
public sealed class IpAbuseMiddleware(
    RequestDelegate next,
    ILogger<IpAbuseMiddleware> logger)
{
    private static readonly HashSet<string> SensitiveProbePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/.env",
        "/.git",
        "/.git/config",
        "/.svn",
        "/.vscode",
        "/.aws",
        "/wp-login.php",
        "/wp-admin",
        "/xmlrpc.php",
        "/phpmyadmin",
        "/pma",
        "/adminer",
        "/solr",
        "/actuator",
        "/actuator/health",
        "/boaform",
        "/vendor/.env",
        "/server-status"
    };

    public async Task InvokeAsync(HttpContext context, IIpAbuseGuard abuseGuard)
    {
        string clientIp = context.GetClientIp();

        // 1. Check if client IP is currently banned
        if (!IpAbuseGuard.IsInternalOrLoopback(clientIp))
        {
            if (await abuseGuard.IsBlockedAsync(clientIp, context.RequestAborted))
            {
                logger.LogWarning("Blocked request from banned IP: {Ip} on {Path}", clientIp, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    "Access Forbidden: Your IP address has been temporarily restricted due to policy violations.",
                    context.RequestAborted);
                return;
            }
        }

        // 2. Exploit probe detection
        string path = context.Request.Path.Value ?? string.Empty;
        if (!string.IsNullOrEmpty(path) && path.Length > 1)
        {
            string normalized = path.TrimEnd('/');
            if (SensitiveProbePaths.Contains(normalized) ||
                normalized.StartsWith("/.env", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/.git/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/wp-includes", StringComparison.OrdinalIgnoreCase))
            {
                if (!IpAbuseGuard.IsInternalOrLoopback(clientIp))
                {
                    await abuseGuard.RecordSensitiveProbeAsync(clientIp, path, context.RequestAborted);
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await next(context);
    }
}

