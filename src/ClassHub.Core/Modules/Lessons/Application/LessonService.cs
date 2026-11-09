using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Persistence;
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
        if (!sameParticipants)
        {
            var participants = await ParticipantsAsync(request, cancellationToken);
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
        // A booked lesson keeps its booking in step (new time, visitor told) in the same save.
        await bookings.OnLessonChangedAsync(lesson, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>Marks a lesson as held. Only a scheduled lesson that has started; a held lesson is never recorded again.</summary>
    public async Task<LessonResponse> CompleteAsync(Guid lessonId, LessonVersionRequest request, CancellationToken cancellationToken)
    {
        var lesson = await LoadForChangeAsync(lessonId, request.Version, cancellationToken);
        ThrowIfCannotChange(lesson, LessonStatus.Completed);
        var now = clock.GetUtcNow().UtcDateTime;
        if (lesson.StartUtc > now)
        {
            throw new BusinessRuleException("A lesson can be marked as held once it has started.", ErrorCodes.InvalidStatusTransition);
        }

        lesson.Status = LessonStatus.Completed;
        lesson.CompletedAtUtc = now;
        Touch(lesson);
        await SaveAsync(cancellationToken);
        return await GetAsync(lesson.Id, cancellationToken);
    }

    /// <summary>Cancels a scheduled lesson with a reason; its time is free again and it never counts as held.</summary>
    public async Task<LessonResponse> CancelAsync(Guid lessonId, CancelLessonRequest request, CancellationToken cancellationToken)
    {
        var lesson = await LoadForChangeAsync(lessonId, request.Version, cancellationToken);
        ThrowIfCannotChange(lesson, LessonStatus.Cancelled);
        lesson.Status = LessonStatus.Cancelled;
        lesson.CancellationReason = request.Reason!.Trim();
        lesson.CancelledAtUtc = clock.GetUtcNow().UtcDateTime;
        Touch(lesson);
        await bookings.OnLessonChangedAsync(lesson, cancellationToken);
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
            .ToLookup(p => p.LessonId, p => p.StudentId);
        var names = await students.FindAsync([.. participants.SelectMany(p => p).Distinct()], cancellationToken);
        var groupNames = await groups.FindAsync([.. lessons.Where(l => l.GroupId is not null).Select(l => l.GroupId!.Value).Distinct()], cancellationToken);
        // Local times are always in the workspace's current zone, the same one the calendar reads days in.
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return [.. lessons.Select(l =>
            new LessonResponse(
                l.Id, l.Kind.ToString(), l.GroupId, l.GroupId is { } g && groupNames.TryGetValue(g, out var group) ? group.Name : null,
                [.. participants[l.Id].Where(names.ContainsKey).Select(id => new LessonParticipantResponse(id, names[id].FirstName, names[id].LastName))
                    .OrderBy(p => p.LastName ?? p.FirstName).ThenBy(p => p.FirstName)],
                l.SubjectId, l.Title, l.Topic, l.StartUtc, l.EndUtc, LessonTime.ToLocal(l.StartUtc, zone), LessonTime.ToLocal(l.EndUtc, zone), zoneId,
                l.Mode.ToString(), l.MeetingUrl, l.Location, l.Status.ToString(), l.CancellationReason, l.CreatedAtUtc, l.UpdatedAtUtc, l.Version,
                l.SeriesId, l.SeriesIndex, l.SeriesException))];
    }

    private static ConflictException Conflict() =>
        new("The lesson was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
