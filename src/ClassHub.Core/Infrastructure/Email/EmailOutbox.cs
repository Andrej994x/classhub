using System.ComponentModel.DataAnnotations;
using ClassHub.Core.Infrastructure.Persistence;

namespace ClassHub.Core.Infrastructure.Email;

/// <summary>A rendered email ready to be queued.</summary>
public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody, string TextBody, string Template);

/// <summary>Queues emails in the outbox. The caller's <c>SaveChangesAsync</c> commits them atomically.</summary>
public interface IEmailOutbox
{
    void Enqueue(EmailMessage message);
}

/// <summary>Delivers one email (dev mailbox now; Resend later).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Email settings (section <c>Email</c>).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary><c>DevMailbox</c> (store locally) or <c>None</c> (queue only, nothing is delivered).</summary>
    [Required, RegularExpression("^(DevMailbox|None)$")]
    public string Provider { get; set; } = EmailProviders.None;

    [Range(50, 600_000)]
    public int DispatchIntervalMilliseconds { get; set; } = 5_000;

    [Range(1, 100)]
    public int BatchSize { get; set; } = 20;

    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 8;
}

public static class EmailProviders
{
    public const string DevMailbox = "DevMailbox";
    public const string None = "None";
}

/// <summary>Links in emails point to the web app (section <c>App</c>).</summary>
public sealed class AppLinkOptions
{
    public const string SectionName = "App";

    /// <summary>Absolute base URL of the web app, for example <c>http://localhost:5173</c>.</summary>
    [Required, Url]
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
}

internal sealed class EmailOutbox(ClassHubDbContext db, TimeProvider clock) : IEmailOutbox
{
    public void Enqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = clock.GetUtcNow().UtcDateTime;
        db.Set<EmailOutboxMessage>().Add(new EmailOutboxMessage
        {
            ToAddress = message.ToAddress,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Template = message.Template,
            CreatedAtUtc = now,
            NextAttemptAtUtc = now,
        });
    }
}

internal sealed class DevMailboxEmailSender(ClassHubDbContext db, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        db.Set<DevMailboxMessage>().Add(new DevMailboxMessage
        {
            ToAddress = message.ToAddress,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Template = message.Template,
            ReceivedAtUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
