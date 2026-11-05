namespace ClassHub.Core.Modules.Workspaces.Contracts;

/// <summary>Creates the workspace of a newly registered teacher (inside the registration transaction).</summary>
public interface IWorkspaceProvisioning
{
    /// <summary>Adds a workspace with the user as Owner. The caller's <c>SaveChangesAsync</c> commits it.</summary>
    Task<Guid> ProvisionForNewTeacherAsync(Guid userId, string locale, string timeZone, CancellationToken cancellationToken);
}

public sealed record MyWorkspaceResponse(Guid Id, string Name, string Role);

public sealed record WorkspaceResponse(Guid Id, string Name, string TimeZone, string Currency, string Locale, string Role, DateTime CreatedAtUtc);

public sealed record UpdateWorkspaceRequest(string? Name, string? TimeZone, string? Currency, string? Locale);

/// <summary>The workspace entity, for database foreign keys from other modules (as a Type).</summary>
public static class WorkspaceForeignKeyTargets
{
    public static readonly Type Workspace = typeof(Domain.Workspace);
}

/// <summary>
/// Changes the current workspace's time zone and currency inside the caller's unit of work (audited); the caller's
/// <c>SaveChangesAsync</c> commits them together with its own changes. Null values are left unchanged.
/// </summary>
public interface IWorkspaceSettings
{
    Task ApplyAsync(string? timeZone, string? currency, CancellationToken cancellationToken);

    /// <summary>The current workspace's default currency (ISO 4217).</summary>
    Task<string> GetCurrencyAsync(CancellationToken cancellationToken);

    /// <summary>The current workspace's content language (mk/en).</summary>
    Task<string> GetLocaleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Which workspaces may be shown publicly: the owner's membership is active and the owner's account is not
/// suspended. Public sites of other workspaces are hidden (404, not in the sitemap).
/// </summary>
public interface IWorkspacePublicVisibility
{
    Task<IReadOnlySet<Guid>> VisibleAsync(IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken);
}

/// <summary>A teacher's workspace as platform administration sees it (R1-P9): metadata only.</summary>
public sealed record OwnedWorkspace(Guid UserId, Guid WorkspaceId, string Name, DateTime CreatedAtUtc);

/// <summary>Teachers: users who own a workspace. Active: not suspended and signed in since the given time.</summary>
public sealed record TeacherCounts(int Teachers, int ActiveTeachers);

/// <summary>Workspace metadata across all tenants, for the platform admin endpoints only (R1-P9).</summary>
public interface IWorkspaceDirectory
{
    /// <summary>The workspace each of the users owns (users without one are absent).</summary>
    Task<IReadOnlyDictionary<Guid, OwnedWorkspace>> OwnedByAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<TeacherCounts> CountTeachersAsync(DateTime activeSinceUtc, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid workspaceId, CancellationToken cancellationToken);
}

/// <summary>The workspace owner's address for notifications, and whether it is confirmed (D-10b).</summary>
public sealed record WorkspaceOwnerContact(Guid UserId, string Email, string Locale, bool EmailConfirmed);

public interface IWorkspaceOwnerContact
{
    /// <summary>The active owner of the workspace, or null when there is none.</summary>
    Task<WorkspaceOwnerContact?> GetOwnerAsync(Guid workspaceId, CancellationToken cancellationToken);
}
