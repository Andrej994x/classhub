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

/// <summary>A visitor's booking request (R2-P6, D-23).</summary>
public enum BookingStatus
{
    Pending = 1,
    Confirmed = 2,
    Declined = 3,
    Cancelled = 4,
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

/// <summary>D-22: attendance per participant of a held lesson.</summary>
public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    Excused = 3,
}

/// <summary>Kinds of <c>LessonEvents</c> (for Finance, R2-P10).</summary>
public static class LessonEventKinds
{
    public const string Completed = "LessonCompleted";
    public const string AttendanceChanged = "AttendanceChanged";
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

/// <summary>A participant; <c>Attendance</c> (Present/Absent/Excused) once the lesson is held.</summary>
public sealed record LessonParticipantResponse(Guid StudentId, string FirstName, string? LastName, string? Attendance);

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
    bool SeriesException,
    string? PrivateNote,
    string? SharedSummary);

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

public sealed record AttendanceItem(Guid StudentId, string? Status);

/// <summary>
/// Marks a lesson held (idempotent: a held lesson is returned as it is). Participants left out are Present; the version
/// is the loaded one.
/// </summary>
public sealed record CompleteLessonRequest(int? Version, IReadOnlyList<AttendanceItem>? Attendance);

/// <summary>Corrects the attendance of a held lesson (Finance is told, R2-P10).</summary>
public sealed record AttendanceRequest(int? Version, IReadOnlyList<AttendanceItem>? Attendance);

public sealed record LessonNotesRequest(int? Version, string? PrivateNote, string? SharedSummary);

/// <summary>A lesson in a student's or group's history; <c>Attendance</c> is the student's when filtered by student.</summary>
public sealed record LessonHistoryItem(Guid Id, string Title, string Status, DateTime StartUtc, DateTime StartLocal, string TimeZone, string? GroupName, string? Attendance);

public sealed record LessonHistoryResponse(IReadOnlyList<LessonHistoryItem> Items, int Total, int Page, int PageSize);

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

// --- Booking (R2-P6) ---

public sealed record WorkingHoursItem(DayOfWeek DayOfWeek, TimeOnly Start, TimeOnly End);

public sealed record TimeOffItem(DateOnly StartDate, DateOnly EndDate, string? Note);

/// <summary>Self-service booking settings; working hours and days off are in <c>TimeZone</c> (the workspace's).</summary>
public sealed record BookingSettingsResponse(
    bool Enabled,
    int SlotStepMinutes,
    int MinNoticeHours,
    int HorizonDays,
    IReadOnlyList<WorkingHoursItem> WorkingHours,
    IReadOnlyList<TimeOffItem> TimeOff,
    string TimeZone,
    int Version);

public sealed record SaveBookingSettingsRequest(
    bool Enabled,
    int? SlotStepMinutes,
    int? MinNoticeHours,
    int? HorizonDays,
    IReadOnlyList<WorkingHoursItem>? WorkingHours,
    IReadOnlyList<TimeOffItem>? TimeOff,
    int? Version);

/// <summary>A booking request for the teacher (contact details from its request; local times in the workspace zone).</summary>
public sealed record BookingRequestResponse(
    Guid Id,
    Guid LeadId,
    string Name,
    string Email,
    string? Phone,
    Guid OfferId,
    string OfferTitle,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime StartLocal,
    DateTime EndLocal,
    string TimeZone,
    string Status,
    Guid? LessonId,
    string? Reason,
    DateTime CreatedAtUtc,
    int Version);

/// <summary>One page of booking requests: pending first, then by time.</summary>
public sealed record BookingRequestListResponse(IReadOnlyList<BookingRequestResponse> Items, int Total, int Page, int PageSize);

public sealed record BookingDecisionRequest(int? Version, string? Reason);

/// <summary>Cancel a confirmed booking (its lesson is cancelled with the reason; the visitor is told).</summary>
public sealed record CancelBookingRequest(int? Version, string? Reason);

/// <summary>Move a confirmed booking (its lesson) to a new wall-clock start in the workspace zone.</summary>
public sealed record RescheduleBookingRequest(int? Version, DateTime? StartLocal);

/// <summary>What the public booking page offers (R2-P6): bookable offers and the zone all times are in.</summary>
public sealed record PublicBookingResponse(bool Available, string TimeZone, int HorizonDays, IReadOnlyList<PublicBookingOffer> Offers);

public sealed record PublicBookingOffer(Guid Id, string Title, int DurationMinutes, string Mode);

public sealed record PublicSlot(DateTime StartUtc, string Time);

public sealed record PublicSlotDay(DateOnly Date, IReadOnlyList<PublicSlot> Slots);

public sealed record PublicSlotsResponse(string TimeZone, IReadOnlyList<PublicSlotDay> Days);

/// <summary>For the public request form (Leads module): a requested slot is checked and then recorded with the request.</summary>
public interface IBookingIntake
{
    /// <summary>
    /// The offer if <paramref name="startUtc"/> is a free slot of a bookable offer of the current (public, read-only)
    /// workspace right now; otherwise a validation error on <c>slotStartUtc</c>.
    /// </summary>
    Task<BookableOffer> CheckAsync(Guid offerId, DateTime startUtc, CancellationToken cancellationToken);

