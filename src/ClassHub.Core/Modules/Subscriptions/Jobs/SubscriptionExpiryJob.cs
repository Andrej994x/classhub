using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Subscriptions.Application;
using ClassHub.Core.Modules.Subscriptions.Domain;
using ClassHub.Core.Modules.Subscriptions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Subscriptions.Jobs;

/// <summary>Subscription settings (section <c>Subscriptions</c>).</summary>
public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    /// <summary>How often ended paid periods are moved back to the default plan.</summary>
    public int ExpiryCheckSeconds { get; set; } = 300;
}

/// <summary>
/// Moves workspaces whose paid period ended back to the default plan and records it (history and audit). The limits
/// already follow the end date on every request; the job only makes the state and the history explicit. It never
/// deletes or hides data (D-29b): over-limit data stays usable, only new creations are blocked.
/// </summary>
internal sealed partial class SubscriptionExpiryJob(IServiceScopeFactory scopes, IOptions<SubscriptionOptions> options, TimeProvider clock, ILogger<SubscriptionExpiryJob> logger)
    : BackgroundService
{
    public const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.ExpiryCheckSeconds));
        using var timer = new PeriodicTimer(interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    /// <summary>One pass; returns how many workspaces were moved back to the default plan.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ClassHubDbContext>();
        var queries = services.GetRequiredService<SubscriptionQueries>();
        var audit = services.GetRequiredService<IAuditLog>();
        var defaultPlan = await services.GetRequiredService<PlanCatalog>().DefaultPlanAsync(cancellationToken);

        var moved = 0;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var workspaceId in await queries.EndedAsync(defaultPlan.Id, now, BatchSize, cancellationToken))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await queries.LockSubscriptionAsync(workspaceId, cancellationToken);
            var subscription = await queries.FindAsync(workspaceId, tracking: true, cancellationToken);
            // Re-checked under the lock: staff may have extended the period meanwhile.
            if (subscription is not { EndsAtUtc: { } end } || end > now || subscription.PlanId == defaultPlan.Id)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                continue;
            }

            db.Set<SubscriptionEvent>().Add(new SubscriptionEvent
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                Kind = SubscriptionEventKind.Expired,
                PlanId = defaultPlan.Id,
                PeriodStartUtc = end,
                OccurredAtUtc = now,
            });
            audit.Record("subscription.expired", "Workspace", workspaceId.ToString(), workspaceId, before: new { endsAtUtc = end }, after: new { plan = defaultPlan.Code });
            subscription.PlanId = defaultPlan.Id;
            subscription.StartsAtUtc = end;
            subscription.EndsAtUtc = null;
            subscription.UpdatedAtUtc = now;
            subscription.Version++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            moved++;
        }

        if (moved > 0)
        {
            LogExpired(logger, moved);
        }

        return moved;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} subscriptions ended and moved back to the default plan")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The subscription expiry job failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
