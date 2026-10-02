using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using IELTop_Content_Server.Options;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

public sealed record EmailCheckResult(bool Valid, string Error, string Reason = "");

public interface IEmailCheckService
{
    Task<EmailCheckResult> ValidateAsync(string email, CancellationToken ct = default);
    Task<EmailCheckResult> CheckSmtpDeliverabilityAsync(string email, CancellationToken ct = default);
}

/// <summary>
/// Validates email deliverability to reject disposable burner emails,
/// non-existent domains, bot spam, and invalid SMTP addresses.
/// </summary>
public sealed class EmailCheckService(
    IOptions<SmtpOptions> smtpOptions,
    ILogger<EmailCheckService> logger) : IEmailCheckService
{
    private readonly SmtpOptions _smtp = smtpOptions.Value;

    // Common disposable, temporary burner email providers favored by automated botnets
    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "tempmail.com", "temp-mail.org", "tempmail.net", "tempmailaddress.com",
        "mailinator.com", "mailinator.net", "mailinator2.com", "mailinater.com",
        "10minutemail.com", "10minutemail.net", "10minutemail.org", "10minmail.com",
        "guerrillamail.com", "guerrillamailblock.com", "guerrillamail.net", "guerrillamail.biz", "guerrillamail.org",
        "yopmail.com", "yopmail.net", "yopmail.fr", "yopmail.org",
        "sharklasers.com", "grr.la", "pokemail.net", "spam4.me",
        "trashmail.com", "trashmail.net", "trashmail.me", "trashmail.org",
        "dispostable.com", "getairmail.com", "throwawaymail.com", "fakeinbox.com",
        "maildrop.cc", "inboxbear.com", "mohmal.com", "nada.ltd",
        "generator.email", "crazymailing.com", "armyspy.com", "cuvox.de", "dayrep.com",
        "fleckens.hu", "gustr.com", "jourrapide.com", "rhyta.com", "superrito.com",
        "teleworm.us", "einrot.com", "klzlv.com", "emailfake.com", "mytemp.email",
        "burnermail.io", "burner.email", "dropmail.me", "10mail.org", "minutemail.com",
        "tmpmail.org", "tempail.com", "emailondeck.com", "fakemailgenerator.com",
        "throwaway.email", "mytrashmail.com", "mailcatch.com", "discardmail.com",
        "trashmail.io", "mailna.co", "mailna.in", "mailna.me", "temp-mail.io",
        "internxt.com", "tempmail.ninja", "inboxkitten.com", "meltmail.com"
    };

    public async Task<EmailCheckResult> ValidateAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new EmailCheckResult(false, "Email address is required.", "empty");

        string clean = email.Trim().ToLowerInvariant();
        if (clean.Length < 5 || clean.Length > 254)
            return new EmailCheckResult(false, "Email address length is invalid.", "length");

        int at = clean.IndexOf('@');
        if (at <= 0 || at == clean.Length - 1 || clean.IndexOf('@', at + 1) != -1)
            return new EmailCheckResult(false, "Invalid email address format.", "syntax");

        string localPart = clean[..at];
        string domain = clean[(at + 1)..];

        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
            return new EmailCheckResult(false, "The email domain is invalid.", "domain_syntax");

        // Reject control chars and spaces
        if (clean.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return new EmailCheckResult(false, "Email cannot contain spaces or control characters.", "illegal_chars");

        // Disposable email blacklist check
        if (_smtp.CheckDisposableEmail && DisposableDomains.Contains(domain))
        {
            logger.LogInformation("Rejected registration with disposable email domain: {Domain}", domain);
            return new EmailCheckResult(false, "Disposable or temporary email addresses are not permitted.", "disposable");
        }

        // Domain DNS / MX presence check
        if (_smtp.CheckDnsMx)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                var entry = await Dns.GetHostEntryAsync(domain, timeoutCts.Token);
                if (entry.AddressList.Length == 0)
                {
                    return new EmailCheckResult(false, "The email domain does not have active DNS records.", "no_dns");
                }
            }
            catch (SocketException ex)
            {
                logger.LogInformation("DNS lookup failed for email domain {Domain}: {Error}", domain, ex.SocketErrorCode);
                return new EmailCheckResult(false, "The email domain does not exist or cannot receive mail.", "dns_failed");
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // In timeout or offline scenarios, fail open on DNS to prevent blocking offline/local dev
                logger.LogWarning("DNS lookup timed out for email domain {Domain}", domain);
            }
        }

        // Optional SMTP deliverability handshake verification
        if (_smtp.CheckSmtpHandshake)
        {
            var smtpResult = await CheckSmtpDeliverabilityAsync(clean, ct);
            if (!smtpResult.Valid)
                return smtpResult;
        }

        return new EmailCheckResult(true, string.Empty);
    }

    public async Task<EmailCheckResult> CheckSmtpDeliverabilityAsync(string email, CancellationToken ct = default)
    {
        string domain = email[(email.IndexOf('@') + 1)..];

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));

            var entry = await Dns.GetHostEntryAsync(domain, timeoutCts.Token);
            if (entry.AddressList.Length == 0)
                return new EmailCheckResult(false, "No mail servers found for this domain.", "no_host");

            IPAddress targetIp = entry.AddressList[0];

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(targetIp, 25, timeoutCts.Token);
            using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            string? banner = await reader.ReadLineAsync(timeoutCts.Token);
            if (banner == null || !banner.StartsWith("220"))
                return new EmailCheckResult(true, string.Empty); // Server didn't banner, fallback to valid

            await writer.WriteLineAsync("HELO ieltop.local");
            await reader.ReadLineAsync(timeoutCts.Token);

            await writer.WriteLineAsync("MAIL FROM:<noreply@ieltop.org>");
            await reader.ReadLineAsync(timeoutCts.Token);

            await writer.WriteLineAsync($"RCPT TO:<{email}>");
            string? rcptResponse = await reader.ReadLineAsync(timeoutCts.Token);

            await writer.WriteLineAsync("QUIT");

            if (rcptResponse != null && (rcptResponse.StartsWith("550") || rcptResponse.StartsWith("551") || rcptResponse.StartsWith("553")))
            {
                logger.LogInformation("SMTP server rejected recipient {Email}: {Response}", email, rcptResponse);
                return new EmailCheckResult(false, "The recipient mailbox was rejected by the mail server.", "smtp_rejected");
            }

            return new EmailCheckResult(true, string.Empty);
        }
        catch (Exception ex)
        {
            // Port 25 is often blocked by cloud firewalls or residential ISPs.
            // Gracefully pass so legitimate users are never blocked due to local network egress policies.
            logger.LogDebug(ex, "SMTP deliverability probe skipped for {Domain}", domain);
            return new EmailCheckResult(true, string.Empty);
        }
    }
}

