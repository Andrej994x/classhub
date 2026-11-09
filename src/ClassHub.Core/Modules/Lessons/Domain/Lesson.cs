using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Lessons.Domain;

/// <summary>
/// A lesson of one teacher (M07/M08, R2-P4): one student, or a group whose members at scheduling time become the
/// participants. Times are stored in UTC and shown in the workspace time zone recorded with the lesson.
/// </summary>
public sealed class Lesson : ITenantOwned
{
    public const int TitleMaxLength = 120;
    public const int TopicMaxLength = 500;
    public const int MeetingUrlMaxLength = 500;
    public const int LocationMaxLength = 200;
    public const int ReasonMaxLength = 500;
    public const int PrivateNoteMaxLength = 4000;
    public const int SharedSummaryMaxLength = 2000;
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 8 * 60;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>The teacher whose schedule this occupies (overlaps are checked per teacher).</summary>
    public Guid TeacherUserId { get; set; }

    public LessonKind Kind { get; set; }

    public Guid? GroupId { get; set; }

    public Guid? SubjectId { get; set; }

    public required string Title { get; set; }

    public string? Topic { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>The IANA time zone the lesson was scheduled in (the workspace's at that time).</summary>
    public required string TimeZone { get; set; }

    public LessonMode Mode { get; set; }

    public string? MeetingUrl { get; set; }

    public string? Location { get; set; }

    public LessonStatus Status { get; set; } = LessonStatus.Scheduled;

    public string? CancellationReason { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>The teacher's own notes about the lesson; never shown to students (D-40).</summary>
    public string? PrivateNote { get; set; }

    /// <summary>An optional summary the teacher shares with the lesson's students (D-40).</summary>
    public string? SharedSummary { get; set; }

    /// <summary>The series this lesson was created by (R2-P5), and its occurrence number in it.</summary>
    public Guid? SeriesId { get; set; }

    public int? SeriesIndex { get; set; }

    /// <summary>Changed on its own ("only this lesson"): later edits of the series leave it alone.</summary>
    public bool SeriesException { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A student taking part in a lesson (for a group lesson: the members when it was scheduled).</summary>
public sealed class LessonParticipant : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid LessonId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Recorded when the lesson is held (R2-P7, D-22): only Present consumes from a package automatically.</summary>
    public AttendanceStatus? Attendance { get; set; }
}

/// <summary>
/// A lesson event for other modules (R2-P7: Finance consumes packages from it in R2-P10), written in the same transaction
/// as the change. A lesson is completed once, so it has at most one <see cref="LessonEventKinds.Completed"/> event.
/// </summary>
public sealed class LessonEvent : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid LessonId { get; set; }

    public required string Kind { get; set; }

    public required string PayloadJson { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Set by the consumer (Finance) once handled.</summary>
    public DateTime? ProcessedAtUtc { get; set; }
}

public static class LessonRules
{
    /// <summary>Only a scheduled lesson moves, and only to a final state.</summary>
    public static bool CanChange(LessonStatus from, LessonStatus to) =>
        from == LessonStatus.Scheduled && to is LessonStatus.Completed or LessonStatus.Cancelled;

    /// <summary>Half-open intervals [start, end): lessons that only touch (one ends when the next starts) do not overlap.</summary>
    public static bool Overlaps(DateTime startA, DateTime endA, DateTime startB, DateTime endB) => startA < endB && startB < endA;

    /// <summary>Only lessons that happen (or happened) occupy the schedule; cancelled ones free their time.</summary>
    public static bool OccupiesSchedule(LessonStatus status) => status != LessonStatus.Cancelled;
}

/// <summary>Wall-clock times in a time zone ↔ UTC, with the daylight-saving edge cases decided explicitly.</summary>
public static class LessonTime
{
    /// <summary>
    /// The UTC instant of a local wall-clock time. A time skipped when clocks go forward does not exist and is refused;
    /// a time that occurs twice when clocks go back means the first occurrence (still summer time).
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone, string field)
    {
        ArgumentNullException.ThrowIfNull(zone);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = ["This time does not exist on that day: the clocks move forward."],
            });
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone), DateTimeKind.Unspecified);

    /// <summary>The UTC instant a local day starts (the first existing minute when midnight itself is skipped).</summary>
    public static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        return ToUtc(local, zone, "date");
    }
}
