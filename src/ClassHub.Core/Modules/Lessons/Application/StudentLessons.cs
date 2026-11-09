using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>
/// The signed-in student's own lessons with one teacher (R2-P12): only lessons they take part in, never the teacher's
/// private note or the other participants.
/// </summary>
internal sealed class StudentLessons(ClassHubDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public const int UpcomingMax = 10;
    public const int PastMax = 10;

    public async Task<StudentLessonsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var studentId = tenant.StudentId ?? throw new InvalidOperationException("Not a student request.");
        var now = clock.GetUtcNow().UtcDateTime;
        var own = db.Set<Lesson>().AsNoTracking()
            .Join(db.Set<LessonParticipant>().AsNoTracking().Where(p => p.StudentId == studentId), l => l.Id, p => p.LessonId, (l, p) => new { Lesson = l, p.Attendance });

        var upcoming = await own.Where(x => x.Lesson.Status == LessonStatus.Scheduled && x.Lesson.EndUtc > now)
            .OrderBy(x => x.Lesson.StartUtc).ThenBy(x => x.Lesson.Id).Take(UpcomingMax)
            .Select(x => new StudentLessonResponse(x.Lesson.Id, x.Lesson.Title, x.Lesson.Topic, x.Lesson.StartUtc, x.Lesson.EndUtc, x.Lesson.TimeZone,
                x.Lesson.Mode, x.Lesson.MeetingUrl, x.Lesson.Location, x.Lesson.Status, null, null))
            .ToListAsync(cancellationToken);

        // Held lessons, and ones that are over but not recorded yet (still Scheduled): the most recent first.
        var past = await own.Where(x => x.Lesson.Status == LessonStatus.Completed || (x.Lesson.Status == LessonStatus.Scheduled && x.Lesson.EndUtc <= now))
            .OrderByDescending(x => x.Lesson.StartUtc).ThenBy(x => x.Lesson.Id).Take(PastMax)
            .Select(x => new StudentLessonResponse(x.Lesson.Id, x.Lesson.Title, x.Lesson.Topic, x.Lesson.StartUtc, x.Lesson.EndUtc, x.Lesson.TimeZone,
                x.Lesson.Mode, null, x.Lesson.Location, x.Lesson.Status,
                x.Lesson.Status == LessonStatus.Completed ? x.Attendance : null,
                x.Lesson.Status == LessonStatus.Completed ? x.Lesson.SharedSummary : null))
            .ToListAsync(cancellationToken);

        var held = await own.Where(x => x.Lesson.Status == LessonStatus.Completed)
            .GroupBy(x => x.Attendance).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(AttendanceStatus status) => held.Where(h => h.Key == status).Sum(h => h.Count);
        var attendance = new StudentAttendanceResponse(held.Sum(h => h.Count), Count(AttendanceStatus.Present), Count(AttendanceStatus.Absent), Count(AttendanceStatus.Excused));

        return new StudentLessonsResponse(upcoming, past, attendance);
    }
}
