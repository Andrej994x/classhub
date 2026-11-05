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
