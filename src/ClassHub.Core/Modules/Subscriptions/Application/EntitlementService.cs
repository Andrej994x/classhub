using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Subscriptions.Domain;
using ClassHub.Core.Modules.Subscriptions.Persistence;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Subscriptions.Application;

/// <summary>Which plan applies to a workspace now, and the limits of the plans (read fresh: the owner may change them).</summary>
internal sealed class PlanCatalog(ClassHubDbContext db, SubscriptionQueries queries, TimeProvider clock)
{
    public Task<Plan> DefaultPlanAsync(CancellationToken cancellationToken) =>
        db.Set<Plan>().AsNoTracking().SingleAsync(p => p.IsDefault, cancellationToken);

    public async Task<Dictionary<Guid, string>> CodesAsync(CancellationToken cancellationToken) =>
        await db.Set<Plan>().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);

    public async Task<WorkspaceEntitlements> ForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var subscription = await queries.FindAsync(workspaceId, tracking: false, cancellationToken);
        var defaultPlan = await DefaultPlanAsync(cancellationToken);
        var planId = SubscriptionPeriods.EffectivePlan(subscription, defaultPlan.Id, now);
        var plan = planId == defaultPlan.Id ? defaultPlan : await db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == planId, cancellationToken);
        var limits = await db.Set<PlanLimit>().AsNoTracking().Where(l => l.PlanId == planId).ToDictionaryAsync(l => l.Key, l => l.Value, StringComparer.Ordinal, cancellationToken);
        return new WorkspaceEntitlements(plan.Code, plan.NameMk, plan.NameEn, planId == defaultPlan.Id ? null : subscription!.EndsAtUtc, limits);
    }
}

internal sealed class EntitlementService(ClassHubDbContext db, ITenantContext tenant, SubscriptionQueries queries, PlanCatalog plans, ITestStats tests, TimeProvider clock)
    : IEntitlementService, ISubscriptionStats
{
    public Task<WorkspaceEntitlements> GetCurrentAsync(CancellationToken cancellationToken) =>
        plans.ForWorkspaceAsync(CurrentWorkspace(), cancellationToken);

    public async Task EnsureWithinLimitAsync(string limit, Func<CancellationToken, Task<int>> countInUse, int adding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(countInUse);
        if (db.Database.CurrentTransaction is null)
        {
            // Without the caller's transaction the lock would end before the new row is saved.
            throw new InvalidOperationException("Plan limits are checked inside the transaction that creates the data.");
        }

        var workspaceId = CurrentWorkspace();
        await queries.LockLimitAsync(workspaceId, limit, cancellationToken);
        var entitlements = await plans.ForWorkspaceAsync(workspaceId, cancellationToken);
        if (!entitlements.Limits.TryGetValue(limit, out var allowed))
        {
            throw new InvalidOperationException($"The plan '{entitlements.PlanCode}' has no limit '{limit}'.");
        }

        var used = await countInUse(cancellationToken);
        if (used + adding > allowed)
        {
            throw new BusinessRuleException($"Your plan allows {allowed} ({limit}). Upgrade to add more.", ErrorCodes.PlanLimitReached);
        }
    }

    /// <summary>The teacher's plan page: limits, and usage where the feature exists.</summary>
    public async Task<TeacherPlanResponse> GetTeacherPlanAsync(CancellationToken cancellationToken)
    {
        var entitlements = await GetCurrentAsync(cancellationToken);
        var published = (await tests.GetAsync(clock.GetUtcNow().UtcDateTime, cancellationToken)).Published;
        int? Used(string key) => key == PlanLimits.PublishedTests ? published : null;
        return new TeacherPlanResponse(
            entitlements.PlanCode,
            entitlements.PlanNameMk,
            entitlements.PlanNameEn,
            entitlements.PaidUntilUtc,
            [.. PlanLimits.All.Where(entitlements.Limits.ContainsKey).Select(k => new PlanLimitUsage(k, entitlements.Limits[k], Used(k)))]);
    }

    public async Task<SubscriptionCounts> CountAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pro = await db.Set<Plan>().AsNoTracking().Where(p => p.Code == PlanCodes.Pro).Select(p => p.Id).SingleAsync(cancellationToken);
        return new SubscriptionCounts(await queries.CountPaidAsync(pro, now, cancellationToken), await queries.RevenueSinceAsync(sinceUtc, cancellationToken));
    }

    private Guid CurrentWorkspace() =>
        tenant.WorkspaceId ?? throw new InvalidOperationException("Plan limits apply to a workspace request.");
}
