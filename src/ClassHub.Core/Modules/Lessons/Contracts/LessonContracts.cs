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
    int Version);

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
