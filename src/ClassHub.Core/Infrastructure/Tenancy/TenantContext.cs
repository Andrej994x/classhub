using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Infrastructure.Tenancy;

/// <summary>Scoped per request. Set once by <see cref="WorkspacePermissionHandler"/>.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? WorkspaceId { get; private set; }

    public Guid? UserId { get; private set; }

    public WorkspaceRole? Role { get; private set; }

    /// <summary>Sets the verified workspace. A request can never switch to another workspace.</summary>
    public void Set(Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        if (WorkspaceId is not null && WorkspaceId != workspaceId)
        {
            throw new InvalidOperationException("The request is already bound to another workspace.");
        }

        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
    }
}

/// <summary>A DbContext whose tenant filter and save checks follow a tenant context.</summary>
public interface ITenantAwareDbContext
{
    ITenantContext? TenantContext { get; }

    /// <summary>The current workspace id, or <see cref="Guid.Empty"/> (matches nothing) when none is set.</summary>
    Guid CurrentTenantId { get; }
}
