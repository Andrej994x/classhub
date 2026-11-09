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

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        queries.WithArchived().CountAsync(s => s.ArchivedAtUtc == null, cancellationToken);

    public async Task<StudentContact?> ContactAsync(Guid studentId, CancellationToken cancellationToken) =>
        await queries.WithArchived().Where(s => s.Id == studentId)
            .Select(s => new StudentContact(s.Id, s.FirstName, s.Email, s.UserId, s.ArchivedAtUtc != null))
            .SingleOrDefaultAsync(cancellationToken);
}
