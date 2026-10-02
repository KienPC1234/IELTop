using IELTop_Content_Server.Models;
using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IELTop_Content_Server.Pages.Security;

[Authorize(Policy = "Admin")]
public sealed class IndexModel(
    IIpAbuseGuard abuseGuard,
    ILogger<IndexModel> logger) : PageModel
{
    public List<BlockedIp> BlockedIps { get; private set; } = [];

    [BindProperty]
    public string IpAddress { get; set; } = string.Empty;

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    [BindProperty]
    public int DurationHours { get; set; } = 24;

    public async Task OnGetAsync(CancellationToken ct)
    {
        BlockedIps = await abuseGuard.ListBlockedIpsAsync(ct);
    }

    public async Task<IActionResult> OnPostBlockAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            TempData["Error"] = "IP address is required.";
            return RedirectToPage();
        }

        string cleanIp = IpAddress.Trim();
        if (IpAbuseGuard.IsInternalOrLoopback(cleanIp))
        {
            TempData["Error"] = "Cannot block internal or loopback IP addresses.";
            return RedirectToPage();
        }

        TimeSpan? duration = DurationHours > 0 ? TimeSpan.FromHours(DurationHours) : null;
        string adminName = User.Identity?.Name ?? "Admin";

        await abuseGuard.BlockIpAsync(
            cleanIp,
            string.IsNullOrWhiteSpace(Reason) ? "Manual administrative ban" : Reason.Trim(),
            duration,
            adminName,
            ct);

        logger.LogInformation("Admin {Admin} manually restricted IP {Ip} for {Hours} hours", adminName, cleanIp, DurationHours);
        TempData["Message"] = $"IP {cleanIp} has been restricted successfully.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnblockAsync(string ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            TempData["Error"] = "IP address is required.";
            return RedirectToPage();
        }

        string adminName = User.Identity?.Name ?? "Admin";
        await abuseGuard.UnblockIpAsync(ip.Trim(), adminName, ct);

        logger.LogInformation("Admin {Admin} manually unblocked IP {Ip}", adminName, ip.Trim());
        TempData["Message"] = $"IP {ip.Trim()} has been unblocked successfully.";
        return RedirectToPage();
    }
}
