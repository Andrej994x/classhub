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

        var upcoming = (await own.Where(x => x.Lesson.Status == LessonStatus.Scheduled && x.Lesson.EndUtc > now)
            .OrderBy(x => x.Lesson.StartUtc).ThenBy(x => x.Lesson.Id).Take(UpcomingMax)
            .Select(x => new { x.Lesson.Id, x.Lesson.Title, x.Lesson.Topic, x.Lesson.StartUtc, x.Lesson.EndUtc, x.Lesson.TimeZone, x.Lesson.Mode, x.Lesson.MeetingUrl, x.Lesson.Location, x.Lesson.Status })
            .ToListAsync(cancellationToken))
            .Select(l => new StudentLessonResponse(l.Id, l.Title, l.Topic, l.StartUtc, l.EndUtc, l.TimeZone, l.Mode.ToString(), l.MeetingUrl, l.Location, l.Status.ToString(), null, null))
            .ToList();

        // Held lessons, and ones that are over but not recorded yet (still Scheduled): the most recent first.
        var past = (await own.Where(x => x.Lesson.Status == LessonStatus.Completed || (x.Lesson.Status == LessonStatus.Scheduled && x.Lesson.EndUtc <= now))
            .OrderByDescending(x => x.Lesson.StartUtc).ThenBy(x => x.Lesson.Id).Take(PastMax)
            .Select(x => new { x.Lesson.Id, x.Lesson.Title, x.Lesson.Topic, x.Lesson.StartUtc, x.Lesson.EndUtc, x.Lesson.TimeZone, x.Lesson.Mode, x.Lesson.Location, x.Lesson.Status, x.Attendance, x.Lesson.SharedSummary })
            .ToListAsync(cancellationToken))
            .Select(l =>
            {
                var held = l.Status == LessonStatus.Completed;
                return new StudentLessonResponse(l.Id, l.Title, l.Topic, l.StartUtc, l.EndUtc, l.TimeZone, l.Mode.ToString(), null, l.Location, l.Status.ToString(),
                    held ? l.Attendance?.ToString() : null, held ? l.SharedSummary : null);
            })
            .ToList();

        var held = await own.Where(x => x.Lesson.Status == LessonStatus.Completed)
            .GroupBy(x => x.Attendance).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(AttendanceStatus status) => held.Where(h => h.Key == status).Sum(h => h.Count);
        var attendance = new StudentAttendanceResponse(held.Sum(h => h.Count), Count(AttendanceStatus.Present), Count(AttendanceStatus.Absent), Count(AttendanceStatus.Excused));

        return new StudentLessonsResponse(upcoming, past, attendance);
    }
}
