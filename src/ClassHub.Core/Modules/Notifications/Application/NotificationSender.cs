using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Notifications.Application;

/// <summary>
/// Sends one claimed notification in its workspace's scope (R2-P11). Everything is decided at send time: the student and
/// their address (their linked account's email and language, otherwise their own email), their preferences, and whether
/// the lesson or homework still is what the notification says. The email joins the outbox in the same save as the
/// "sent" mark; the version check makes a second dispatcher's attempt fail instead of sending twice.
/// </summary>
internal sealed class NotificationSender(
    ClassHubDbContext db,
    IStudentLookup students,
    ILessonLookup lessons,
    IHomeworkNotices homework,
    IWorkspaceNames workspaces,
    IEmailOutbox outbox,
    IOptions<AppLinkOptions> links)
{
    /// <summary>The outcome of a notice that no longer applied when it was due (the lesson moved, was cancelled, …).</summary>
    public const string Outdated = "outdated";

    public async Task<NotificationStatus?> SendAsync(Guid notificationId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var notification = await db.Set<Notification>().SingleOrDefaultAsync(n => n.Id == notificationId, cancellationToken);
        if (notification is null || notification.Status != NotificationStatus.Pending)
        {
            return notification?.Status;
        }

        var (status, outcome, message) = await DecideAsync(notification, nowUtc, cancellationToken);
        if (message is not null)
        {
            outbox.Enqueue(message);
        }

        notification.Status = status;
        notification.Outcome = outcome;
        notification.SentAtUtc = status == NotificationStatus.Sent ? nowUtc : null;
        notification.LockedUntilUtc = null;
        notification.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return null; // changed meanwhile (withdrawn, or handled elsewhere): nothing of ours is kept
        }

        return status;
    }

    private async Task<(NotificationStatus Status, string? Outcome, EmailMessage? Message)> DecideAsync(Notification n, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var contact = await students.ContactAsync(n.StudentId, cancellationToken);
        if (contact is null || contact.Archived)
        {
            return Suppressed("student");
        }

        var workspaceName = (await workspaces.FindAsync([n.WorkspaceId], cancellationToken)).GetValueOrDefault(n.WorkspaceId);
        string? address = null;
        string locale = workspaceName?.Locale ?? SupportedLocales.Macedonian;
        NotificationPreference? preference = null;
        var hasAccount = false;
        if (contact.UserId is { } userId)
        {
            var account = await db.Set<AppUser>().AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Email, u.Locale, u.Status }).SingleOrDefaultAsync(cancellationToken);
            if (account is { Status: UserStatus.Active, Email: not null })
            {
                (address, locale, hasAccount) = (account.Email, account.Locale, true);
                preference = await db.Set<NotificationPreference>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            }
        }

        address ??= contact.Email;
        if (string.IsNullOrWhiteSpace(address))
        {
            return Suppressed("no-address");
        }

        if (!NotificationRules.Wanted(n.Kind, preference))
        {
            return Suppressed("preference");
        }

        var portal = hasAccount ? $"{links.Value.PublicBaseUrl.TrimEnd('/')}/student/w/{n.WorkspaceId}" : null;
        var teacher = workspaceName?.Name ?? "ClassHub";
        NotificationContent content;
        if (n.Kind == NotificationKind.HomeworkAssigned)
        {
            var notice = await homework.FindAsync(n.HomeworkId!.Value, n.StudentId, cancellationToken);
            // Only the assignment this notice announces: once removed, a later one is announced by its own notice.
            if (notice is null || notice.AssignmentId != n.HomeworkAssignmentId)
            {
                return Suppressed("homework");
            }

            content = new NotificationContent(teacher, contact.FirstName, notice.Title, null, notice.DueDate, portal is null ? null : $"{portal}/homework/{notice.AssignmentId}");
        }
        else if (n.Kind == NotificationKind.SeriesScheduled)
        {
            // About the series: its next lesson for this student when sent; none left (all cancelled or ended): outdated.
            var next = await lessons.NextInSeriesAsync(n.SeriesId!.Value, n.StudentId, nowUtc, cancellationToken);
            if (next is null)
            {
                return Suppressed(Outdated);
            }

            content = new NotificationContent(teacher, contact.FirstName, next.Title, NotificationRules.LocalTime(next.StartUtc, next.TimeZone, locale), null, portal);
        }
        else if (n.Kind == NotificationKind.LessonCancelled)
        {
            // Told unless it is no longer true: the student is (again) in the lesson and it is still scheduled. Also when
            // the lesson was removed: named as it was when cancelled.
            var lessonId = n.LessonId!.Value;
            var current = (await lessons.FindAsync([lessonId], cancellationToken)).GetValueOrDefault(lessonId);
            if (current is { Status: "Scheduled" }
                && (await lessons.AttendanceAsync(lessonId, cancellationToken))?.Participants.Any(p => p.StudentId == n.StudentId) == true)
            {
                return Suppressed(Outdated);
            }

            content = new NotificationContent(teacher, contact.FirstName, n.LessonTitle ?? string.Empty,
                NotificationRules.LocalTime(n.LessonStartUtc!.Value, n.LessonTimeZone!, locale), null, portal);
        }
        else
        {
            var lessonId = n.LessonId!.Value;
            var lesson = (await lessons.FindAsync([lessonId], cancellationToken)).GetValueOrDefault(lessonId);
            var facts = await lessons.AttendanceAsync(lessonId, cancellationToken);
            if (lesson is null || facts is null)
            {
                return Suppressed("lesson");
            }

            var scheduled = lesson.Status == "Scheduled";
            var takesPart = facts.Participants.Any(p => p.StudentId == n.StudentId);
            var stillTrue = n.Kind switch
            {
                NotificationKind.LessonScheduled => scheduled && takesPart,
                NotificationKind.LessonRescheduled => scheduled && takesPart && lesson.StartUtc == n.LessonStartUtc,
                // Never in the last hour (also when a planned reminder was delayed): too late to be of use.
                NotificationKind.LessonReminder => scheduled && takesPart && lesson.StartUtc == n.LessonStartUtc
                    && lesson.StartUtc - nowUtc >= NotificationRules.MinimumReminderNotice,
                _ => false,
            };
            if (!stillTrue)
            {
                return Suppressed(Outdated);
            }

            content = new NotificationContent(teacher, contact.FirstName, lesson.Title, NotificationRules.LocalTime(lesson.StartUtc, lesson.TimeZone, locale), null, portal);
        }

        return (NotificationStatus.Sent, null, NotificationEmails.Create(n.Kind, address, locale, content));
    }

    private static (NotificationStatus, string?, EmailMessage?) Suppressed(string why) => (NotificationStatus.Suppressed, why, null);
}
