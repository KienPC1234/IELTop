using System.Security.Cryptography;
using IELTop_Content_Server.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Bearer tokens given out by POST /api/login. Tokens live in the
/// distributed cache and expire on their own, so nothing needs a
/// cleanup job and a restart does not sign everyone out forever.
/// </summary>
public interface ITokenService
{
    Task<string> IssueAsync(int accountId, string username, CancellationToken ct = default);

    /// <summary>Returns "accountId:username" for a live token, else null.</summary>
    Task<string?> ResolveAsync(string token, CancellationToken ct = default);
}

public sealed class TokenService : ITokenService
{
    private const string Prefix = "token:";
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _lifetime;

    public TokenService(IDistributedCache cache, IOptions<ServerOptions> options)
    {
        _cache = cache;
        _lifetime = TimeSpan.FromHours(Math.Max(1, options.Value.TokenHours));
    }

    public async Task<string> IssueAsync(int accountId, string username, CancellationToken ct = default)
    {
        string raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await _cache.SetStringAsync(
            Prefix + raw,
            $"{accountId}:{username}",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _lifetime },
            ct);
        return raw;
    }

    public async Task<string?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        try
        {
            return await _cache.GetStringAsync(Prefix + token, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
