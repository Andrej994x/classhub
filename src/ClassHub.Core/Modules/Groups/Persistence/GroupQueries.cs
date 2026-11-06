using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Groups.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Groups.Persistence;

/// <summary>Reads that include archived groups (allow-listed archive-filter bypass; the tenant filter always applies).</summary>
internal sealed class GroupQueries(ClassHubDbContext db)
{
    public IQueryable<Group> WithArchived(bool tracking = false)
    {
        var groups = db.Set<Group>().IgnoreQueryFilters([TenancyFilters.Archived]);
        return tracking ? groups : groups.AsNoTracking();
    }

    /// <summary>
    /// Serializes membership changes and archiving of one group (inside the caller's transaction), so the member cap
    /// and the "no new members in an archived group" rule hold under parallel requests.
    /// </summary>
    public Task LockMembersAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:group-members:{groupId:N}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50009, 'Could not acquire the group members lock.', 1;",
            cancellationToken);
    }
}
