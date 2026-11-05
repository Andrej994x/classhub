using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.DiagnosticTests.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.DiagnosticTests.Persistence;

/// <summary>
/// Tests including archived ones (allow-listed: only the <c>Archived</c> filter is off; the tenant filter applies). Used
/// for slugs, which archived tests keep, and for archive/restore.
/// </summary>
internal sealed class DiagnosticTestQueries(ClassHubDbContext db)
{
    public IQueryable<DiagnosticTest> WithArchived() => db.Set<DiagnosticTest>().IgnoreQueryFilters([TenancyFilters.Archived]);

    public async Task<HashSet<string>> SlugsStartingWithAsync(string prefix, CancellationToken cancellationToken) =>
        (await WithArchived().AsNoTracking().Where(t => t.Slug.StartsWith(prefix)).Select(t => t.Slug).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
}
