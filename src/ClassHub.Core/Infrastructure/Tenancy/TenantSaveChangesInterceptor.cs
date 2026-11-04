using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClassHub.Core.Infrastructure.Tenancy;

/// <summary>
/// Guards every write of <see cref="ITenantOwned"/> entities. Inside a workspace-scoped request:
/// new rows get the current WorkspaceId (or must already have it); changes and deletes must target the
/// current workspace. WorkspaceId can never be changed. Outside a workspace (registration provisioning,
/// background jobs) new rows need an explicit WorkspaceId.
/// </summary>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static TenantSaveChangesInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Enforce(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Enforce(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    internal static void Enforce(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var current = (context as ITenantAwareDbContext)?.TenantContext?.WorkspaceId;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            var workspaceProperty = entry.Property(nameof(ITenantOwned.WorkspaceId));
            switch (entry.State)
            {
                case EntityState.Added:
                    if ((Guid)workspaceProperty.CurrentValue! == Guid.Empty)
                    {
                        workspaceProperty.CurrentValue = current
                            ?? throw Violation(entry, "has no WorkspaceId and the request is not workspace-scoped");
                    }
                    else if (current is not null && (Guid)workspaceProperty.CurrentValue! != current)
                    {
                        throw Violation(entry, "belongs to another workspace");
                    }

                    break;

                case EntityState.Modified:
                    if (workspaceProperty.IsModified &&
                        !Equals(workspaceProperty.OriginalValue, workspaceProperty.CurrentValue))
                    {
                        throw Violation(entry, "would move to another workspace");
                    }

                    if (current is not null && (Guid)workspaceProperty.OriginalValue! != current)
                    {
                        throw Violation(entry, "belongs to another workspace");
                    }

                    break;

                case EntityState.Deleted:
                    if (current is not null && (Guid)workspaceProperty.OriginalValue! != current)
                    {
                        throw Violation(entry, "belongs to another workspace");
                    }

                    break;
            }
        }
    }

    private static TenantIsolationException Violation(EntityEntry entry, string reason) =>
        new($"Tenant isolation violation: {entry.Metadata.ClrType.Name} {reason}.");
}

/// <summary>A write would cross workspace boundaries. Always a programming error, never user input.</summary>
public sealed class TenantIsolationException(string message) : InvalidOperationException(message);
