using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Subscriptions.Domain;

namespace ClassHub.Core.Modules.Subscriptions.Application;

/// <summary>
/// Records an ended paid period (history event and audit entry, no actor) and moves the subscription back to the
/// default plan. Called under the subscription lock by the expiry job and before staff start a new period, so an
/// expiry is recorded exactly once even when Pro is reactivated before the job runs. The history event has no actor;
/// the audit entry carries the staff member whose action recorded it (none for the job).
/// </summary>
internal static class SubscriptionExpiry
{
    /// <returns>True when the subscription had ended and was moved back; the caller saves.</returns>
    public static bool RecordIfEnded(ClassHubDbContext db, IAuditLog audit, WorkspaceSubscription? subscription, Plan defaultPlan, DateTime nowUtc)
    {
        if (subscription is not { EndsAtUtc: { } end } || end > nowUtc || subscription.PlanId == defaultPlan.Id)
        {
            return false;
        }

        db.Set<SubscriptionEvent>().Add(new SubscriptionEvent
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = subscription.WorkspaceId,
            Kind = SubscriptionEventKind.Expired,
            PlanId = defaultPlan.Id,
            PeriodStartUtc = end,
            // When the period actually ended (the job or a reactivation may record it later), which also keeps it
            // before a reactivation recorded in the same transaction.
            OccurredAtUtc = end,
        });
        audit.Record("subscription.expired", "Workspace", subscription.WorkspaceId.ToString(), subscription.WorkspaceId,
            before: new { endsAtUtc = end }, after: new { plan = defaultPlan.Code });
        subscription.PlanId = defaultPlan.Id;
        subscription.StartsAtUtc = end;
        subscription.EndsAtUtc = null;
        subscription.UpdatedAtUtc = nowUtc;
        subscription.Version++;
        return true;
    }
}
