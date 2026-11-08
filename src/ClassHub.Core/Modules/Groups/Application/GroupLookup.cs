using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Groups.Domain;
using ClassHub.Core.Modules.Groups.Persistence;
using ClassHub.Core.Modules.Students.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Groups.Application;

internal sealed class GroupLookup(ClassHubDbContext db, GroupQueries queries, IStudentLookup students) : IGroupLookup
{
    public async Task<IReadOnlyDictionary<Guid, GroupSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, GroupSummary>();
        }

        var groups = await queries.WithArchived().Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);
        var members = await db.Set<GroupMember>().AsNoTracking()
            .Where(m => ids.Contains(m.GroupId) && m.RemovedAtUtc == null)
            .Select(m => new { m.GroupId, m.StudentId })
            .ToListAsync(cancellationToken);
        var names = await students.FindAsync([.. members.Select(m => m.StudentId).Distinct()], cancellationToken);
        return groups.ToDictionary(g => g.Id, g => new GroupSummary(g.Id, g.Name, g.ArchivedAtUtc is not null,
            [.. members.Where(m => m.GroupId == g.Id && names.TryGetValue(m.StudentId, out var s) && !s.Archived).Select(m => m.StudentId)]));
    }
}
