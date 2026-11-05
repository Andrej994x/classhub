using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Subscriptions.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Subscriptions.Persistence;

/// <summary>
/// Locks and cross-tenant reads of subscriptions (allow-listed tenant-filter bypass): platform staff manage any
/// workspace's plan, the expiry job runs outside any workspace, and limit checks serialize with an application lock.
/// Every query filters by workspace explicitly.
/// </summary>
internal sealed class SubscriptionQueries(ClassHubDbContext db)
{
    /// <summary>Serializes creations against one limit of one workspace until the caller's transaction ends.</summary>
    public Task LockLimitAsync(Guid workspaceId, string limit, CancellationToken cancellationToken)
    {
        var resource = $"classhub:plan-limit:{workspaceId:N}:{limit}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50003, 'Could not acquire the plan limit lock.', 1;",
            cancellationToken);
    }

    /// <summary>Serializes changes of one workspace's subscription (staff activations, the expiry job).</summary>
    public Task LockSubscriptionAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:subscription:{workspaceId:N}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50004, 'Could not acquire the subscription lock.', 1;",
            cancellationToken);
    }

    public Task<WorkspaceSubscription?> FindAsync(Guid workspaceId, bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Set<WorkspaceSubscription>().IgnoreQueryFilters([TenancyFilters.Tenant]).Where(s => s.WorkspaceId == workspaceId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<List<SubscriptionEvent>> HistoryAsync(Guid workspaceId, int take, CancellationToken cancellationToken) =>
        await db.Set<SubscriptionEvent>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId)
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <summary>Workspaces whose paid period has ended but that were not moved back to the default plan yet.</summary>
    public async Task<List<Guid>> EndedAsync(Guid defaultPlanId, DateTime nowUtc, int take, CancellationToken cancellationToken) =>
        await db.Set<WorkspaceSubscription>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .Where(s => s.PlanId != defaultPlanId && s.EndsAtUtc != null && s.EndsAtUtc <= nowUtc)
            .OrderBy(s => s.EndsAtUtc)
            .Select(s => s.WorkspaceId)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountPaidAsync(Guid planId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.Set<WorkspaceSubscription>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .CountAsync(s => s.PlanId == planId && s.EndsAtUtc > nowUtc, cancellationToken);

    public async Task<List<MoneyTotal>> RevenueSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var totals = await db.Set<SubscriptionEvent>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .Where(e => e.OccurredAtUtc >= sinceUtc && e.AmountMinor != null && e.Currency != null)
            .GroupBy(e => e.Currency)
            .Select(g => new { Currency = g.Key, Amount = g.Sum(e => e.AmountMinor) })
            .ToListAsync(cancellationToken);
        return [.. totals.Select(t => new MoneyTotal(t.Currency!, t.Amount ?? 0)).OrderBy(t => t.Currency, StringComparer.Ordinal)];
    }
}
