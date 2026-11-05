using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Subscriptions.Domain;

/// <summary>
/// A SaaS plan (M14): platform reference data, not tenant-owned. Free is the default every workspace has; Pro is
/// activated manually by platform staff (no automatic billing in the MVP). Academy comes later.
/// </summary>
public sealed class Plan
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 60;

    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string NameMk { get; set; }

    public required string NameEn { get; set; }

    /// <summary>The plan of every workspace without an active paid subscription (exactly one plan).</summary>
    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>One limit of a plan (D-29: provisional values, editable only by the Platform Owner).</summary>
public sealed class PlanLimit
{
    public const int KeyMaxLength = 40;
    public const int MaxValue = 1_000_000;

    public Guid PlanId { get; set; }

    public required string Key { get; set; }

    public int Value { get; set; }
}

/// <summary>
/// A workspace's paid plan, if it ever had one: a workspace without a row, or whose period ended, is on the default
/// plan (Free). Expiry never deletes data (D-29b): it only changes which limits apply to new creations.
/// </summary>
public sealed class WorkspaceSubscription : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid PlanId { get; set; }

    public DateTime StartsAtUtc { get; set; }

    /// <summary>End of the paid period; null on the default plan.</summary>
    public DateTime? EndsAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

public enum SubscriptionEventKind
{
    /// <summary>A paid period started (new, or after the previous one ended).</summary>
    Activated = 1,

    /// <summary>An active paid period was made longer.</summary>
    Extended = 2,

    /// <summary>The paid period ended; the workspace is back on the default plan.</summary>
    Expired = 3,
}

/// <summary>
/// Append-only history of a workspace's plan, with the manual payment record of each activation or extension
/// (D-30). Amounts are minor units of the currency.
/// </summary>
public sealed class SubscriptionEvent : ITenantOwned
{
    public const int MethodMaxLength = 20;
    public const int ReferenceMaxLength = 100;
    public const int NoteMaxLength = 500;
    public const long MaxAmountMinor = 100_000_000_000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public SubscriptionEventKind Kind { get; set; }

    public Guid PlanId { get; set; }

    public DateTime PeriodStartUtc { get; set; }

    public DateTime? PeriodEndUtc { get; set; }

    public int? Months { get; set; }

    public long? AmountMinor { get; set; }

    public string? Currency { get; set; }

    public string? PaymentMethod { get; set; }

    public string? PaymentReference { get; set; }

    public DateOnly? PaidOn { get; set; }

    public string? Note { get; set; }

    /// <summary>The staff member, or null for the expiry job.</summary>
    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Manual payment methods for a Pro activation (no payment provider in the MVP).</summary>
public static class PaymentMethods
{
    public const string BankTransfer = "bank-transfer";
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Complimentary = "complimentary";
    public const string Other = "other";

    public static IReadOnlyList<string> All { get; } = [BankTransfer, Cash, Card, Complimentary, Other];
}

/// <summary>The period rules, free of persistence so they can be unit-tested.</summary>
public static class SubscriptionPeriods
{
    /// <summary>The plan that applies at <paramref name="nowUtc"/>: the paid plan inside its period, otherwise the default.</summary>
    public static Guid EffectivePlan(WorkspaceSubscription? subscription, Guid defaultPlanId, DateTime nowUtc) =>
        subscription is { EndsAtUtc: { } end } && end > nowUtc ? subscription.PlanId : defaultPlanId;

    /// <summary>
    /// A paid period of <paramref name="months"/> for <paramref name="planId"/>: an active period of the same plan is
    /// extended from its end (no paid days lost); otherwise a new period starts now.
    /// </summary>
    public static (SubscriptionEventKind Kind, DateTime Start, DateTime End) Add(WorkspaceSubscription? current, Guid planId, int months, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        if (current is { EndsAtUtc: { } end } && end > nowUtc && current.PlanId == planId)
        {
            return (SubscriptionEventKind.Extended, current.StartsAtUtc, end.AddMonths(months));
        }

        return (SubscriptionEventKind.Activated, nowUtc, nowUtc.AddMonths(months));
    }
}
