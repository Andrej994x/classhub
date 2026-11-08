using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Workspaces.Persistence;

/// <summary>Workspace names for student invitations and the student portal (workspaces themselves are not tenant-owned).</summary>
internal sealed class WorkspaceNameQueries(ClassHubDbContext db) : IWorkspaceNames
{
    public async Task<IReadOnlyDictionary<Guid, WorkspaceName>> FindAsync(IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken)
    {
        if (workspaceIds.Count == 0)
        {
            return new Dictionary<Guid, WorkspaceName>();
        }

        return await db.Set<Workspace>().AsNoTracking()
            .Where(w => workspaceIds.Contains(w.Id))
            .Select(w => new WorkspaceName(w.Id, w.Name, w.Locale))
            .ToDictionaryAsync(w => w.WorkspaceId, cancellationToken);
    }
}
