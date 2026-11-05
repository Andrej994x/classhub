namespace ClassHub.Core.Modules.Admin.Contracts;

/// <summary>
/// Platform numbers (R1-P9). Teachers own a workspace; active teachers are not suspended and signed in during the last
/// 30 days; new users registered during the last 7 or 30 days. Aggregates only.
/// </summary>
public sealed record AdminOverviewResponse(
    int Users,
    int SuspendedUsers,
    int Teachers,
    int ActiveTeachers,
    int NewUsersLast7Days,
    int NewUsersLast30Days,
    int PublishedSites,
    int PlatformStaff);

/// <summary>The account's workspace and site as metadata (never their content).</summary>
public sealed record AdminWorkspaceSummary(Guid Id, string Name, DateTime CreatedAtUtc, string? SiteSlug, bool SitePublished);

/// <summary>An account as platform administration sees it: metadata only (no profile, students, leads or notes).</summary>
public sealed record AdminUserSummary(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    string Status,
    IReadOnlyList<string> PlatformRoles,
    bool MfaEnabled,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    AdminWorkspaceSummary? Workspace);

public sealed record AdminUserPage(IReadOnlyList<AdminUserSummary> Items, int Total, int Page, int PageSize);

/// <summary>An account with its lockout state and the audit entries about it (newest first).</summary>
public sealed record AdminUserDetail(AdminUserSummary User, DateTime? LockedOutUntilUtc, IReadOnlyList<AdminAuditEntry> History);

/// <summary>Suspension or reactivation: the reason is required and recorded in the audit log.</summary>
public sealed record AdminStatusRequest(string? Reason);

/// <summary>Grant or revoke Platform Admin (owner only): confirmed with a fresh authenticator code (step-up).</summary>
public sealed record AdminRoleRequest(string? Code);

/// <summary>
/// One audit entry. <c>DetailsJson</c> (before/after values) is shown only for platform-level entries; entries of a
/// workspace show what happened and to which record, not the workspace's data.
/// </summary>
public sealed record AdminAuditEntry(
    Guid Id,
    DateTime OccurredAtUtc,
    Guid? ActorUserId,
    string? ActorEmail,
    string Action,
    string TargetType,
    string TargetId,
    Guid? WorkspaceId,
    string? DetailsJson);

public sealed record AdminAuditPage(IReadOnlyList<AdminAuditEntry> Items, int Total, int Page, int PageSize);
