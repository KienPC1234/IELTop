using System.Net.Http.Json;
using System.Text.Json.Serialization;
using IELTop_Content_Server.Options;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Verifies a Cloudflare Turnstile token on the public portal forms.
/// The check is off when no keys are set, so the server works offline
/// and a developer is not blocked.
/// </summary>
public sealed record MathChallenge(string Question, string Signature);

public interface ICaptchaService
{
    bool Enabled { get; }
    string SiteKey { get; }
    MathChallenge CreateMathChallenge();
    Task<bool> VerifyAsync(string? token, string remoteIp, CancellationToken ct = default);
    Task<bool> VerifySubmissionAsync(string? turnstileToken, string? mathAnswer, string? mathSignature, string remoteIp, CancellationToken ct = default);
}

public sealed class CaptchaService(
    IHttpClientFactory clients,
    IOptions<CaptchaOptions> options,
    IWebHostEnvironment environment,
    ILogger<CaptchaService> logger) : ICaptchaService
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    // Cloudflare published test keys. They always pass, so a developer
    // can exercise the form without a site.
    private const string TestSiteKey = "1x00000000000000000000AA";
    private const string TestSecretKey = "1x0000000000000000000000000000000AA";

    private static readonly byte[] InternalSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    private readonly CaptchaOptions _options = options.Value;

    public bool Enabled => _options.Enabled && !IsTestKeyInRelease();
    public string SiteKey => _options.SiteKey;

    private bool IsTestKeyInRelease() =>
        !environment.IsDevelopment() && !_options.AllowTestKeys && _options.SecretKey == TestSecretKey;

    public MathChallenge CreateMathChallenge()
    {
        int a = Random.Shared.Next(2, 19);
        int b = Random.Shared.Next(1, 14);
        int expected = a + b;
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string payload = $"{expected}:{timestamp}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(InternalSecret);
        string hash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        string signature = $"{timestamp}:{hash}";
        return new MathChallenge($"{a} + {b} = ?", signature);
    }

    public async Task<bool> VerifySubmissionAsync(
        string? turnstileToken, string? mathAnswer, string? mathSignature, string remoteIp, CancellationToken ct = default)
    {
        if (Enabled)
        {
            return await VerifyAsync(turnstileToken, remoteIp, ct);
        }

        if (string.IsNullOrWhiteSpace(mathAnswer) || string.IsNullOrWhiteSpace(mathSignature))
            return false;

        var parts = mathSignature.Split(':', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out long ts))
            return false;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > 900)
            return false;

        string expectedHash = parts[1];
        string payload = $"{mathAnswer.Trim()}:{ts}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(InternalSecret);
        string actualHash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(actualHash),
            System.Text.Encoding.UTF8.GetBytes(expectedHash));
    }

    public async Task<bool> VerifyAsync(string? token, string remoteIp, CancellationToken ct = default)
    {
        if (!Enabled)
            return true;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var client = clients.CreateClient("turnstile");
            var payload = new Dictionary<string, string>
            {
                ["secret"] = _options.SecretKey,
                ["response"] = token
            };
            if (!string.IsNullOrWhiteSpace(remoteIp) && !IpAbuseGuard.IsInternalOrLoopback(remoteIp))
                payload["remoteip"] = remoteIp;

            using var response = await client.PostAsync(
                VerifyUrl, new FormUrlEncodedContent(payload), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile returned HTTP {Status}", (int)response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<TurnstileResult>(ct);
            if (body is null || !body.Success)
            {
                logger.LogInformation("Turnstile rejected: {Codes}",
                    body is null ? "no body" : string.Join(",", body.ErrorCodes ?? new List<string>()));
                return false;
            }
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Default is to refuse when Cloudflare cannot be reached, so
            // an outage is not a way around the check. An operator who
            // would rather keep the form open can set Captcha FailOpen.
            logger.LogWarning(e, "Turnstile could not be reached. FailOpen={FailOpen}", _options.FailOpen);
            return _options.FailOpen;
        }
    }

    private sealed class TurnstileResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string>? ErrorCodes { get; set; }
    }
}
