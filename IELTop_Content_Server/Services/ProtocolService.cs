using System.Text.Json;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Everything the public protocol needs for one request: who the
/// caller is, and the greeting data. All reads go through the cache.
/// </summary>
public interface IProtocolService
{
    Task<ServerGreeting> GreetingAsync(CancellationToken ct = default);
    Task<AccessDecision> AuthorizeAsync(Microsoft.AspNetCore.Http.HttpRequest request, CancellationToken ct = default);
    Task<(bool Ok, string Error, string Token)> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<long> CountPapersAsync(CancellationToken ct = default);
    Task<long> CountAudioAsync(CancellationToken ct = default);
    Task<long> CountAccessCodesAsync(CancellationToken ct = default);
}

public sealed class ServerGreeting
{
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = "ieltop/1";
    public List<string> Auth { get; set; } = new();
    public List<string> Skills { get; set; } = new();
}

/// <summary>
/// Why a request was allowed. A credential call may refresh LastUsedAt
/// and counts only when it actually used a stored row.
/// </summary>
public sealed class AccessDecision
{
    public bool Allowed { get; set; }
    public bool MasterCode { get; set; }
    public int? AccessCodeId { get; set; }
    public int? AccountId { get; set; }
    public string Identity { get; set; } = "anonymous";
}

