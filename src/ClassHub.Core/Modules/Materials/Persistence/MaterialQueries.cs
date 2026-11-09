using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Materials.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Materials.Persistence;

/// <summary>Reads that include archived materials (allow-listed archive-filter bypass; the tenant filter always applies).</summary>
internal sealed class MaterialQueries(ClassHubDbContext db)
{
    public IQueryable<Material> WithArchived(bool tracking = false)
    {
        var materials = db.Set<Material>().IgnoreQueryFilters([TenancyFilters.Archived]);
        return tracking ? materials : materials.AsNoTracking();
    }
}
