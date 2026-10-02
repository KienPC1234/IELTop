using Microsoft.AspNetCore.Http;
using System.Net;

namespace IELTop_Content_Server;

public static class HttpContextExtensions
{
    /// <summary>
    /// Extracts the real client IP address, properly handling Cloudflare Tunnel (CF-Connecting-IP),
    /// X-Forwarded-For, and standard remote IP.
    /// </summary>
    public static string GetClientIp(this HttpContext context)
    {
        // 1. Cloudflare Tunnel / CDN sets CF-Connecting-IP
        if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) &&
            !string.IsNullOrWhiteSpace(cfIp))
        {
            string ip = cfIp.ToString().Trim();
            if (IPAddress.TryParse(ip, out _))
                return ip;
        }

        // 2. Standard X-Forwarded-For
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded) &&
            !string.IsNullOrWhiteSpace(forwarded))
        {
            string[] parts = forwarded.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && IPAddress.TryParse(parts[0], out _))
                return parts[0];
        }

        // 3. X-Real-IP
        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) &&
            !string.IsNullOrWhiteSpace(realIp))
        {
            string ip = realIp.ToString().Trim();
            if (IPAddress.TryParse(ip, out _))
                return ip;
        }

        // 4. Fallback to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