    /// <summary>Records the booking request for the lead in the caller's unit of work (explicit workspace; it saves).</summary>
    Task AddAsync(Guid workspaceId, Guid leadId, BookableOffer offer, DateTime startUtc, DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed record BookableOffer(Guid Id, string Title, Guid? SubjectId, int DurationMinutes, string Mode, string TimeZone);

/// <summary>For the public site page: whether the workspace takes bookings (the page links to <c>/{slug}/book</c>).</summary>
public interface IBookingAvailability
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}

/// <summary>A lesson for other modules (homework linked to a lesson).</summary>
public sealed record LessonSummary(Guid Id, string Title, DateTime StartUtc, string TimeZone, string Status);

/// <summary>A participant of a lesson and whether they attended (Present).</summary>
public sealed record LessonPresence(Guid StudentId, bool Present);

/// <summary>A lesson's current state for Finance: held or not, and who was present.</summary>
public sealed record LessonAttendanceFacts(Guid LessonId, string Title, DateTime StartUtc, bool Held, IReadOnlyList<LessonPresence> Participants);

/// <summary>Lessons of the current workspace, for other modules.</summary>
public interface ILessonLookup
{
    Task<IReadOnlyDictionary<Guid, LessonSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The lesson's current attendance (null when it is not a lesson of the current workspace).</summary>
    Task<LessonAttendanceFacts?> AttendanceAsync(Guid lessonId, CancellationToken cancellationToken);

    /// <summary>The series' next scheduled lesson starting after <paramref name="afterUtc"/> that the student takes part in.</summary>
    Task<LessonSummary?> NextInSeriesAsync(Guid seriesId, Guid studentId, DateTime afterUtc, CancellationToken cancellationToken);
}

/// <summary>A lesson ahead for the teacher's dashboard (R2-P12), with its students.</summary>
public sealed record LessonAhead(Guid Id, string Title, DateTime StartUtc, DateTime EndUtc, string TimeZone, LessonKind Kind, IReadOnlyList<Guid> StudentIds);

/// <summary>The scheduled lessons of a window: how many, and the first few in start order.</summary>
public sealed record LessonsAhead(int Count, IReadOnlyList<LessonAhead> First);

/// <summary>The current workspace's schedule ahead (R2-P12 dashboard).</summary>
public interface ILessonOutlook
{
    /// <summary>Scheduled lessons not over at <paramref name="nowUtc"/> that start before <paramref name="untilUtc"/>.</summary>
    Task<LessonsAhead> AheadAsync(DateTime nowUtc, DateTime untilUtc, int max, CancellationToken cancellationToken);
}

/// <summary>
/// A lesson as its student sees it in the portal (R2-P12): never the teacher's private note or the other students. The
/// meeting link only while the lesson is ahead; attendance and the shared summary once it is held.
/// </summary>
public sealed record StudentLessonResponse(
    Guid Id,
    string Title,
    string? Topic,
    DateTime StartUtc,
    DateTime EndUtc,
    string TimeZone,
    LessonMode Mode,
    string? MeetingUrl,
    string? Location,
    LessonStatus Status,
    AttendanceStatus? Attendance,
    string? SharedSummary);

/// <summary>The student's own attendance over their held lessons.</summary>
public sealed record StudentAttendanceResponse(int Held, int Present, int Absent, int Excused);

/// <summary>The student's lessons with one teacher: the next ones, the most recent past ones, and their attendance.</summary>
public sealed record StudentLessonsResponse(IReadOnlyList<StudentLessonResponse> Upcoming, IReadOnlyList<StudentLessonResponse> Past, StudentAttendanceResponse Attendance);

/// <summary>A scheduled lesson starting soon, with its students (reminders, R2-P11).</summary>
public sealed record UpcomingLesson(Guid LessonId, Guid WorkspaceId, Guid TeacherUserId, DateTime StartUtc, IReadOnlyList<Guid> StudentIds);

/// <summary>Scheduled lessons across workspaces, for the reminder planner.</summary>
public interface IUpcomingLessons
{
    /// <summary>
    /// System scope (every workspace, identifiers only): scheduled lessons starting in [fromUtc, toUtc), in start order,
    /// after <paramref name="after"/> (the last lesson of the previous page), at most <paramref name="max"/>.
    /// </summary>
    Task<IReadOnlyList<UpcomingLesson>> ScheduledBetweenAsync(DateTime fromUtc, DateTime toUtc, (DateTime StartUtc, Guid LessonId)? after, int max, CancellationToken cancellationToken);
}

/// <summary>An unprocessed lesson event (the Finance outbox, R2-P7/R2-P10).</summary>
public sealed record PendingLessonEvent(Guid Id, Guid WorkspaceId, Guid LessonId, Guid TeacherUserId, string Kind, DateTime OccurredAtUtc);

/// <summary>The lesson events Finance consumes.</summary>
public interface ILessonEventFeed
{
    /// <summary>System scope (every workspace, identifiers only): the oldest unprocessed events.</summary>
    Task<IReadOnlyList<PendingLessonEvent>> PendingAsync(int max, CancellationToken cancellationToken);

    /// <summary>
    /// In the current workspace and the caller's transaction: marks the event processed; false when it already was
    /// (another worker handled it).
    /// </summary>
    Task<bool> MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken);
}
