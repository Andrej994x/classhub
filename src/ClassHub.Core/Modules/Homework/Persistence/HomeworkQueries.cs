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

    /// <summary>
    /// Inside the caller's transaction: whether the homework is still active, holding an update lock on its row until
    /// the commit. Archiving (an update of that row) therefore waits for an answer being saved, or wins and the answer
    /// is refused; a locking read sees the latest committed row (not the snapshot).
    /// </summary>
    public async Task<bool> LockActiveAsync(Guid homeworkId, Guid workspaceId, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM [Homework] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {homeworkId} AND [WorkspaceId] = {workspaceId} AND [ArchivedAtUtc] IS NULL")
            .SingleAsync(cancellationToken) > 0;
}
