namespace ClassHub.Core.Modules.Notifications.Contracts;

/// <summary>
/// Plans notifications to students in the caller's unit of work (saved with the caller's <c>SaveChangesAsync</c>, in the
/// same transaction as the change they announce). Planning twice changes nothing (dedupe keys); later changes withdraw
/// what is now outdated. Reminders are planned by the dispatcher from the schedule itself.
/// </summary>
public interface INotificationScheduler
{
    /// <summary>A new lesson for these students.</summary>
    Task LessonScheduledAsync(LessonNotice lesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>
    /// Recurring lessons for these students: one email for the series, naming its next lesson when sent (cancelling one
    /// lesson of the series does not withdraw it).
    /// </summary>
    Task SeriesScheduledAsync(Guid seriesId, LessonNotice firstLesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>The lesson moved: pending reminders are withdrawn and the students are told the new time.</summary>
    Task LessonRescheduledAsync(LessonNotice lesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>
    /// The lesson is cancelled or removed (or these students no longer take part): everything pending is withdrawn, and
    /// students who were already told about it are told it is off.
    /// </summary>
    Task LessonCancelledAsync(LessonNotice lesson, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>New homework for these students (one notice per assignment).</summary>
    Task HomeworkAssignedAsync(Guid homeworkId, IReadOnlyCollection<HomeworkAssignmentRef> assignments, CancellationToken cancellationToken);
}

/// <summary>
/// A lesson as a notification records it: its revision (version after the change) and what a cancellation names even
/// after the lesson is gone.
/// </summary>
public sealed record LessonNotice(Guid LessonId, int Revision, string Title, DateTime StartUtc, string TimeZone);

/// <summary>A student's new assignment of a homework.</summary>
public sealed record HomeworkAssignmentRef(Guid StudentId, Guid AssignmentId);

/// <summary>The signed-in user's choices for optional notifications.</summary>
public sealed record NotificationPreferencesResponse(bool LessonReminders, bool HomeworkEmails);

public sealed record UpdateNotificationPreferencesRequest(bool? LessonReminders, bool? HomeworkEmails);