public sealed class ProtocolService(
    IDbContextFactory<AppDbContext> dbFactory,
    IContentCache cache,
    ITokenService tokens,
    IWriteThrottle throttle,
    IOptions<ServerOptions> serverOptions,
    IOptions<CacheOptions> cacheOptions,
    ILogger<ProtocolService> logger) : IProtocolService
{
    private const string CodePrefix = "code:";
    private const string AccountPrefix = "account:";

    // Side effect writes are at most once per window per subject, so a
    // busy client does not turn every read into an update.
    private static readonly TimeSpan TouchWindow = TimeSpan.FromMinutes(5);

    private readonly ServerOptions _server = serverOptions.Value;
    private readonly CacheOptions _cache = cacheOptions.Value;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<ServerGreeting> GreetingAsync(CancellationToken ct = default)
    {
        long version = await cache.VersionAsync(ct);
        string key = $"v{version}:greeting";

        return await cache.GetOrAddAsync(
            key,
            TimeSpan.FromSeconds(Math.Max(5, _cache.SettingsSeconds)),
            async token =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token);
                var settings = await db.Settings.AsNoTracking().ToListAsync(token);
                var map = settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

                string name = map.TryGetValue("server.name", out var stored) && stored.Length > 0
                    ? stored
                    : _server.Name;

                var auth = new List<string>();
                if (bool.TryParse(map.GetValueOrDefault("auth.anonymous"), out var anon) ? anon : _server.AllowAnonymous)
                    auth.Add("anonymous");
                if (bool.TryParse(map.GetValueOrDefault("auth.code"), out var code) ? code : _server.AllowAccessCode)
                    auth.Add("code");
                if (bool.TryParse(map.GetValueOrDefault("auth.login"), out var login) ? login : _server.AllowLogin)
                    auth.Add("login");

                var skills = MapList(map.GetValueOrDefault("server.skills"), _server.Skills)
                    .Select(s => s.ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ServerGreeting
                {
                    Name = name,
                    Protocol = "ieltop/1",
                    Auth = auth,
                    Skills = skills
                };
            },
            ct);
    }

    public async Task<AccessDecision> AuthorizeAsync(
        Microsoft.AspNetCore.Http.HttpRequest request, CancellationToken ct = default)
    {
        var greeting = await GreetingAsync(ct);
        bool anonymousAllowed = greeting.Auth.Contains("anonymous", StringComparer.OrdinalIgnoreCase);
        bool codeAllowed = greeting.Auth.Contains("code", StringComparer.OrdinalIgnoreCase);
        bool loginAllowed = greeting.Auth.Contains("login", StringComparer.OrdinalIgnoreCase);

        string headerCode = request.Headers["X-Access-Code"].FirstOrDefault() ?? string.Empty;
        string bearer = ExtractBearer(request);

        // Bearer wins when both are present, so a signed in client that
        // also carries a class code is treated as the signed in user.
        if (loginAllowed && bearer.Length > 0)
        {
            var account = await ResolveAccountFromTokenAsync(bearer, ct);
            if (account is not null)
            {
                await TouchAccountAsync(account.Id, ct);
                return new AccessDecision
                {
                    Allowed = true,
                    AccountId = account.Id,
                    Identity = account.Username
                };
            }
        }

        if (codeAllowed && headerCode.Length > 0)
        {
            if (_server.MasterCode.Length > 0 && FixedEquals(headerCode, _server.MasterCode))
            {
                return new AccessDecision { Allowed = true, MasterCode = true, Identity = "master" };
            }

            var row = await FindCodeAsync(headerCode, ct);
            if (row is not null)
            {
                await TouchCodeAsync(row.Id, ct);
                return new AccessDecision { Allowed = true, AccessCodeId = row.Id, Identity = row.Name };
            }
        }

        if (anonymousAllowed)
            return new AccessDecision { Allowed = true, Identity = "anonymous" };

        return new AccessDecision { Allowed = false, Identity = "denied" };
    }

    private async Task<LoginAccount?> ResolveAccountFromTokenAsync(string token, CancellationToken ct)
    {
        // The token value embeds the account id, so a disabled account
        // stops working even before its token expires.
        string payload = await tokens.ResolveAsync(token, ct) ?? string.Empty;
        if (payload.Length == 0)
            return null;
        int cut = payload.IndexOf(':');
        if (cut <= 0 || !int.TryParse(payload[..cut], out int accountId))
            return null;

        string key = AccountPrefix + accountId;
        var cached = await cache.GetOrAddAsync(
            key,
            TimeSpan.FromSeconds(30),
            async token2 =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(token2);
                var row = await db.LoginAccounts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == accountId, token2);
                return row is null
                    ? new CachedAccount()
                    : new CachedAccount
                    {
                        Id = row.Id,
                        Username = row.Username,
                        IsActive = row.IsActive
                    };
            },
            ct);

        if (cached is null || cached.Id == 0 || !cached.IsActive)
            return null;

        return new LoginAccount { Id = cached.Id, Username = cached.Username, IsActive = true };
    }

    /// <summary>Account facts safe to keep in the shared cache.</summary>
    private sealed class CachedAccount
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    private async Task<AccessCode?> FindCodeAsync(string code, CancellationToken ct)
    {
        string key = CodePrefix + code;
        var cached = await cache.GetAsync<CachedCode>(key, ct);
        if (cached is null)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var row = await db.AccessCodes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == code && c.IsActive, ct);
            if (row is null)
                return null;
            cached = new CachedCode { Id = row.Id, Name = row.Name, ExpiresAt = row.ExpiresAt };
            await cache.SetAsync(key, cached, TimeSpan.FromSeconds(Math.Max(5, _cache.SettingsSeconds)), ct);
        }

        if (cached.ExpiresAt is not null && cached.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        return new AccessCode { Id = cached.Id, Name = cached.Name, ExpiresAt = cached.ExpiresAt, IsActive = true };
    }

    /// <summary>Code facts safe to keep in the shared cache.</summary>
    private sealed class CachedCode
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private async Task TouchCodeAsync(int id, CancellationToken ct)
    {
        if (!throttle.ShouldWrite($"code:{id}", TouchWindow))
            return;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.AccessCodes
                .Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.UseCount, c => c.UseCount + 1)
                    .SetProperty(c => c.LastUsedAt, DateTimeOffset.UtcNow), ct);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not record access code use.");
        }
    }

    private async Task TouchAccountAsync(int id, CancellationToken ct)
    {
        if (!throttle.ShouldWrite($"account:{id}", TouchWindow))
            return;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.LoginAccounts
                .Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastLoginAt, DateTimeOffset.UtcNow), ct);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not record login account use.");
        }
    }

    public async Task<(bool Ok, string Error, string Token)> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        var greeting = await GreetingAsync(ct);
        if (!greeting.Auth.Contains("login", StringComparer.OrdinalIgnoreCase))
            return (false, "Login is not enabled on this server.", string.Empty);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, "Enter a username and a password.", string.Empty);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.LoginAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username == username && a.IsActive, ct);
        if (account is null || !PasswordHasher.Verify(password, account.PasswordHash, account.PasswordSalt))
            return (false, "Wrong username or password.", string.Empty);

        string token = await tokens.IssueAsync(account.Id, account.Username, ct);
        await TouchAccountAsync(account.Id, ct);
        return (true, string.Empty, token);
    }

    public async Task<long> CountPapersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Papers.LongCountAsync(ct);
    }

    public async Task<long> CountAudioAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Audio.LongCountAsync(ct);
    }

    public async Task<long> CountAccessCodesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AccessCodes.LongCountAsync(ct);
    }

    private static string ExtractBearer(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        var header = request.Headers.Authorization.FirstOrDefault() ?? string.Empty;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : string.Empty;
    }

    private static bool FixedEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
    }

    private static List<string> MapList(string? raw, List<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback.Count > 0 ? fallback : ServerOptions.DefaultSkills;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(raw, Json);
            return list is { Count: > 0 } ? list : fallback;
        }
        catch (JsonException)
        {
            return fallback.Count > 0 ? fallback : ServerOptions.DefaultSkills;
        }
    }
}
