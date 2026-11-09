namespace ClassHub.Core.SharedKernel.Tenancy;

/// <summary>
/// Marks an entity that belongs to exactly one workspace (tenant). Such entities are filtered by the
/// named query filter <see cref="TenancyFilters.Tenant"/>, stamped and checked on save by the tenant
/// interceptor, and reference other tenant entities only through composite (WorkspaceId, Id) foreign keys.
/// </summary>
public interface ITenantOwned
{
    Guid WorkspaceId { get; }
}

/// <summary>Marks an entity that can be archived instead of deleted (history is kept).</summary>
public interface IArchivable
{
    DateTime? ArchivedAtUtc { get; }
}

/// <summary>
/// Marks an append-only record (the finance ledger, R2-P10): rows are only ever added; a correction is a new row. The
/// save interceptor refuses to update or delete one.
/// </summary>
public interface IAppendOnly;

/// <summary>Names of the EF Core named query filters.</summary>
public static class TenancyFilters
{
    /// <summary>Rows of the current workspace only; nothing at all when no workspace is set (fail closed).</summary>
    public const string Tenant = "Tenant";

    /// <summary>Hides archived rows; switched off explicitly where history is shown.</summary>
    public const string Archived = "Archived";
}

/// <summary>Role of a user inside a workspace.</summary>
public enum WorkspaceRole
{
    /// <summary>The teacher who owns the workspace.</summary>
    Owner = 0,
}

/// <summary>
/// The workspace of the current request. It is set only by the workspace authorization handler after the
/// user's active membership was verified; never from client-supplied headers or bodies.
/// </summary>
public interface ITenantContext
{
    Guid? WorkspaceId { get; }

    Guid? UserId { get; }

    WorkspaceRole? Role { get; }

    /// <summary>
    /// Set for student portal requests (<c>student:self</c>): the signed-in user's own student record in this
    /// workspace. Such requests have no workspace role.
    /// </summary>
    Guid? StudentId => null;

    /// <summary>
    /// True for anonymous public reads (public site pages): the request sees one workspace's rows through the
    /// normal tenant filters, but may not write any tenant-owned data.
    /// </summary>
    bool IsReadOnly => false;

    bool HasWorkspace => WorkspaceId is not null;

    /// <summary>The current workspace; throws when the request is not workspace-scoped.</summary>
    Guid RequireWorkspaceId() =>
        WorkspaceId ?? throw new InvalidOperationException("This operation requires a workspace-scoped request.");
}
