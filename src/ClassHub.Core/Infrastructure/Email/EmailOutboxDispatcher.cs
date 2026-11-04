using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Infrastructure.Email;

/// <summary>
/// Delivers queued emails. Several instances may run: rows are claimed with
/// <c>UPDATE TOP (n) … WITH (UPDLOCK, READPAST, ROWLOCK)</c> and a lease, so a message is delivered by
/// one worker at a time. Failed deliveries are retried with backoff up to <see cref="EmailOptions.MaxAttempts"/>.
/// </summary>
internal sealed partial class EmailOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<EmailOutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(options.Value.DispatchIntervalMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) > 0)
                {
                    // Keep draining while full batches are found.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogDispatchFailed(logger, ex);
            }

            try
            {
                await Task.Delay(interval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Claims and delivers one batch; returns the number of claimed messages.</summary>
    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var settings = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var leaseUntil = now.Add(Lease);

        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE TOP ({settings.BatchSize}) o
            SET o.LockedUntilUtc = {leaseUntil}, o.Attempts = o.Attempts + 1
            OUTPUT inserted.Id AS [Value]
            FROM EmailOutbox AS o WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE o.SentAtUtc IS NULL
              AND o.NextAttemptAtUtc <= {now}
              AND (o.LockedUntilUtc IS NULL OR o.LockedUntilUtc < {now})
              AND o.Attempts < {settings.MaxAttempts}
            """).ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return 0;
        }

        var messages = await db.Set<EmailOutboxMessage>()
            .Where(m => claimed.Contains(m.Id))
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await sender.SendAsync(
                    new EmailMessage(message.ToAddress, message.Subject, message.HtmlBody, message.TextBody, message.Template),
                    cancellationToken);
                message.SentAtUtc = clock.GetUtcNow().UtcDateTime;
                message.LockedUntilUtc = null;
                message.LastError = null;
                // Do not keep tokens or personal data at rest once delivered.
                message.HtmlBody = string.Empty;
                message.TextBody = string.Empty;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                message.LockedUntilUtc = null;
                message.LastError = ex.GetType().Name;
                message.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime.Add(Backoff(message.Attempts));
                LogDeliveryFailed(logger, message.Id, message.Attempts, ex.GetType().Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return claimed.Count;
    }

    internal static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(3600, 15 * Math.Pow(2, Math.Max(0, attempts - 1))));

    [LoggerMessage(Level = LogLevel.Error, Message = "Email outbox dispatch failed")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {MessageId} delivery attempt {Attempt} failed: {ErrorType}")]
    private static partial void LogDeliveryFailed(ILogger logger, Guid messageId, int attempt, string errorType);
}
