using System.Globalization;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Notifications.Domain;

public enum NotificationKind
{
    LessonScheduled = 1,
    LessonRescheduled = 2,
    LessonCancelled = 3,
    LessonReminder = 4,
    HomeworkAssigned = 5,

    /// <summary>Recurring lessons were scheduled (one email for the series; each lesson then gets its reminder).</summary>
    SeriesScheduled = 6,
}

public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,

    /// <summary>Not sent at send time: no longer true (cancelled, moved), no recipient, or opted out.</summary>
    Suppressed = 3,

    /// <summary>Withdrawn before sending by a later change (cancellation, rescheduling).</summary>
    Cancelled = 4,
    Failed = 5,
}

/// <summary>
/// A notification to a student (R2-P11, email only, D-26). Rows are planned with a unique dedupe key (never two of the
/// same), claimed by one dispatcher at a time, and checked again when sent: the recipient, their preferences and the
/// lesson or homework are read at send time, so nothing outdated goes out.
/// </summary>
public sealed class Notification : ITenantOwned
{
    public const int DedupeKeyMaxLength = 200;
    public const int OutcomeMaxLength = 100;
    public const int TitleMaxLength = 160;
    public const int TimeZoneMaxLength = 64;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public NotificationKind Kind { get; set; }

    public required string DedupeKey { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>The teacher on whose behalf it is sent (the dispatcher works in their workspace scope).</summary>
    public Guid TeacherUserId { get; set; }

    public Guid? LessonId { get; set; }

    /// <summary>The lesson's start when planned (reminders and reschedules apply only while it still starts then).</summary>
    public DateTime? LessonStartUtc { get; set; }

    /// <summary>The lesson's title and time zone when planned (a cancellation still names a lesson that was removed).</summary>
    public string? LessonTitle { get; set; }

    public string? LessonTimeZone { get; set; }

    public Guid? HomeworkId { get; set; }

    public DateTime DueAtUtc { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public DateTime? LockedUntilUtc { get; set; }

    public int Attempts { get; set; }

    public DateTime? SentAtUtc { get; set; }

    /// <summary>Why it was suppressed, cancelled or failed.</summary>
    public string? Outcome { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A user's choices for the optional notifications (lesson reminders, new homework); missing means on.</summary>
public sealed class NotificationPreference
{
    public Guid UserId { get; set; }

    public bool LessonReminders { get; set; } = true;

    public bool HomeworkEmails { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>The notification rules: dedupe keys, reminder timing, optional kinds and zone-aware times.</summary>
public static class NotificationRules
{
    /// <summary>Reminders go out this long before a lesson.</summary>
    public static readonly TimeSpan ReminderLead = TimeSpan.FromHours(24);

    /// <summary>A lesson planned later than its reminder time still gets one, if at least this long before it starts.</summary>
    public static readonly TimeSpan MinimumReminderNotice = TimeSpan.FromHours(1);

    /// <summary>
    /// A lesson notice is one per lesson, student and lesson revision (its version after the change): retries of one
    /// change are the same notice, every later change (a student added back, a lesson moved back) is a new one.
    /// </summary>
    public static string LessonKey(NotificationKind kind, Guid lessonId, Guid studentId, int revision) =>
        $"{Name(kind)}:{lessonId:N}:{studentId:N}:r{revision.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A reminder is one per lesson, student and start time; <paramref name="attempt"/> counts reminders for that time that
    /// were withdrawn (a lesson moved away and back gets a new one, never two pending or sent for the same time).
    /// </summary>
    public static string ReminderKey(Guid lessonId, Guid studentId, DateTime startUtc, int attempt) =>
        $"{Name(NotificationKind.LessonReminder)}:{lessonId:N}:{studentId:N}:{startUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:{attempt.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>New homework is one per assignment (assigned again after being removed: a new assignment, a new notice).</summary>
    public static string HomeworkKey(Guid assignmentId) => $"{Name(NotificationKind.HomeworkAssigned)}:{assignmentId:N}";

    /// <summary>
    /// When a lesson's reminder is due: <see cref="ReminderLead"/> before it starts; right away when that moment has
    /// passed but the lesson is still at least <see cref="MinimumReminderNotice"/> away; otherwise none.
    /// </summary>
    public static DateTime? ReminderDueAtUtc(DateTime startUtc, DateTime nowUtc)
    {
        var due = startUtc - ReminderLead;
        if (due >= nowUtc)
        {
            return due;
        }

        return startUtc - nowUtc >= MinimumReminderNotice ? nowUtc : null;
    }

    /// <summary>Reminders and new homework can be switched off; lesson changes always go out.</summary>
    public static bool IsOptional(NotificationKind kind) => kind is NotificationKind.LessonReminder or NotificationKind.HomeworkAssigned;

    public static bool Wanted(NotificationKind kind, NotificationPreference? preference) => kind switch
    {
        NotificationKind.LessonReminder => preference?.LessonReminders ?? true,
        NotificationKind.HomeworkAssigned => preference?.HomeworkEmails ?? true,
        _ => true,
    };

    /// <summary>
    /// A lesson time as the recipient reads it: the local date and time in the lesson's time zone (with the zone named),
    /// in the recipient's language.
    /// </summary>
    public static string LocalTime(DateTime startUtc, string timeZone, string locale)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);
        var culture = CultureInfo.GetCultureInfo(locale == "en" ? "en-GB" : "mk-MK");
        return $"{local.ToString("dddd, d MMMM yyyy, HH:mm", culture)} ({timeZone})";
    }

    private static string Name(NotificationKind kind) => kind switch
    {
        NotificationKind.LessonScheduled => "lesson-scheduled",
        NotificationKind.LessonRescheduled => "lesson-rescheduled",
        NotificationKind.LessonCancelled => "lesson-cancelled",
        NotificationKind.LessonReminder => "lesson-reminder",
        NotificationKind.HomeworkAssigned => "homework-assigned",
        NotificationKind.SeriesScheduled => "series-scheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
