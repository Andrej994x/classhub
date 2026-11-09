namespace ClassHub.Core.Modules.Subscriptions.Contracts;

/// <summary>
/// The limit keys of every plan (D-29). Enforced where the feature exists: published tests now; active students
/// (R2-P1), activities and storage (with their modules) later. Analytics level: 1 basic (the dashboard), 2 advanced.
/// </summary>
public static class PlanLimits
{
    public const string ActiveStudents = "active-students";
    public const string PublishedTests = "published-tests";
    public const string Activities = "activities";
    public const string StorageMb = "storage-mb";
    public const string AnalyticsLevel = "analytics-level";

    public static IReadOnlyList<string> All { get; } = [ActiveStudents, PublishedTests, Activities, StorageMb, AnalyticsLevel];
}

public static class PlanCodes
{
    public const string Free = "free";
    public const string Pro = "pro";
}

/// <summary>The plan that applies to the current workspace now, with its limits.</summary>
public sealed record WorkspaceEntitlements(string PlanCode, string PlanNameMk, string PlanNameEn, DateTime? PaidUntilUtc, IReadOnlyDictionary<string, int> Limits);

/// <summary>Plan limits for the current workspace (bound by the workspace authorization).</summary>
public interface IEntitlementService
{
    Task<WorkspaceEntitlements> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inside the caller's transaction: serializes every creation against <paramref name="limit"/> in this workspace
    /// (an application lock held until the commit), counts what is in use with <paramref name="countInUse"/> and throws
    /// 422 <c>PLAN_LIMIT_REACHED</c> when <paramref name="adding"/> more would exceed the plan. Parallel creations near
    /// the limit therefore cannot exceed it. Over the limit after a downgrade, existing data stays usable (D-29b).
    /// </summary>
    Task EnsureWithinLimitAsync(string limit, Func<CancellationToken, Task<int>> countInUse, int adding, CancellationToken cancellationToken);
}

/// <summary>
/// The plan's storage (<see cref="PlanLimits.StorageMb"/>) for the current workspace: every stored file counts, whoever
/// writes it (materials, profile photo, …). Every writer of stored files checks here, inside the transaction that adds
/// the file, so all of them are serialized by the same plan-limit lock.
/// </summary>
public interface IStorageQuota
{
    /// <summary>
    /// Inside the caller's transaction: throws 422 <c>PLAN_LIMIT_REACHED</c> when the workspace's stored bytes plus
    /// <paramref name="addingBytes"/>, minus what <paramref name="freeingBytes"/> says the same transaction removes,
    /// exceed the plan (whole megabytes, rounded up). <paramref name="freeingBytes"/> runs under the lock, so a file
    /// replaced by a parallel writer is never counted as freed twice.
    /// </summary>
    Task EnsureRoomAsync(long addingBytes, Func<CancellationToken, Task<long>>? freeingBytes, CancellationToken cancellationToken);

    /// <summary>The bytes the current workspace stores.</summary>
    Task<long> UsedBytesAsync(CancellationToken cancellationToken);
}

/// <summary>Plan numbers for the platform overview.</summary>
public sealed record MoneyTotal(string Currency, long AmountMinor);

public sealed record SubscriptionCounts(int ProWorkspaces, IReadOnlyList<MoneyTotal> RevenueInPeriod);

public interface ISubscriptionStats
{
    /// <summary>Workspaces on Pro now, and the recorded payments since <paramref name="sinceUtc"/> per currency.</summary>
    Task<SubscriptionCounts> CountAsync(DateTime sinceUtc, CancellationToken cancellationToken);
}

// ---- Teacher API (GET /api/w/{id}/plan) ----

public sealed record PlanLimitUsage(string Key, int Limit, int? Used);

/// <summary>The workspace's plan: what it allows and what is in use (null where the feature does not exist yet).</summary>
public sealed record TeacherPlanResponse(string Code, string NameMk, string NameEn, DateTime? PaidUntilUtc, IReadOnlyList<PlanLimitUsage> Limits);

// ---- Admin API ----

public sealed record PlanLimitValue(string? Key, int? Value);

public sealed record AdminPlanResponse(Guid Id, string Code, string NameMk, string NameEn, bool IsDefault, IReadOnlyList<PlanLimitValue> Limits, int Version, DateTime UpdatedAtUtc);

/// <summary>New values for every limit of a plan (Platform Owner only), with the version that was loaded.</summary>
public sealed record UpdatePlanLimitsRequest(IReadOnlyList<PlanLimitValue>? Limits, int? Version);

/// <summary>Activate or extend Pro for a number of months, with the manual payment record (D-30).</summary>
public sealed record ActivateProRequest(int? Months, long? AmountMinor, string? Currency, string? PaymentMethod, string? PaymentReference, DateOnly? PaidOn, string? Note);

public sealed record SubscriptionEventResponse(
    Guid Id,
    string Kind,
    string PlanCode,
    DateTime PeriodStartUtc,
    DateTime? PeriodEndUtc,
    int? Months,
    long? AmountMinor,
    string? Currency,
    string? PaymentMethod,
    string? PaymentReference,
    DateOnly? PaidOn,
    string? Note,
    Guid? ActorUserId,
    DateTime OccurredAtUtc);

/// <summary>A workspace's plan now and its history (newest first), for platform staff.</summary>
public sealed record AdminSubscriptionResponse(Guid WorkspaceId, string PlanCode, DateTime? PaidUntilUtc, IReadOnlyList<SubscriptionEventResponse> History);
