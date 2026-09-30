using IELTop_Content_Server.Options;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Pages;

/// <summary>
/// A small self check that anyone can open. It reports readiness, not
/// secrets: no codes, no keys, no file paths.
/// </summary>
[AllowAnonymous]
public sealed class HealthModel(
    IProtocolService protocol,
    IPaperService papers,
    IAudioService audio,
    IOptions<DatabaseOptions> database,
    IOptions<CacheOptions> cache) : PageModel
{
    public bool Healthy { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string Protocol { get; private set; } = "ieltop/1";
    public string BaseUrl { get; private set; } = string.Empty;
    public int PaperCount { get; private set; }
    public int AudioCount { get; private set; }
    public List<string> AuthModes { get; private set; } = new();
    public string DatabaseProvider { get; private set; } = string.Empty;
    public string CacheProvider { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken ct)
    {
        DatabaseProvider = database.Value.Provider;
        CacheProvider = cache.Value.Provider;
        BaseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            var greeting = await protocol.GreetingAsync(ct);
            AuthModes = greeting.Auth;
            PaperCount = await papers.CountAsync(ct);
            AudioCount = (await audio.ListAsync(ct)).Count;
            Protocol = greeting.Protocol;

            Healthy = AuthModes.Count > 0;
            Message = Healthy
                ? "Clients can connect. Papers and audio are served through the cache."
                : "No auth mode is enabled, so every client is refused.";
        }
        catch (Exception e)
        {
            Healthy = false;
            Message = $"The store could not be read: {e.GetType().Name}.";
        }
    }
}
