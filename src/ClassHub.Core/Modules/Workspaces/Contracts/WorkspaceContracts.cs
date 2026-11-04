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
