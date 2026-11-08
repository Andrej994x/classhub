namespace ClassHub.Core.Modules.Lessons.Contracts;

public enum LessonKind
{
    Individual = 1,
    Group = 2,
}

public enum LessonMode
{
    Online = 1,
    InPerson = 2,
}

/// <summary>Why an occurrence of a series was not created.</summary>
public enum SeriesSkipReason
{
    /// <summary>Another lesson of the teacher overlaps it.</summary>
    Overlap = 1,

    /// <summary>The local time does not exist that day (the clocks move forward).</summary>
    TimeDoesNotExist = 2,

    /// <summary>The student or group is archived, or the group has no students.</summary>
    Unavailable = 3,
}

/// <summary>D-22: Scheduled → Completed or Cancelled. Both end states are final; a cancelled lesson never counts as held.</summary>
public enum LessonStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>The lesson entity, for database foreign keys from other modules (attendance, materials, packages later).</summary>
public static class LessonForeignKeyTargets
{
    public static readonly Type Lesson = typeof(Domain.Lesson);
}

public sealed record LessonParticipantResponse(Guid StudentId, string FirstName, string? LastName);

/// <summary>
/// A lesson (kind, mode and status are the enum names). <c>StartLocal</c>/<c>EndLocal</c> are wall-clock times in <c>TimeZone</c>, the
/// workspace's current zone (the calendar reads days in it); <c>StartUtc</c>/<c>EndUtc</c> are the instants.
/// </summary>
public sealed record LessonResponse(
    Guid Id,
    string Kind,
    Guid? GroupId,
    string? GroupName,
    IReadOnlyList<LessonParticipantResponse> Participants,
    Guid? SubjectId,
    string Title,
    string? Topic,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime StartLocal,
    DateTime EndLocal,
    string TimeZone,
    string Mode,
    string? MeetingUrl,
    string? Location,
    string Status,
    string? CancellationReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version,
    Guid? SeriesId,
    int? SeriesIndex,
    bool SeriesException);

/// <summary>
/// Schedule (no version) or change a scheduled lesson (the version that was loaded). The start is a wall-clock time in
/// the workspace time zone; a student (individual) or a group is required.
/// </summary>
public sealed record SaveLessonRequest(
    string? Kind,
    Guid? StudentId,
    Guid? GroupId,
    Guid? SubjectId,
    string? Title,
    string? Topic,
    DateTime? StartLocal,
    int? DurationMinutes,
    string? Mode,
    string? MeetingUrl,
    string? Location,
    int? Version);

public sealed record LessonVersionRequest(int? Version);

public sealed record CancelLessonRequest(int? Version, string? Reason);

/// <summary>
/// Set up recurring lessons: every <c>IntervalWeeks</c> (1 or 2) from <c>StartDate</c> at <c>StartTime</c> (workspace
/// time zone), ending on <c>EndDate</c>, after <c>Count</c> lessons, or not at all (at most one of the two).
/// </summary>
public sealed record SaveSeriesRequest(
    string? Kind,
    Guid? StudentId,
    Guid? GroupId,
    Guid? SubjectId,
    string? Title,
    string? Topic,
    DateOnly? StartDate,
    TimeOnly? StartTime,
    int? DurationMinutes,
    int? IntervalWeeks,
    DateOnly? EndDate,
    int? Count,
    string? Mode,
    string? MeetingUrl,
    string? Location);

/// <summary>
/// D-24 edits. <c>ThisAndFollowing</c> starts at <c>FromLessonId</c>; <c>AllFuture</c> at the next scheduled occurrence.
/// The series ends there and a new one with <c>Series</c> continues; held, cancelled and individually changed lessons stay.
/// <c>TimeZone</c> is the zone the form showed the times in: the series' own or the workspace's current one (default).
/// </summary>
public sealed record UpdateSeriesRequest(string? Scope, Guid? FromLessonId, SaveSeriesRequest? Series, int? Version, string? TimeZone);

/// <summary>End the series: no occurrences on or after <c>FromDate</c> (scheduled ones are removed).</summary>
public sealed record EndSeriesRequest(DateOnly? FromDate, int? Version);

public sealed record SeriesSkipResponse(DateOnly Date, string Reason);

public sealed record SeriesResponse(
    Guid Id,
    string Kind,
    Guid? StudentId,
    Guid? GroupId,
    Guid? SubjectId,
    string Title,
    string? Topic,
    DateOnly StartDate,
    TimeOnly StartTime,
    int DurationMinutes,
    int IntervalWeeks,
    DateOnly? EndDate,
    int? Count,
    string Mode,
    string? MeetingUrl,
    string? Location,
    string TimeZone,
    Guid? ReplacedBySeriesId,
    IReadOnlyList<SeriesSkipResponse> Skips,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version);

/// <summary>The series after a create or edit, how many lessons were created now, and the occurrences that were skipped now.</summary>
public sealed record SeriesResultResponse(SeriesResponse Series, int Created, IReadOnlyList<SeriesSkipResponse> Conflicts);
