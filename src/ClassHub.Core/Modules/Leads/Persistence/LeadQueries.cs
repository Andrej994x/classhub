using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Leads.Persistence;

/// <summary>
/// Leads including archived ones (allow-listed: only the <c>Archived</c> filter is switched off; the tenant filter
/// always applies, so this still sees the current workspace only), and the intake's per-workspace quota, which runs
/// before any tenant binding and therefore filters by the workspace explicitly.
/// </summary>
internal sealed class LeadQueries(ClassHubDbContext db)
{
    public IQueryable<Lead> WithArchived(bool tracking = false)
    {
        var leads = db.Set<Lead>().IgnoreQueryFilters([TenancyFilters.Archived]);
        return tracking ? leads : leads.AsNoTracking();
    }

    /// <summary>
    /// Serializes lead intake per workspace for the caller's transaction (sp_getapplock, released at commit or
    /// rollback), so the quota check and the insert are atomic even for parallel submissions from many addresses.
    /// </summary>
    public async Task LockIntakeAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:lead-intake:{workspaceId:N}";
        await db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50002, 'Could not acquire the lead intake lock.', 1;",
            cancellationToken);
    }

    /// <summary>Leads of <paramref name="workspaceId"/> created since <paramref name="sinceUtc"/>, archived ones included.</summary>
    public Task<int> CountCreatedSinceAsync(Guid workspaceId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        db.Set<Lead>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(l => l.WorkspaceId == workspaceId && l.CreatedAtUtc >= sinceUtc, cancellationToken);
}
