using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Persistence;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>
/// Lessons and the calendar (M07/M08, R2-P4). Every change that occupies time runs in a transaction under the teacher's
/// schedule lock and then checks for overlaps, so two lessons of one teacher never overlap, even under parallel requests.
/// </summary>
internal sealed class LessonService(
    ClassHubDbContext db,
    LessonQueries queries,
    ITenantContext tenant,
    IWorkspaceSettings workspace,
    IStudentLookup students,
    IGroupLookup groups,
    LessonParticipants participantResolver,
    ICatalogLookup catalog,
    BookingService bookings,
    INotificationScheduler notifications,
    TimeProvider clock)
{
    /// <summary>The longest calendar range one request may read.</summary>
    public const int MaxRangeDays = 62;

    /// <summary>Lessons starting on the local days [from, to] in the workspace time zone, in start order.</summary>
    public async Task<IReadOnlyList<LessonResponse>> ListAsync(DateOnly from, DateOnly to, Guid? studentId, Guid? groupId, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxRangeDays)
        {
            throw new BadRequestException($"The range must run forwards and cover at most {MaxRangeDays} days.");
        }

        var zone = await ZoneAsync(cancellationToken);
        var fromUtc = LessonTime.StartOfDayUtc(from, zone);
        var toUtc = LessonTime.StartOfDayUtc(to.AddDays(1), zone);
        var lessons = db.Set<Lesson>().AsNoTracking().Where(l => l.StartUtc >= fromUtc && l.StartUtc < toUtc);
        if (studentId is { } student)
        {
            lessons = lessons.Where(l => db.Set<LessonParticipant>().Any(p => p.LessonId == l.Id && p.StudentId == student));
        }

        if (groupId is { } group)
        {
            lessons = lessons.Where(l => l.GroupId == group);
        }

        return await ToResponsesAsync(await lessons.OrderBy(l => l.StartUtc).ThenBy(l => l.Id).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<LessonResponse> GetAsync(Guid lessonId, CancellationToken cancellationToken) =>
        (await ToResponsesAsync([await LoadAsync(lessonId, tracking: false, cancellationToken)], cancellationToken))[0];

    public async Task<LessonResponse> CreateAsync(SaveLessonRequest request, CancellationToken cancellationToken)
    {
        var teacher = tenant.UserId!.Value;
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var (startUtc, endUtc) = Times(request, TimeZoneInfo.FindSystemTimeZoneById(zoneId));
        await ThrowIfUnknownSubjectAsync(request.SubjectId, null, cancellationToken);
        var participants = await ParticipantsAsync(request, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockScheduleAsync(teacher, cancellationToken);
        await ThrowIfOverlapAsync(teacher, startUtc, endUtc, null, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var lesson = new Lesson
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            TeacherUserId = teacher,
            Title = request.Title!.Trim(),
            TimeZone = zoneId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        Apply(lesson, request, startUtc, endUtc);
        db.Set<Lesson>().Add(lesson);
        db.Set<LessonParticipant>().AddRange(participants.Select(id => new LessonParticipant { WorkspaceId = lesson.WorkspaceId, LessonId = lesson.Id, StudentId = id }));
        await notifications.LessonScheduledAsync(Notice(lesson), participants, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>Changes a scheduled lesson (rescheduling re-checks overlaps; a new student or group replaces the participants).</summary>
    public async Task<LessonResponse> UpdateAsync(Guid lessonId, SaveLessonRequest request, CancellationToken cancellationToken)
    {
        var current = await LoadAsync(lessonId, tracking: false, cancellationToken);
        // The editor shows and sends times in the workspace's current zone (it may have changed since scheduling).
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        // An unchanged start keeps its instant: in a repeated hour the wall-clock value alone cannot tell which one it was.
        var (startUtc, endUtc) = request.StartLocal == LessonTime.ToLocal(current.StartUtc, zone)
            ? (current.StartUtc, current.StartUtc.AddMinutes(request.DurationMinutes!.Value))
            : Times(request, zone);
        await ThrowIfUnknownSubjectAsync(request.SubjectId, current.SubjectId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockScheduleAsync(current.TeacherUserId, cancellationToken);
        var lesson = await LoadForChangeAsync(lessonId, request.Version, cancellationToken);
        if (lesson.Status != LessonStatus.Scheduled)
        {
            throw new BusinessRuleException("Only a scheduled lesson can be changed.", ErrorCodes.InvalidStatusTransition);
        }

        var kind = Enum.Parse<LessonKind>(request.Kind!);
        var who = kind == LessonKind.Group ? request.GroupId : request.StudentId;
        var sameParticipants = kind == lesson.Kind && (lesson.Kind == LessonKind.Group
            ? who == lesson.GroupId
            : await db.Set<LessonParticipant>().AnyAsync(p => p.LessonId == lesson.Id && p.StudentId == who, cancellationToken));
        var before = await db.Set<LessonParticipant>().Where(p => p.LessonId == lesson.Id).Select(p => p.StudentId).ToListAsync(cancellationToken);
        var after = before;
        if (!sameParticipants)
        {
            var participants = await ParticipantsAsync(request, cancellationToken);
            after = [.. participants];
            db.Set<LessonParticipant>().RemoveRange(await db.Set<LessonParticipant>().Where(p => p.LessonId == lesson.Id).ToListAsync(cancellationToken));
            db.Set<LessonParticipant>().AddRange(participants.Select(id => new LessonParticipant { WorkspaceId = lesson.WorkspaceId, LessonId = lesson.Id, StudentId = id }));
        }

        await ThrowIfOverlapAsync(lesson.TeacherUserId, startUtc, endUtc, lesson.Id, cancellationToken);
        lesson.Title = request.Title!.Trim();
        lesson.TimeZone = zoneId;
        // "Only this lesson" (D-24): later edits of its series leave it alone.
        lesson.SeriesException = lesson.SeriesId is not null;
        Apply(lesson, request, startUtc, endUtc);
        Touch(lesson);
        // A booked lesson keeps its booking in step (new time, visitor told) in the same save; otherwise the students are
        // told (R2-P11): removed ones that it is off, new ones that it is on, the others of a new time.
        if (!await bookings.OnLessonChangedAsync(lesson, cancellationToken))
        {
            // Removed students hear about the lesson as they knew it (title and time before this edit), as this change.
            await notifications.LessonCancelledAsync(Notice(current, lesson.Version), [.. before.Except(after)], cancellationToken);
            await notifications.LessonScheduledAsync(Notice(lesson), [.. after.Except(before)], cancellationToken);
            if (lesson.StartUtc != current.StartUtc)
            {
                await notifications.LessonRescheduledAsync(Notice(lesson), [.. after.Intersect(before)], cancellationToken);
            }
        }

        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>
    /// Marks a lesson as held, with each participant's attendance (left out: Present). Idempotent: a held lesson is
    /// returned as it is and never recorded again; parallel completions make one transition and one Finance event.
    /// </summary>
    public async Task<LessonResponse> CompleteAsync(Guid lessonId, CompleteLessonRequest request, CancellationToken cancellationToken)
    {
        var lesson = await LoadAsync(lessonId, tracking: true, cancellationToken);
        if (lesson.Status == LessonStatus.Completed)
        {
            return await GetAsync(lesson.Id, cancellationToken);
        }

        if (lesson.Version != request.Version)
        {
            throw Conflict();
        }

        ThrowIfCannotChange(lesson, LessonStatus.Completed);
        var now = clock.GetUtcNow().UtcDateTime;
        if (lesson.StartUtc > now)
        {
            throw new BusinessRuleException("A lesson can be marked as held once it has started.", ErrorCodes.InvalidStatusTransition);
        }

        var participants = await ApplyAttendanceAsync(lesson, request.Attendance, defaultPresent: true, cancellationToken);
        lesson.Status = LessonStatus.Completed;
        lesson.CompletedAtUtc = now;
        Touch(lesson);
        AddEvent(lesson, LessonEventKinds.Completed, participants, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || SqlServerErrors.IsUniqueViolation(ex))
        {
            // Completed at the same moment elsewhere: the lesson is held once, so this request sees that result.
            db.ChangeTracker.Clear();
            var current = await LoadAsync(lessonId, tracking: false, cancellationToken);
            return current.Status == LessonStatus.Completed ? await GetAsync(lessonId, cancellationToken) : throw Conflict();
        }

        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>Corrects the attendance of a held lesson; Finance is told with an event.</summary>
    public async Task<LessonResponse> UpdateAttendanceAsync(Guid lessonId, AttendanceRequest request, CancellationToken cancellationToken)
    {
        var lesson = await LoadForChangeAsync(lessonId, request.Version, cancellationToken);
        if (lesson.Status != LessonStatus.Completed)
        {
            throw new BusinessRuleException("Attendance is recorded for held lessons.", ErrorCodes.InvalidStatusTransition);
        }

        var participants = await ApplyAttendanceAsync(lesson, request.Attendance, defaultPresent: false, cancellationToken);
        Touch(lesson);
        AddEvent(lesson, LessonEventKinds.AttendanceChanged, participants, clock.GetUtcNow().UtcDateTime);
        await SaveAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>The private note and the shared summary (D-40), in any status.</summary>
    public async Task<LessonResponse> UpdateNotesAsync(Guid lessonId, LessonNotesRequest request, CancellationToken cancellationToken)
    {
        var lesson = await LoadForChangeAsync(lessonId, request.Version, cancellationToken);
        lesson.PrivateNote = Clean(request.PrivateNote);
        lesson.SharedSummary = Clean(request.SharedSummary);
        Touch(lesson);
        await SaveAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>A student's or a group's lessons, newest first (one of the two is required).</summary>
    public async Task<LessonHistoryResponse> HistoryAsync(Guid? studentId, Guid? groupId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if ((studentId is null) == (groupId is null))
        {
            throw new BadRequestException("Choose a student or a group.");
        }

        if (page is < 1 or > 10_000 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("The page must be 1 to 10000 and the page size 1 to 100.");
        }

        // The student or group must be this workspace's (archived ones included): another workspace's is not found.
        var known = studentId is { } sid
            ? (await students.FindAsync([sid], cancellationToken)).ContainsKey(sid)
            : (await groups.FindAsync([groupId!.Value], cancellationToken)).ContainsKey(groupId!.Value);
        if (!known)
        {
            throw new NotFoundException();
        }

        var lessons = db.Set<Lesson>().AsNoTracking();
        lessons = studentId is { } student
            ? lessons.Where(l => db.Set<LessonParticipant>().Any(p => p.LessonId == l.Id && p.StudentId == student))
            : lessons.Where(l => l.GroupId == groupId);
        var total = await lessons.CountAsync(cancellationToken);
        var rows = await lessons.OrderByDescending(l => l.StartUtc).ThenBy(l => l.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var attendance = studentId is { } who
            ? await db.Set<LessonParticipant>().AsNoTracking().Where(p => ids.Contains(p.LessonId) && p.StudentId == who)
                .ToDictionaryAsync(p => p.LessonId, p => p.Attendance, cancellationToken)
            : [];
        var groupNames = await groups.FindAsync([.. rows.Where(l => l.GroupId is not null).Select(l => l.GroupId!.Value).Distinct()], cancellationToken);
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return new LessonHistoryResponse(
            [.. rows.Select(l => new LessonHistoryItem(l.Id, l.Title, l.Status.ToString(), l.StartUtc, LessonTime.ToLocal(l.StartUtc, zone), zoneId,
                l.GroupId is { } g && groupNames.TryGetValue(g, out var group) ? group.Name : null,
                attendance.TryGetValue(l.Id, out var a) ? a?.ToString() : null))],
            total, page, pageSize);
    }

    /// <summary>Sets the participants' attendance from the request (only the lesson's participants may be named).</summary>
    private async Task<IReadOnlyList<LessonParticipant>> ApplyAttendanceAsync(Lesson lesson, IReadOnlyList<AttendanceItem>? items, bool defaultPresent, CancellationToken cancellationToken)
    {
        var participants = await db.Set<LessonParticipant>().Where(p => p.LessonId == lesson.Id).ToListAsync(cancellationToken);
        foreach (var item in items ?? [])
        {
            var participant = participants.SingleOrDefault(p => p.StudentId == item.StudentId) ?? throw Invalid("attendance", "Only the lesson's students have attendance.");
            participant.Attendance = Enum.Parse<AttendanceStatus>(item.Status!);
        }

        if (defaultPresent)
        {
            foreach (var participant in participants.Where(p => p.Attendance is null))
            {
                participant.Attendance = AttendanceStatus.Present;
            }
        }

        return participants;
    }

    private void AddEvent(Lesson lesson, string kind, IReadOnlyList<LessonParticipant> participants, DateTime now) =>
        db.Set<LessonEvent>().Add(new LessonEvent
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = lesson.WorkspaceId,
            LessonId = lesson.Id,
            Kind = kind,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                lessonId = lesson.Id,
                completedAtUtc = lesson.CompletedAtUtc,
                attendance = participants.Select(p => new { studentId = p.StudentId, status = p.Attendance?.ToString() }),
            }),
            OccurredAtUtc = now,
        });

    /// <summary>Cancels a scheduled lesson with a reason; its time is free again and it never counts as held.</summary>
    public async Task<LessonResponse> CancelAsync(Guid lessonId, CancelLessonRequest request, CancellationToken cancellationToken)
    {
        var lesson = await LoadForChangeAsync(lessonId, request.Version, cancellationToken);
        ThrowIfCannotChange(lesson, LessonStatus.Cancelled);
        lesson.Status = LessonStatus.Cancelled;
        lesson.CancellationReason = request.Reason!.Trim();
        lesson.CancelledAtUtc = clock.GetUtcNow().UtcDateTime;
        Touch(lesson);
        if (!await bookings.OnLessonChangedAsync(lesson, cancellationToken))
        {
            // Pending reminders are withdrawn; students who were told about the lesson hear it is off.
            var participants = await db.Set<LessonParticipant>().Where(p => p.LessonId == lesson.Id).Select(p => p.StudentId).ToListAsync(cancellationToken);
            await notifications.LessonCancelledAsync(Notice(lesson), participants, cancellationToken);
        }

        await SaveAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    private static (DateTime StartUtc, DateTime EndUtc) Times(SaveLessonRequest request, TimeZoneInfo zone)
    {
        var start = LessonTime.ToUtc(request.StartLocal!.Value, zone, "startLocal");
        return (start, start.AddMinutes(request.DurationMinutes!.Value));
    }

    private static void Apply(Lesson lesson, SaveLessonRequest request, DateTime startUtc, DateTime endUtc)
    {
        lesson.Kind = Enum.Parse<LessonKind>(request.Kind!);
        lesson.GroupId = lesson.Kind == LessonKind.Group ? request.GroupId : null;
        lesson.SubjectId = request.SubjectId;
        lesson.Topic = Clean(request.Topic);
        lesson.StartUtc = startUtc;
        lesson.EndUtc = endUtc;
        lesson.Mode = Enum.Parse<LessonMode>(request.Mode!);
        lesson.MeetingUrl = lesson.Mode == LessonMode.Online ? Clean(request.MeetingUrl) : null;
        lesson.Location = lesson.Mode == LessonMode.InPerson ? Clean(request.Location) : null;
    }

    private Task<IReadOnlyList<Guid>> ParticipantsAsync(SaveLessonRequest request, CancellationToken cancellationToken) =>
        participantResolver.RequireAsync(Enum.Parse<LessonKind>(request.Kind!), request.StudentId, request.GroupId, cancellationToken);

    private async Task ThrowIfOverlapAsync(Guid teacher, DateTime startUtc, DateTime endUtc, Guid? except, CancellationToken cancellationToken)
    {
        if (await queries.OverlapsAsync(teacher, startUtc, endUtc, except, cancellationToken))
        {
            throw new ConflictException("Another lesson is already scheduled at this time.", ErrorCodes.LessonOverlap);
        }
    }

    private async Task ThrowIfUnknownSubjectAsync(Guid? wanted, Guid? saved, CancellationToken cancellationToken)
    {
        // A subject the ClassHub team deactivated stays valid on the lesson that already has it.
        if (wanted is { } id && id != saved && (await catalog.UnknownSubjectsAsync([id], cancellationToken)).Count > 0)
        {
            throw Invalid("subjectId", "Unknown subject.");
        }
    }

    private static void ThrowIfCannotChange(Lesson lesson, LessonStatus to)
    {
        if (!LessonRules.CanChange(lesson.Status, to))
        {
            throw new BusinessRuleException($"A {lesson.Status.ToString().ToLowerInvariant()} lesson cannot change any more.", ErrorCodes.InvalidStatusTransition);
        }
    }

    /// <summary>The lesson as notifications record it; its version (after the change) tells changes apart.</summary>
    internal static LessonNotice Notice(Lesson lesson, int? revision = null) =>
        new(lesson.Id, revision ?? lesson.Version, lesson.Title, lesson.StartUtc, lesson.TimeZone);

    private void Touch(Lesson lesson)
    {
        lesson.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        lesson.Version++;
    }

    private async Task<Lesson> LoadAsync(Guid lessonId, bool tracking, CancellationToken cancellationToken)
    {
        var lessons = tracking ? db.Set<Lesson>() : db.Set<Lesson>().AsNoTracking();
        return await lessons.SingleOrDefaultAsync(l => l.Id == lessonId, cancellationToken) ?? throw new NotFoundException();
    }

    private async Task<Lesson> LoadForChangeAsync(Guid lessonId, int? version, CancellationToken cancellationToken)
    {
        var lesson = await LoadAsync(lessonId, tracking: true, cancellationToken);
        return lesson.Version == version ? lesson : throw Conflict();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken cancellationToken) =>
        TimeZoneInfo.FindSystemTimeZoneById(await workspace.GetTimeZoneAsync(cancellationToken));

    private async Task<IReadOnlyList<LessonResponse>> ToResponsesAsync(IReadOnlyList<Lesson> lessons, CancellationToken cancellationToken)
    {
        var ids = lessons.Select(l => l.Id).ToList();
        var participants = (await db.Set<LessonParticipant>().AsNoTracking().Where(p => ids.Contains(p.LessonId)).ToListAsync(cancellationToken))
            .ToLookup(p => p.LessonId);
        var names = await students.FindAsync([.. participants.SelectMany(p => p).Select(p => p.StudentId).Distinct()], cancellationToken);
        var groupNames = await groups.FindAsync([.. lessons.Where(l => l.GroupId is not null).Select(l => l.GroupId!.Value).Distinct()], cancellationToken);
        // Local times are always in the workspace's current zone, the same one the calendar reads days in.
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return [.. lessons.Select(l =>
            new LessonResponse(
                l.Id, l.Kind.ToString(), l.GroupId, l.GroupId is { } g && groupNames.TryGetValue(g, out var group) ? group.Name : null,
                [.. participants[l.Id].Where(p => names.ContainsKey(p.StudentId))
                    .Select(p => new LessonParticipantResponse(p.StudentId, names[p.StudentId].FirstName, names[p.StudentId].LastName, p.Attendance?.ToString()))
                    .OrderBy(p => p.LastName ?? p.FirstName).ThenBy(p => p.FirstName)],
                l.SubjectId, l.Title, l.Topic, l.StartUtc, l.EndUtc, LessonTime.ToLocal(l.StartUtc, zone), LessonTime.ToLocal(l.EndUtc, zone), zoneId,
                l.Mode.ToString(), l.MeetingUrl, l.Location, l.Status.ToString(), l.CancellationReason, l.CreatedAtUtc, l.UpdatedAtUtc, l.Version,
                l.SeriesId, l.SeriesIndex, l.SeriesException, l.PrivateNote, l.SharedSummary))];
    }

    private static ConflictException Conflict() =>
        new("The lesson was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
