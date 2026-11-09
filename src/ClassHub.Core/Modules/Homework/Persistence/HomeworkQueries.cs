using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Homework.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Homework.Persistence;

/// <summary>Reads that include archived homework (allow-listed archive-filter bypass; the tenant filter always applies).</summary>
internal sealed class HomeworkQueries(ClassHubDbContext db)
{
    public IQueryable<HomeworkTask> WithArchived(bool tracking = false)
    {
        var homework = db.Set<HomeworkTask>().IgnoreQueryFilters([TenancyFilters.Archived]);
        return tracking ? homework : homework.AsNoTracking();
    }
}
