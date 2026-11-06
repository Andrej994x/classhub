using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Students.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Students.Application;

internal sealed class StudentLookup(StudentQueries queries) : IStudentLookup
{
    public async Task<IReadOnlyDictionary<Guid, StudentSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? new Dictionary<Guid, StudentSummary>()
            : await queries.WithArchived().Where(s => ids.Contains(s.Id))
                .Select(s => new StudentSummary(s.Id, s.FirstName, s.LastName, s.ArchivedAtUtc != null))
                .ToDictionaryAsync(s => s.Id, cancellationToken);
}
