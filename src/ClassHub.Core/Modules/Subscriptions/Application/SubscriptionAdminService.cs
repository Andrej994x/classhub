using System.Security.Claims;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Subscriptions.Domain;
using ClassHub.Core.Modules.Subscriptions.Persistence;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Subscriptions.Application;

/// <summary>
/// Platform staff manage plans (D-39): admins and the owner activate or extend Pro with a manual payment record
/// (D-30); only the owner changes plan limits. Teachers cannot change their own plan (no workspace endpoint does).
/// </summary>
internal sealed class SubscriptionAdminService(
    ClassHubDbContext db,
    SubscriptionQueries queries,
    PlanCatalog plans,
    IWorkspaceDirectory workspaces,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int HistorySize = 50;

    public async Task<IReadOnlyList<AdminPlanResponse>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var all = await db.Set<Plan>().AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var limits = (await db.Set<PlanLimit>().AsNoTracking().ToListAsync(cancellationToken)).ToLookup(l => l.PlanId);
        return [.. all.Select(p => ToResponse(p, limits[p.Id]))];
    }

    public async Task<AdminPlanResponse> UpdatePlanAsync(string code, UpdatePlanLimitsRequest request, CancellationToken cancellationToken)
    {
        var plan = await db.Set<Plan>().SingleOrDefaultAsync(p => p.Code == code, cancellationToken) ?? throw new NotFoundException();
        if (plan.Version != request.Version)
        {
            throw new ConflictException("The plan was changed meanwhile. Reload and try again.", ErrorCodes.ConcurrencyConflict);
        }

        var rows = await db.Set<PlanLimit>().Where(l => l.PlanId == plan.Id).ToListAsync(cancellationToken);
        var before = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        foreach (var value in request.Limits!)
        {
            var row = rows.SingleOrDefault(r => r.Key == value.Key);
            if (row is null)
            {
                db.Set<PlanLimit>().Add(new PlanLimit { PlanId = plan.Id, Key = value.Key!, Value = value.Value!.Value });
            }
            else
            {
                row.Value = value.Value!.Value;
            }
        }

        var after = request.Limits!.ToDictionary(v => v.Key!, v => v.Value!.Value, StringComparer.Ordinal);
        plan.Version++;
        plan.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        audit.Record("plan.limits.updated", "Plan", plan.Code, before: before, after: after);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The plan was changed meanwhile. Reload and try again.", ErrorCodes.ConcurrencyConflict);
        }

        return (await ListPlansAsync(cancellationToken)).Single(p => p.Id == plan.Id);
    }

    public async Task<AdminSubscriptionResponse> GetAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await workspaces.ExistsAsync(workspaceId, cancellationToken))
        {
            throw new NotFoundException();
        }

        var entitlements = await plans.ForWorkspaceAsync(workspaceId, cancellationToken);
        var codes = await plans.CodesAsync(cancellationToken);
        var history = await queries.HistoryAsync(workspaceId, HistorySize, cancellationToken);
        return new AdminSubscriptionResponse(workspaceId, entitlements.PlanCode, entitlements.PaidUntilUtc, [.. history.Select(e => ToResponse(e, codes))]);
    }

    /// <summary>
    /// Activates Pro for <c>Months</c>, or extends an active Pro period from its end, and records the payment. The
    /// subscription, the history entry and the audit entry commit together; one workspace's changes are serialized.
    /// </summary>
    public async Task<AdminSubscriptionResponse> ActivateProAsync(ClaimsPrincipal actor, Guid workspaceId, ActivateProRequest request, CancellationToken cancellationToken)
    {
        if (!await workspaces.ExistsAsync(workspaceId, cancellationToken))
        {
            throw new NotFoundException();
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (request.PaidOn!.Value > DateOnly.FromDateTime(now).AddDays(1))
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["paidOn"] = ["The payment date cannot be in the future."] });
        }

        var pro = await db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Code == PlanCodes.Pro, cancellationToken);
        var actorId = Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await queries.LockSubscriptionAsync(workspaceId, cancellationToken);
            var subscription = await queries.FindAsync(workspaceId, tracking: true, cancellationToken);
            // A period that ended but was not processed by the expiry job yet is recorded first, so its expiry is
            // never lost from the history when Pro starts again.
            SubscriptionExpiry.RecordIfEnded(db, audit, subscription, await plans.DefaultPlanAsync(cancellationToken), now);
            var (kind, start, end) = SubscriptionPeriods.Add(subscription, pro.Id, request.Months!.Value, now);
            if (subscription is null)
            {
                subscription = new WorkspaceSubscription { Id = Guid.CreateVersion7(), WorkspaceId = workspaceId };
                db.Set<WorkspaceSubscription>().Add(subscription);
            }

            subscription.PlanId = pro.Id;
            subscription.StartsAtUtc = start;
            subscription.EndsAtUtc = end;
            subscription.UpdatedAtUtc = now;
            subscription.Version++;

            db.Set<SubscriptionEvent>().Add(new SubscriptionEvent
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                Kind = kind,
                PlanId = pro.Id,
                PeriodStartUtc = start,
                PeriodEndUtc = end,
                Months = request.Months,
                AmountMinor = request.AmountMinor,
                Currency = request.Currency,
                PaymentMethod = request.PaymentMethod,
                PaymentReference = Trimmed(request.PaymentReference),
                PaidOn = request.PaidOn,
                Note = Trimmed(request.Note),
                ActorUserId = actorId,
                OccurredAtUtc = now,
            });
            audit.Record(
                kind == SubscriptionEventKind.Extended ? "subscription.pro.extended" : "subscription.pro.activated",
                "Workspace",
                workspaceId.ToString(),
                workspaceId,
                after: new { plan = PlanCodes.Pro, endsAtUtc = end, request.Months, request.AmountMinor, request.Currency, request.PaymentMethod });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(workspaceId, cancellationToken);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AdminPlanResponse ToResponse(Plan plan, IEnumerable<PlanLimit> limits)
    {
        var byKey = limits.ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        return new AdminPlanResponse(plan.Id, plan.Code, plan.NameMk, plan.NameEn, plan.IsDefault,
            [.. PlanLimits.All.Where(byKey.ContainsKey).Select(k => new PlanLimitValue(k, byKey[k]))], plan.Version, plan.UpdatedAtUtc);
    }

    internal static SubscriptionEventResponse ToResponse(SubscriptionEvent e, IReadOnlyDictionary<Guid, string> codes) =>
        new(e.Id, e.Kind.ToString(), codes.TryGetValue(e.PlanId, out var code) ? code : string.Empty, e.PeriodStartUtc, e.PeriodEndUtc, e.Months,
            e.AmountMinor, e.Currency, e.PaymentMethod, e.PaymentReference, e.PaidOn, e.Note, e.ActorUserId, e.OccurredAtUtc);
}
