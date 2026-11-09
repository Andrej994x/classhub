using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Persistence;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>
/// Recurring lessons (R2-P5, D-24). Every change runs in one transaction under the teacher's schedule lock: occurrences
/// are created as ordinary lessons up to the horizon, each checked for overlaps; one that cannot be created is skipped
/// and recorded. Edits of "this and following" or "all future" end the series and continue with a new one; held,
/// cancelled and individually changed lessons are never touched by series edits.
/// </summary>
internal sealed class LessonSeriesService(
    ClassHubDbContext db,
    LessonQueries queries,
    ITenantContext tenant,
    IWorkspaceSettings workspace,
    LessonParticipants participants,
    ICatalogLookup catalog,
    INotificationScheduler notifications,
    TimeProvider clock)
{
    public const string ThisAndFollowing = "ThisAndFollowing";
    public const string AllFuture = "AllFuture";

    public async Task<SeriesResultResponse> CreateAsync(SaveSeriesRequest request, CancellationToken cancellationToken)
    {
        var teacher = tenant.UserId!.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        ThrowIfStartsInThePast(request, Today(zone, now));
        await ThrowIfUnknownSubjectAsync(request.SubjectId, null, cancellationToken);
        await participants.RequireAsync(Enum.Parse<LessonKind>(request.Kind!), request.StudentId, request.GroupId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockScheduleAsync(teacher, cancellationToken);
        var series = NewSeries(request, teacher, zoneId, now);
        db.Set<LessonSeries>().Add(series);
        await db.SaveChangesAsync(cancellationToken);
        var (created, conflicts) = await MaterializeAsync(series, now, cancellationToken, announce: true);
        await transaction.CommitAsync(cancellationToken);
        return new SeriesResultResponse(await GetAsync(series.Id, cancellationToken), created, conflicts);
    }

    public async Task<SeriesResponse> GetAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        var series = await db.Set<LessonSeries>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == seriesId, cancellationToken) ?? throw new NotFoundException();
        var skips = await db.Set<LessonSeriesSkip>().AsNoTracking().Where(s => s.SeriesId == seriesId).OrderBy(s => s.SeriesIndex)
            .Select(s => new { s.Date, s.Reason }).ToListAsync(cancellationToken);
        return new SeriesResponse(series.Id, series.Kind.ToString(), series.StudentId, series.GroupId, series.SubjectId, series.Title, series.Topic,
            series.StartDate, series.StartTime, series.DurationMinutes, series.IntervalWeeks, series.EndDate, series.Count, series.Mode.ToString(),
            series.MeetingUrl, series.Location, series.TimeZone, series.ReplacedBySeriesId,
            [.. skips.Select(s => new SeriesSkipResponse(s.Date, s.Reason.ToString()))], series.CreatedAtUtc, series.UpdatedAtUtc, series.Version);
    }

    /// <summary>D-24: the series ends where the edit starts and a new series with the new settings continues.</summary>
    public async Task<SeriesResultResponse> UpdateAsync(Guid seriesId, UpdateSeriesRequest request, CancellationToken cancellationToken)
    {
        var settings = request.Series!;
        var now = clock.GetUtcNow().UtcDateTime;
        var saved = await FindAsync(seriesId, cancellationToken);
        // The times are read in the zone the form showed them in, so an unchanged time keeps its instants.
        var workspaceZone = await workspace.GetTimeZoneAsync(cancellationToken);
        var zoneId = request.TimeZone ?? workspaceZone;
        if (zoneId != workspaceZone && zoneId != saved.TimeZone)
        {
            throw Invalid("timeZone", "Use the series' time zone or the workspace's.");
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        ThrowIfStartsInThePast(settings, Today(zone, now));
        await ThrowIfUnknownSubjectAsync(settings.SubjectId, saved.SubjectId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var teacher = (await FindAsync(seriesId, cancellationToken)).TeacherUserId;
        await queries.LockScheduleAsync(teacher, cancellationToken);
        var old = await LoadForChangeAsync(seriesId, request.Version, cancellationToken);

        // The cut is an instant, so the series' and the form's calendars never mix: from the selected lesson (or, for all
        // future lessons, from now), earlier if the new series starts on an earlier day, and never before now.
        var from = now;
        if (request.Scope == ThisAndFollowing)
        {
            var lesson = await db.Set<Lesson>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == request.FromLessonId && l.SeriesId == old.Id, cancellationToken)
                ?? throw Invalid("fromLessonId", "The lesson is not part of this series.");
            from = lesson.StartUtc;
        }

        from = Latest(Earliest(from, LessonTime.StartOfDayUtc(settings.StartDate!.Value, zone)), now);
        await participants.RequireAsync(Enum.Parse<LessonKind>(settings.Kind!), settings.StudentId, settings.GroupId, cancellationToken);
        await RemoveFromAsync(old, from, cancellationToken);

        var next = NewSeries(settings, teacher, zoneId, now);
        db.Set<LessonSeries>().Add(next);
        old.ReplacedBySeriesId = next.Id;
        Touch(old, now);
        await SaveAsync(cancellationToken);
        var (created, conflicts) = await MaterializeAsync(next, now, cancellationToken, announce: true);
        await transaction.CommitAsync(cancellationToken);
        return new SeriesResultResponse(await GetAsync(next.Id, cancellationToken), created, conflicts);
    }

    /// <summary>Ends the series: no occurrences on or after the date (scheduled ones are removed, the past stays).</summary>
    public async Task<SeriesResponse> EndAsync(Guid seriesId, EndSeriesRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await queries.LockScheduleAsync((await FindAsync(seriesId, cancellationToken)).TeacherUserId, cancellationToken);
        var series = await LoadForChangeAsync(seriesId, request.Version, cancellationToken);
        // The date is in the series' own calendar; a lesson that already started stays.
        var from = Latest(LessonTime.StartOfDayUtc(request.FromDate!.Value, TimeZoneInfo.FindSystemTimeZoneById(series.TimeZone)), now);
        await RemoveFromAsync(series, from, cancellationToken);
        Touch(series, now);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(series.Id, cancellationToken);
    }

    /// <summary>
    /// The horizon job: creates the occurrences of a series that are now within the horizon. Runs in the series' own
    /// workspace (the caller binds the tenant), under the teacher's schedule lock.
    /// </summary>
    public async Task<int> ExtendAsync(Guid seriesId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var teacher = (await FindAsync(seriesId, cancellationToken)).TeacherUserId;
        await queries.LockScheduleAsync(teacher, cancellationToken);
        var series = await db.Set<LessonSeries>().SingleAsync(s => s.Id == seriesId, cancellationToken);
        if (series.ReplacedBySeriesId is not null)
        {
            return 0;
        }

        var (created, _) = await MaterializeAsync(series, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    /// <summary>Creates (or skips) every occurrence from <c>NextIndex</c> whose date is within the horizon.</summary>
    /// <summary>
    /// Creates the series' lessons up to the horizon. <paramref name="announce"/>: a new or replaced series is announced to
    /// its students once (each lesson then gets its reminder); extending to the horizon announces nothing.
    /// </summary>
    private async Task<(int Created, IReadOnlyList<SeriesSkipResponse> Conflicts)> MaterializeAsync(LessonSeries series, DateTime nowUtc, CancellationToken cancellationToken, bool announce = false)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(series.TimeZone);
        var until = LessonSeries.HorizonEnd(zone, nowUtc);
        var occurrences = SeriesPattern.Occurrences(series.StartDate, series.StartTime, series.IntervalWeeks, series.EndDate, series.Count, series.NextIndex, until, zone).ToList();
        if (occurrences.Count == 0)
        {
            return (0, []);
        }

        var (people, problem) = await participants.ResolveAsync(series.Kind, series.StudentId, series.GroupId, cancellationToken);
        var created = 0;
        Lesson? first = null;
        var conflicts = new List<SeriesSkipResponse>();
        foreach (var (index, date, startUtc) in occurrences)
        {
            SeriesSkipReason? reason = startUtc is null ? SeriesSkipReason.TimeDoesNotExist
                : problem != LessonParticipants.Problem.None ? SeriesSkipReason.Unavailable
                : await queries.OverlapsAsync(series.TeacherUserId, startUtc.Value, startUtc.Value.AddMinutes(series.DurationMinutes), null, cancellationToken) ? SeriesSkipReason.Overlap
                : null;
            if (reason is { } skipped)
            {
                db.Set<LessonSeriesSkip>().Add(new LessonSeriesSkip { WorkspaceId = series.WorkspaceId, SeriesId = series.Id, SeriesIndex = index, Date = date, Reason = skipped });
                conflicts.Add(new SeriesSkipResponse(date, skipped.ToString()));
            }
            else
            {
                var lesson = new Lesson
                {
                    Id = Guid.CreateVersion7(),
                    WorkspaceId = series.WorkspaceId,
                    TeacherUserId = series.TeacherUserId,
                    Kind = series.Kind,
                    GroupId = series.GroupId,
                    SubjectId = series.SubjectId,
                    Title = series.Title,
                    Topic = series.Topic,
                    StartUtc = startUtc!.Value,
                    EndUtc = startUtc.Value.AddMinutes(series.DurationMinutes),
                    TimeZone = series.TimeZone,
                    Mode = series.Mode,
                    MeetingUrl = series.MeetingUrl,
                    Location = series.Location,
                    SeriesId = series.Id,
                    SeriesIndex = index,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                };
                db.Set<Lesson>().Add(lesson);
                db.Set<LessonParticipant>().AddRange(people.Select(id => new LessonParticipant { WorkspaceId = series.WorkspaceId, LessonId = lesson.Id, StudentId = id }));
                first ??= lesson;
                created++;
            }

            series.NextIndex = index + 1;
        }

        if (announce && first is not null)
        {
            await notifications.SeriesScheduledAsync(series.Id, LessonService.Notice(first), people, cancellationToken);
        }

        await SaveAsync(cancellationToken);
        return (created, conflicts);
    }

    /// <summary>
    /// Ends a series at the instant <paramref name="fromUtc"/>: its scheduled, unchanged lessons starting then or later and
    /// its skips from that local day on go; the end date becomes the local day of the last moment before the cut.
    /// </summary>
    private async Task RemoveFromAsync(LessonSeries series, DateTime fromUtc, CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(series.TimeZone);
        var lessons = await db.Set<Lesson>()
            .Where(l => l.SeriesId == series.Id && l.Status == LessonStatus.Scheduled && !l.SeriesException && l.StartUtc >= fromUtc)
            .ToListAsync(cancellationToken);
        var ids = lessons.Select(l => l.Id).ToList();
        var removedParticipants = await db.Set<LessonParticipant>().Where(p => ids.Contains(p.LessonId)).ToListAsync(cancellationToken);
        // Pending notices of the removed lessons are withdrawn; students told about them or the series hear they are off.
        await notifications.SeriesEndedAsync(series.Id,
            [.. lessons.Select(l => (LessonService.Notice(l, l.Version + 1), (IReadOnlyCollection<Guid>)[.. removedParticipants.Where(p => p.LessonId == l.Id).Select(p => p.StudentId)]))],
            cancellationToken);

        db.Set<LessonParticipant>().RemoveRange(removedParticipants);
        db.Set<Lesson>().RemoveRange(lessons);
        var fromDay = DateOnly.FromDateTime(LessonTime.ToLocal(fromUtc, zone));
        db.Set<LessonSeriesSkip>().RemoveRange(await db.Set<LessonSeriesSkip>().Where(s => s.SeriesId == series.Id && s.Date >= fromDay).ToListAsync(cancellationToken));
        var end = DateOnly.FromDateTime(LessonTime.ToLocal(fromUtc.AddTicks(-1), zone));
        if (series.EndDate is null || series.EndDate > end)
        {
            series.EndDate = end;
        }

        // Deletions first, so the new occurrences' overlap checks no longer see the removed lessons.
        await SaveAsync(cancellationToken);
    }

    private static LessonSeries NewSeries(SaveSeriesRequest request, Guid teacher, string zoneId, DateTime now)
    {
        var kind = Enum.Parse<LessonKind>(request.Kind!);
        var mode = Enum.Parse<LessonMode>(request.Mode!);
        return new LessonSeries
        {
            Id = Guid.CreateVersion7(),
            TeacherUserId = teacher,
            Kind = kind,
            StudentId = kind == LessonKind.Individual ? request.StudentId : null,
            GroupId = kind == LessonKind.Group ? request.GroupId : null,
            SubjectId = request.SubjectId,
            Title = request.Title!.Trim(),
            Topic = Clean(request.Topic),
            Mode = mode,
            MeetingUrl = mode == LessonMode.Online ? Clean(request.MeetingUrl) : null,
            Location = mode == LessonMode.InPerson ? Clean(request.Location) : null,
            TimeZone = zoneId,
            StartDate = request.StartDate!.Value,
            StartTime = request.StartTime!.Value,
            DurationMinutes = request.DurationMinutes!.Value,
            IntervalWeeks = request.IntervalWeeks!.Value,
            EndDate = request.EndDate,
            Count = request.Count,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static void ThrowIfStartsInThePast(SaveSeriesRequest request, DateOnly today)
    {
        if (request.StartDate < today)
        {
            throw Invalid("startDate", "Recurring lessons start today or later.");
        }
    }

    private async Task ThrowIfUnknownSubjectAsync(Guid? wanted, Guid? saved, CancellationToken cancellationToken)
    {
        if (wanted is { } id && id != saved && (await catalog.UnknownSubjectsAsync([id], cancellationToken)).Count > 0)
        {
            throw Invalid("subjectId", "Unknown subject.");
        }
    }

    private async Task<LessonSeries> FindAsync(Guid seriesId, CancellationToken cancellationToken) =>
        await db.Set<LessonSeries>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == seriesId, cancellationToken) ?? throw new NotFoundException();

    private async Task<LessonSeries> LoadForChangeAsync(Guid seriesId, int? version, CancellationToken cancellationToken)
    {
        var series = await db.Set<LessonSeries>().SingleOrDefaultAsync(s => s.Id == seriesId, cancellationToken) ?? throw new NotFoundException();
        if (series.Version != version)
        {
            throw Conflict();
        }

        return series.ReplacedBySeriesId is null
            ? series
            : throw new BusinessRuleException("This series was already changed; edit the series that continues it.", ErrorCodes.InvalidStatusTransition);
    }

    private static void Touch(LessonSeries series, DateTime now)
    {
        series.UpdatedAtUtc = now;
        series.Version++;
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

    private static DateOnly Today(TimeZoneInfo zone, DateTime nowUtc) => DateOnly.FromDateTime(LessonTime.ToLocal(nowUtc, zone));

    private static DateTime Earliest(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Latest(DateTime a, DateTime b) => a > b ? a : b;

    private static ConflictException Conflict() =>
        new("The series was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
