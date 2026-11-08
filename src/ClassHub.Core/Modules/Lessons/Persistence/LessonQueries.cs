using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Persistence;

/// <summary>
/// The schedule lock and the overlap query (allow-listed: the overlap check reads the teacher's own lessons in every
/// workspace, because one teacher cannot be in two places; it never returns them to the caller).
/// </summary>
internal sealed class LessonQueries(ClassHubDbContext db)
{
    /// <summary>Serializes schedule changes of one teacher until the caller's transaction ends.</summary>
    public Task LockScheduleAsync(Guid teacherUserId, CancellationToken cancellationToken)
    {
        var resource = $"classhub:schedule:{teacherUserId:N}";
        return db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50009, 'Could not acquire the schedule lock.', 1;",
            cancellationToken);
    }

    /// <summary>Whether another lesson of the teacher that occupies the schedule overlaps [startUtc, endUtc).</summary>
    public Task<bool> OverlapsAsync(Guid teacherUserId, DateTime startUtc, DateTime endUtc, Guid? exceptLessonId, CancellationToken cancellationToken) =>
        db.Set<Lesson>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .AnyAsync(l => l.TeacherUserId == teacherUserId && l.Status != Contracts.LessonStatus.Cancelled
                && l.StartUtc < endUtc && startUtc < l.EndUtc && l.Id != exceptLessonId, cancellationToken);
}
