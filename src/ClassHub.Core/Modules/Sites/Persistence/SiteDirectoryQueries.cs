using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Sites.Persistence;

/// <summary>
/// Site metadata across tenants for platform administration (allow-listed tenant-filter bypass; the admin endpoints
/// require a platform role with MFA): the address and whether it is published, never the content.
/// </summary>
internal sealed class SiteDirectoryQueries(ClassHubDbContext db) : ISiteDirectory
{
    private IQueryable<Site> All() => db.Set<Site>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking();

    public async Task<IReadOnlyDictionary<Guid, SiteDirectoryEntry>> ForWorkspacesAsync(IReadOnlyCollection<Guid> workspaceIds, CancellationToken cancellationToken)
    {
        if (workspaceIds.Count == 0)
        {
            return new Dictionary<Guid, SiteDirectoryEntry>();
        }

        return await All()
            .Where(s => workspaceIds.Contains(s.WorkspaceId))
            .Select(s => new SiteDirectoryEntry(s.WorkspaceId, s.Slug, s.PublishedAtUtc != null && s.PublishedJson != null))
            .ToDictionaryAsync(s => s.WorkspaceId, cancellationToken);
    }

    public Task<int> CountPublishedAsync(CancellationToken cancellationToken) =>
        All().CountAsync(s => s.PublishedAtUtc != null && s.PublishedJson != null, cancellationToken);
}
