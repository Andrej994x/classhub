using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Students.Persistence;

/// <summary>
/// Student account reads that are not workspace-scoped (allow-listed tenant-filter bypass), always narrowed by a
/// capability: the hash of an invitation token, or the signed-in user's own id. They never list other students.
/// </summary>
internal sealed class StudentAccountQueries(ClassHubDbContext db) : IStudentSelfLookup
{
    private IQueryable<Student> AllStudents(bool tracking) =>
        tracking ? db.Set<Student>().IgnoreQueryFilters() : db.Set<Student>().IgnoreQueryFilters().AsNoTracking();

    /// <summary>The invitation with this token hash (any workspace, any state), or null.</summary>
    public Task<StudentInvitation?> FindInvitationAsync(byte[] tokenHash, bool tracking, CancellationToken cancellationToken)
    {
        var invitations = db.Set<StudentInvitation>().IgnoreQueryFilters();
        return (tracking ? invitations : invitations.AsNoTracking()).SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);
    }

    /// <summary>The invited student (archived ones included, so the caller can refuse them).</summary>
    public Task<Student?> FindStudentAsync(Guid workspaceId, Guid studentId, bool tracking, CancellationToken cancellationToken) =>
        AllStudents(tracking).SingleOrDefaultAsync(s => s.WorkspaceId == workspaceId && s.Id == studentId, cancellationToken);

    /// <summary>Whether the account is already linked to a student record of this workspace (archived ones included).</summary>
    public Task<bool> IsLinkedInWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        AllStudents(tracking: false).AnyAsync(s => s.WorkspaceId == workspaceId && s.UserId == userId, cancellationToken);

    /// <summary>The account's active student records across teachers (D-16).</summary>
    public Task<List<Student>> ActiveForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        AllStudents(tracking: false).Where(s => s.UserId == userId && s.ArchivedAtUtc == null)
            .OrderBy(s => s.CreatedAtUtc).ToListAsync(cancellationToken);

    public Task<Guid?> FindActiveStudentAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        AllStudents(tracking: false).Where(s => s.WorkspaceId == workspaceId && s.UserId == userId && s.ArchivedAtUtc == null)
            .Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
}
