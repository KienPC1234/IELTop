using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages;

/// <summary>
/// A public page anyone can read, so a visitor sees what this server is
/// and how to connect.
/// </summary>
[AllowAnonymous]
public sealed class AboutModel(
    IProtocolService protocol,
    IAudioService audio) : PageModel
{
    public string Name { get; private set; } = string.Empty;
    public string Protocol { get; private set; } = "ieltop/1";
    public string BaseUrl { get; private set; } = string.Empty;
    public long PaperCount { get; private set; }
    public int AudioCount { get; private set; }
    public List<string> AuthModes { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var greeting = await protocol.GreetingAsync(ct);
        Name = greeting.Name;
        Protocol = greeting.Protocol;
        AuthModes = greeting.Auth;
        PaperCount = await protocol.CountPapersAsync(ct);
        AudioCount = (await audio.ListAsync(ct)).Count;
        BaseUrl = $"{Request.Scheme}://{Request.Host}";
    }
}
