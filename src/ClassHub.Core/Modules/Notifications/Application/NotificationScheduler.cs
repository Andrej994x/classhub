using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Notifications.Application;

/// <summary>
/// Plans immediate notifications in the caller's unit of work (R2-P11). Rows are only added when their dedupe key is new
/// (lesson changes are serialized per teacher by the schedule lock, homework assignments are unique per student), and
/// a later change withdraws the pending rows it makes outdated.
/// </summary>
internal sealed class NotificationScheduler(ClassHubDbContext db, ITenantContext tenant, TimeProvider clock) : INotificationScheduler
{
    private static readonly NotificationKind[] LessonKinds =
        [NotificationKind.LessonScheduled, NotificationKind.LessonRescheduled, NotificationKind.LessonReminder];

    public async Task LessonScheduledAsync(Guid lessonId, DateTime startUtc, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            await AddAsync(NotificationKind.LessonScheduled, studentId, lessonId: lessonId, startUtc: startUtc, cancellationToken: cancellationToken);
        }
    }

    public async Task LessonRescheduledAsync(Guid lessonId, DateTime startUtc, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            var pending = await PendingAsync(lessonId, studentId, cancellationToken);
            // Reminders and earlier reschedules for the old time are withdrawn (a reminder for the new time follows).
            Withdraw(pending.Where(n => n.Kind is NotificationKind.LessonReminder or NotificationKind.LessonRescheduled), "rescheduled");
            if (pending.Any(n => n.Kind == NotificationKind.LessonScheduled))
            {
                continue; // the announcement has not gone out yet: it will name the new time
            }

            await AddAsync(NotificationKind.LessonRescheduled, studentId, lessonId: lessonId, startUtc: startUtc, cancellationToken: cancellationToken);
        }
    }

    public async Task LessonCancelledAsync(Guid lessonId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            Withdraw(await PendingAsync(lessonId, studentId, cancellationToken), "cancelled");
            // Only a student who was told about the lesson needs to hear it is off.
            var told = await db.Set<Notification>().AnyAsync(n => n.LessonId == lessonId && n.StudentId == studentId && n.Status == NotificationStatus.Sent
                && n.Kind != NotificationKind.LessonCancelled, cancellationToken);
            if (told)
            {
                await AddAsync(NotificationKind.LessonCancelled, studentId, lessonId: lessonId, cancellationToken: cancellationToken);
            }
        }
    }

    public async Task HomeworkAssignedAsync(Guid homeworkId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        foreach (var studentId in studentIds.Distinct())
        {
            await AddAsync(NotificationKind.HomeworkAssigned, studentId, homeworkId: homeworkId, cancellationToken: cancellationToken);
        }
    }

    private async Task<List<Notification>> PendingAsync(Guid lessonId, Guid studentId, CancellationToken cancellationToken) =>
        await db.Set<Notification>().Where(n => n.LessonId == lessonId && n.StudentId == studentId && n.Status == NotificationStatus.Pending
            && LessonKinds.Contains(n.Kind)).ToListAsync(cancellationToken);

    private void Withdraw(IEnumerable<Notification> notifications, string why)
    {
        foreach (var notification in notifications)
        {
            notification.Status = NotificationStatus.Cancelled;
            notification.Outcome = why;
            notification.LockedUntilUtc = null;
            notification.Version++;
        }
    }

    private async Task AddAsync(NotificationKind kind, Guid studentId, Guid? lessonId = null, Guid? homeworkId = null, DateTime? startUtc = null, CancellationToken cancellationToken = default)
    {
        var key = NotificationRules.DedupeKey(kind, lessonId ?? homeworkId!.Value, studentId, startUtc);
        if (await db.Set<Notification>().AnyAsync(n => n.DedupeKey == key, cancellationToken)
            || db.ChangeTracker.Entries<Notification>().Any(e => e.Entity.DedupeKey == key))
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        db.Set<Notification>().Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            Kind = kind,
            DedupeKey = key,
            StudentId = studentId,
            TeacherUserId = tenant.UserId ?? throw new InvalidOperationException("Notifications are planned on a teacher's behalf."),
            LessonId = lessonId,
            LessonStartUtc = kind is NotificationKind.LessonScheduled or NotificationKind.LessonRescheduled ? startUtc : null,
            HomeworkId = homeworkId,
            DueAtUtc = now,
            CreatedAtUtc = now,
        });
    }
}
