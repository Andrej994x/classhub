using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Leads.Persistence;

/// <summary>
/// Leads including archived ones (allow-listed: only the <c>Archived</c> filter is switched off; the tenant filter
/// always applies, so this still sees the current workspace only).
/// </summary>
internal sealed class LeadQueries(ClassHubDbContext db)
{
    public IQueryable<Lead> WithArchived(bool tracking = false)
    {
        var leads = db.Set<Lead>().IgnoreQueryFilters([TenancyFilters.Archived]);
        return tracking ? leads : leads.AsNoTracking();
    }

    /// <summary>Leads of the current workspace created since <paramref name="sinceUtc"/>, archived ones included.</summary>
    public Task<int> CountCreatedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken) =>
        WithArchived().CountAsync(l => l.CreatedAtUtc >= sinceUtc, cancellationToken);
}
