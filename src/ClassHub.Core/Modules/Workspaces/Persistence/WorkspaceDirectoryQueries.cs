using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Workspaces.Persistence;

/// <summary>
/// Workspace metadata across tenants for platform administration (allow-listed tenant-filter bypass; the admin
/// endpoints require a platform role with MFA). Only names, dates and ownership: never the workspace's content.
/// </summary>
internal sealed class WorkspaceDirectoryQueries(ClassHubDbContext db) : IWorkspaceDirectory
{
    private IQueryable<WorkspaceMember> ActiveOwners() =>
        db.Set<WorkspaceMember>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .Where(m => m.Role == WorkspaceRole.Owner && m.RevokedAtUtc == null);

    public async Task<IReadOnlyDictionary<Guid, OwnedWorkspace>> OwnedByAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, OwnedWorkspace>();
        }

        var rows = await ActiveOwners()
            .Where(m => userIds.Contains(m.UserId))
            .Join(db.Set<Workspace>(), m => m.WorkspaceId, w => w.Id, (m, w) => new OwnedWorkspace(m.UserId, w.Id, w.Name, w.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        // One workspace per teacher in the MVP; the oldest wins should there ever be more.
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.CreatedAtUtc).First());
    }

    public Task<bool> ExistsAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.Set<Workspace>().AsNoTracking().AnyAsync(w => w.Id == workspaceId, cancellationToken);

    public async Task<TeacherCounts> CountTeachersAsync(DateTime activeSinceUtc, CancellationToken cancellationToken)
    {
        var owners = ActiveOwners()
            .Join(db.Set<AppUser>(), m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Status, u.LastLoginAtUtc })
            .Distinct();
        var teachers = await owners.CountAsync(cancellationToken);
        var active = await owners.CountAsync(u => u.Status == UserStatus.Active && u.LastLoginAtUtc >= activeSinceUtc, cancellationToken);
        return new TeacherCounts(teachers, active);
    }
}
