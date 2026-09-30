using System.Net;
using System.Net.Mail;
using IELTop_Content_Server.Data;
using IELTop_Content_Server.Models;
using IELTop_Content_Server.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IELTop_Content_Server.Services;

/// <summary>
/// Queues notification email and a background worker delivers it. A
/// request never waits on SMTP, and a down mail host only delays mail.
/// </summary>
public interface INotificationService
{
    Task QueueAsync(string to, string subject, string body, CancellationToken ct = default);
    Task<int> PendingCountAsync(CancellationToken ct = default);
}

public sealed class NotificationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task QueueAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            return;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.Notifications.Add(new Notification
            {
                ToAddress = Clamp(to.Trim(), 256),
                Subject = Clamp(subject, 256),
                Body = body,
                Status = NotificationStatus.Pending
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not queue a notification for {To}", to);
        }
    }

    public async Task<int> PendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.CountAsync(n => n.Status == NotificationStatus.Pending, ct);
    }

    private static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

/// <summary>
/// Delivers queued notifications. Runs on a timer, retries a few times,
/// then gives up with the reason kept on the row.
/// </summary>
public sealed class NotificationWorker(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<SmtpOptions> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private const int MaxAttempts = 5;

    private readonly SmtpOptions _smtp = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so startup and seeding finish first.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Notification worker iteration failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task DeliverPendingAsync(CancellationToken ct)
    {
        List<Notification> batch;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            batch = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Pending && n.Attempts < MaxAttempts)
                .OrderBy(n => n.Id)
                .Take(20)
                .ToListAsync(ct);
        }

        if (batch.Count == 0)
            return;

        if (!_smtp.Enabled || string.IsNullOrWhiteSpace(_smtp.Host))
        {
            // Keep the rows. When SMTP is configured later they go out.
            logger.LogDebug("SMTP is disabled, {Count} notification(s) waiting.", batch.Count);
            return;
        }

        foreach (var item in batch)
        {
            if (ct.IsCancellationRequested)
                return;

            try
            {
                await SendAsync(item, ct);
                item.Status = NotificationStatus.Sent;
                item.SentAt = DateTimeOffset.UtcNow;
                item.LastError = string.Empty;
            }
            catch (Exception e)
            {
                item.Attempts++;
                item.LastError = e.Message.Length > 400 ? e.Message[..400] : e.Message;
                if (item.Attempts >= MaxAttempts)
                    item.Status = NotificationStatus.Failed;
                logger.LogWarning(e, "Notification {Id} to {To} failed.", item.Id, item.ToAddress);
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.Notifications.Update(item);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SendAsync(Notification item, CancellationToken ct)
    {
        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000
        };

        if (!string.IsNullOrWhiteSpace(_smtp.Username))
            client.Credentials = new NetworkCredential(_smtp.Username, _smtp.Password);

        using var message = new MailMessage
        {
            From = new MailAddress(FromAddress(), _smtp.FromName),
            Subject = item.Subject,
            Body = item.Body,
            IsBodyHtml = false
        };
        message.To.Add(item.ToAddress);

        await client.SendMailAsync(message, ct);
    }

    private string FromAddress() =>
        string.IsNullOrWhiteSpace(_smtp.FromAddress) ? "no-reply@example.com" : _smtp.FromAddress;
}
