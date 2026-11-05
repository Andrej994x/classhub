using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Workspaces.Persistence;

/// <summary>
/// Membership lookups that run <b>before</b> a request is bound to a workspace, so they bypass the tenant
/// filter. Allow-listed in the tenant-bypass architecture test; every query filters by the user explicitly.
/// </summary>
internal sealed class WorkspaceAccessQueries(ClassHubDbContext db) : IWorkspaceMembershipLookup, IWorkspacePublicVisibility, IWorkspaceOwnerContact
{
    public Task<WorkspaceOwnerContact?> GetOwnerAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.Set<WorkspaceMember>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner && m.RevokedAtUtc == null)
            .Join(db.Set<AppUser>(), m => m.UserId, u => u.Id, (m, u) => u)
            .Select(u => new WorkspaceOwnerContact(u.Id, u.Email!, u.Locale, u.EmailConfirmed))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Workspaces with an active owner membership whose account is active (not suspended).</summary>
    public async Task<IReadOnlySet<Guid>> VisibleAsync(IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken)
    {
        if (workspaceIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var visible = await db.Set<WorkspaceMember>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .AsNoTracking()
            .Where(m => workspaceIds.Contains(m.WorkspaceId) && m.Role == WorkspaceRole.Owner && m.RevokedAtUtc == null)
            .Join(db.Set<AppUser>(), m => m.UserId, u => u.Id, (m, u) => new { m.WorkspaceId, u.Status })
            .Where(x => x.Status == UserStatus.Active)
            .Select(x => x.WorkspaceId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return visible.ToHashSet();
    }

    public async Task<WorkspaceRole?> GetActiveRoleAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await db.Set<WorkspaceMember>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.UserId == userId && m.RevokedAtUtc == null)
            .Select(m => (WorkspaceRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);
        return role;
    }

    /// <summary>The workspaces the user belongs to (for <c>GET /api/me/workspaces</c>).</summary>
    public async Task<List<MyWorkspaceResponse>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<WorkspaceMember>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.RevokedAtUtc == null)
            .Join(db.Set<Workspace>(), m => m.WorkspaceId, w => w.Id, (m, w) => new { w.Id, w.Name, m.Role, w.CreatedAtUtc })
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new MyWorkspaceResponse(x.Id, x.Name, x.Role.ToString()))
            .ToListAsync(cancellationToken);
}
