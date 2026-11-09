using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Notifications.Application;

/// <summary>
/// Plans immediate notifications in the caller's unit of work (R2-P11). A notice is keyed by the change it announces
/// (the lesson's revision, the homework assignment): repeating one change adds nothing, every later change is told. A
/// later change withdraws the pending rows it makes outdated.
/// </summary>
internal sealed class NotificationScheduler(ClassHubDbContext db, ITenantContext tenant, TimeProvider clock) : INotificationScheduler
{
    private static readonly NotificationKind[] LessonKinds =
        [NotificationKind.LessonScheduled, NotificationKind.SeriesScheduled, NotificationKind.LessonRescheduled, NotificationKind.LessonReminder];

    public async Task LessonScheduledAsync(LessonNotice lesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            await AddLessonAsync(NotificationKind.LessonScheduled, lesson, studentId, cancellationToken);
        }
    }

    public async Task SeriesScheduledAsync(LessonNotice firstLesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            await AddLessonAsync(NotificationKind.SeriesScheduled, firstLesson, studentId, cancellationToken);
        }
    }

    public async Task LessonRescheduledAsync(LessonNotice lesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            var pending = await PendingAsync(lesson.LessonId, studentId, cancellationToken);
            // Reminders and earlier reschedules for the old time are withdrawn (a reminder for the new time follows).
            Withdraw(pending.Where(n => n.Kind is NotificationKind.LessonReminder or NotificationKind.LessonRescheduled), "rescheduled");
            if (pending.Any(n => n.Kind is NotificationKind.LessonScheduled or NotificationKind.SeriesScheduled))
            {
                continue; // the announcement has not gone out yet: it will name the new time
            }

            await AddLessonAsync(NotificationKind.LessonRescheduled, lesson, studentId, cancellationToken);
        }
    }

    public async Task LessonCancelledAsync(LessonNotice lesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            Withdraw(await PendingAsync(lesson.LessonId, studentId, cancellationToken), "cancelled");
            // Only a student who was told about this lesson (announcement, reschedule or reminder) needs to hear it is off.
            var told = await db.Set<Notification>().AnyAsync(n => n.LessonId == lesson.LessonId && n.StudentId == studentId && n.Status == NotificationStatus.Sent
                && n.Kind != NotificationKind.LessonCancelled, cancellationToken);
            if (told)
            {
                await AddLessonAsync(NotificationKind.LessonCancelled, lesson, studentId, cancellationToken);
            }
        }
    }

    public async Task HomeworkAssignedAsync(Guid homeworkId, IReadOnlyCollection<HomeworkAssignmentRef> assignments, CancellationToken cancellationToken)
    {
        foreach (var assignment in assignments)
        {
            await AddAsync(new Notification
            {
                Kind = NotificationKind.HomeworkAssigned,
                DedupeKey = NotificationRules.HomeworkKey(assignment.AssignmentId),
                StudentId = assignment.StudentId,
                HomeworkId = homeworkId,
            }, cancellationToken);
        }
    }

    private async Task<List<Notification>> PendingAsync(Guid lessonId, Guid studentId, CancellationToken cancellationToken) =>
        await db.Set<Notification>().Where(n => n.LessonId == lessonId && n.StudentId == studentId && n.Status == NotificationStatus.Pending
            && LessonKinds.Contains(n.Kind)).ToListAsync(cancellationToken);

    private static void Withdraw(IEnumerable<Notification> notifications, string why)
    {
        foreach (var notification in notifications)
        {
            notification.Status = NotificationStatus.Cancelled;
            notification.Outcome = why;
            notification.LockedUntilUtc = null;
            notification.Version++;
        }
    }

    private Task AddLessonAsync(NotificationKind kind, LessonNotice lesson, Guid studentId, CancellationToken cancellationToken) =>
        AddAsync(new Notification
        {
            Kind = kind,
            DedupeKey = NotificationRules.LessonKey(kind, lesson.LessonId, studentId, lesson.Revision),
            StudentId = studentId,
            LessonId = lesson.LessonId,
            LessonStartUtc = lesson.StartUtc,
            LessonTitle = lesson.Title.Length > Notification.TitleMaxLength ? lesson.Title[..Notification.TitleMaxLength] : lesson.Title,
            LessonTimeZone = lesson.TimeZone,
        }, cancellationToken);

    private async Task AddAsync(Notification notification, CancellationToken cancellationToken)
    {
        var key = notification.DedupeKey;
        if (await db.Set<Notification>().AnyAsync(n => n.DedupeKey == key, cancellationToken)
            || db.ChangeTracker.Entries<Notification>().Any(e => e.Entity.DedupeKey == key))
        {
            return; // the same change was planned already
        }

        var now = clock.GetUtcNow().UtcDateTime;
        notification.Id = Guid.CreateVersion7();
        notification.WorkspaceId = tenant.RequireWorkspaceId();
        notification.TeacherUserId = tenant.UserId ?? throw new InvalidOperationException("Notifications are planned on a teacher's behalf.");
        notification.DueAtUtc = now;
        notification.CreatedAtUtc = now;
        db.Set<Notification>().Add(notification);
    }
}
