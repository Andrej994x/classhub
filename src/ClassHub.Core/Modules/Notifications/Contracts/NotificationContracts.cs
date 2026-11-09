namespace ClassHub.Core.Modules.Notifications.Contracts;

/// <summary>
/// Plans notifications to students in the caller's unit of work (saved with the caller's <c>SaveChangesAsync</c>, in the
/// same transaction as the change they announce). Planning twice changes nothing (dedupe keys); later changes withdraw
/// what is now outdated. Reminders are planned by the dispatcher from the schedule itself.
/// </summary>
public interface INotificationScheduler
{
    /// <summary>A new lesson for these students.</summary>
    Task LessonScheduledAsync(Guid lessonId, DateTime startUtc, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>The lesson moved: pending reminders are withdrawn and the students are told the new time.</summary>
    Task LessonRescheduledAsync(Guid lessonId, DateTime startUtc, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>
    /// The lesson is cancelled (or these students no longer take part): everything pending is withdrawn, and students who
    /// were already told about it are told it is off.
    /// </summary>
    Task LessonCancelledAsync(Guid lessonId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);

    /// <summary>New homework for these students.</summary>
    Task HomeworkAssignedAsync(Guid homeworkId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);
}

/// <summary>The signed-in user's choices for optional notifications.</summary>
public sealed record NotificationPreferencesResponse(bool LessonReminders, bool HomeworkEmails);

public sealed record UpdateNotificationPreferencesRequest(bool? LessonReminders, bool? HomeworkEmails);
