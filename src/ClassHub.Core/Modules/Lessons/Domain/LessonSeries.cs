using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Lessons.Domain;

/// <summary>
/// Recurring lessons (R2-P5): every week or every two weeks at the same local time, ending on a date, after a number of
/// lessons, or not at all. Occurrences are created as ordinary lessons up to <see cref="HorizonWeeks"/> ahead (a job
/// extends the horizon). Editing "this and following" or "all future" ends this series and starts a new one (D-24).
/// </summary>
public sealed class LessonSeries : ITenantOwned
{
    public const int HorizonWeeks = 12;
    public const int MaxCount = 200;
    public const int MaxSpanDays = 2 * 366;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid TeacherUserId { get; set; }

    public LessonKind Kind { get; set; }

    public Guid? StudentId { get; set; }

    public Guid? GroupId { get; set; }

    public Guid? SubjectId { get; set; }

    public required string Title { get; set; }

    public string? Topic { get; set; }

    public LessonMode Mode { get; set; }

    public string? MeetingUrl { get; set; }

    public string? Location { get; set; }

    /// <summary>The IANA zone the local times are in (the workspace's when the series was set up).</summary>
    public required string TimeZone { get; set; }

    public DateOnly StartDate { get; set; }

    public TimeOnly StartTime { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>1 = every week, 2 = every two weeks.</summary>
    public int IntervalWeeks { get; set; }

    /// <summary>The last day an occurrence may fall on (also set when the series is ended or replaced).</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>The number of occurrences, when the series ends after a count.</summary>
    public int? Count { get; set; }

    /// <summary>The first occurrence index not created or skipped yet.</summary>
    public int NextIndex { get; set; }

    /// <summary>The series that continues this one after a "this and following" or "all future" edit.</summary>
    public Guid? ReplacedBySeriesId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>An occurrence that could not be created (reported to the teacher; the series goes on).</summary>
public sealed class LessonSeriesSkip : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid SeriesId { get; set; }

    public int SeriesIndex { get; set; }

    public DateOnly Date { get; set; }

    public SeriesSkipReason Reason { get; set; }
}

public static class SeriesPattern
{
    /// <summary>The local date of an occurrence.</summary>
    public static DateOnly DateOf(DateOnly start, int intervalWeeks, int index) => start.AddDays(index * 7 * intervalWeeks);

    /// <summary>Whether an occurrence index is still part of the series (count and end date).</summary>
    public static bool Includes(DateOnly start, int intervalWeeks, DateOnly? endDate, int? count, int index) =>
        index >= 0 && (count is null || index < count) && (endDate is null || DateOf(start, intervalWeeks, index) <= endDate);

    /// <summary>
    /// The occurrences from <paramref name="fromIndex"/> whose date is before <paramref name="untilExclusive"/>, with
    /// their UTC times. The local time stays the same across daylight-saving changes; a local time that does not exist
    /// that day (clocks forward) has no instant (<c>null</c>), and a repeated one means the first occurrence.
    /// </summary>
    public static IEnumerable<(int Index, DateOnly Date, DateTime? StartUtc)> Occurrences(
        DateOnly start, TimeOnly time, int intervalWeeks, DateOnly? endDate, int? count, int fromIndex, DateOnly untilExclusive, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        for (var index = fromIndex; Includes(start, intervalWeeks, endDate, count, index); index++)
        {
            var date = DateOf(start, intervalWeeks, index);
            if (date >= untilExclusive)
            {
                yield break;
            }

            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            yield return (index, date, zone.IsInvalidTime(local) ? null : LessonTime.ToUtc(local, zone, "startTime"));
        }
    }

    /// <summary>The first index whose date is on or after <paramref name="date"/>.</summary>
    public static int FirstIndexOnOrAfter(DateOnly start, int intervalWeeks, DateOnly date) =>
        date <= start ? 0 : (date.DayNumber - start.DayNumber + 7 * intervalWeeks - 1) / (7 * intervalWeeks);
}
