using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Sites.Persistence;

/// <summary>
/// Cross-workspace slug lookup (allow-listed tenant-filter bypass): a slug is a platform-wide address, so availability
/// must consider every site. Returns only a yes/no, never another workspace's data.
/// </summary>
internal sealed class SiteSlugQueries(ClassHubDbContext db)
{
    public Task<bool> IsTakenByAnotherSiteAsync(string slug, Guid? ownSiteId, CancellationToken cancellationToken) =>
        db.Set<Site>().IgnoreQueryFilters([TenancyFilters.Tenant]).AnyAsync(s => s.Slug == slug && s.Id != ownSiteId, cancellationToken);
}
