using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Students.Persistence;

/// <summary>
/// Reads that include archived students (allow-listed archive-filter bypass; the tenant filter always applies) and
/// the intake lock that serializes every creation of students in a workspace.
/// </summary>
internal sealed class StudentQueries(ClassHubDbContext db)
{
    public IQueryable<Student> WithArchived(bool tracking = false)
    {
        var students = db.Set<Student>().IgnoreQueryFilters([TenancyFilters.Archived]);
        return tracking ? students : students.AsNoTracking();
    }

    /// <summary>
    /// Serializes student creation in one workspace (manual adds, lead conversions, restores) until the caller's
    /// transaction ends: the email dedupe and the plan limit then see every earlier creation.
    /// </summary>
    public Task LockIntakeAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:student-intake:{workspaceId:N}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50009, 'Could not acquire the student intake lock.', 1;",
            cancellationToken);
    }
}
