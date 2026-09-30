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
public interface ICaptchaService
{
    bool Enabled { get; }
    string SiteKey { get; }
    Task<bool> VerifyAsync(string? token, string remoteIp, CancellationToken ct = default);
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

    private readonly CaptchaOptions _options = options.Value;

    public bool Enabled => _options.Enabled && !IsTestKeyInRelease();
    public string SiteKey => _options.SiteKey;

    private bool IsTestKeyInRelease() =>
        !environment.IsDevelopment() && _options.SecretKey == TestSecretKey;

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
            if (!string.IsNullOrWhiteSpace(remoteIp))
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
