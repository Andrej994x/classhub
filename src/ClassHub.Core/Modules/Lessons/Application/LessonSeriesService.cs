using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Persistence;
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
        var (created, conflicts) = await MaterializeAsync(series, now, cancellationToken);
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
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var today = Today(TimeZoneInfo.FindSystemTimeZoneById(zoneId), now);
        ThrowIfStartsInThePast(settings, today);
        await ThrowIfUnknownSubjectAsync(settings.SubjectId, (await FindAsync(seriesId, cancellationToken)).SubjectId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var teacher = (await FindAsync(seriesId, cancellationToken)).TeacherUserId;
        await queries.LockScheduleAsync(teacher, cancellationToken);
        var old = await LoadForChangeAsync(seriesId, request.Version, cancellationToken);

        DateOnly from;
        if (request.Scope == ThisAndFollowing)
        {
            var lesson = await db.Set<Lesson>().AsNoTracking().SingleOrDefaultAsync(l => l.Id == request.FromLessonId && l.SeriesId == old.Id, cancellationToken)
                ?? throw Invalid("fromLessonId", "The lesson is not part of this series.");
            from = DateOnly.FromDateTime(LessonTime.ToLocal(lesson.StartUtc, TimeZoneInfo.FindSystemTimeZoneById(old.TimeZone)));
        }
        else
        {
            from = today;
        }

        // Moving the following lessons earlier also takes the old ones from that day; the past is never rewritten.
        from = Max(Min(from, settings.StartDate!.Value), today);
        await participants.RequireAsync(Enum.Parse<LessonKind>(settings.Kind!), settings.StudentId, settings.GroupId, cancellationToken);
        await RemoveFromAsync(old, from, cancellationToken);

        var next = NewSeries(settings, teacher, zoneId, now);
        db.Set<LessonSeries>().Add(next);
        old.ReplacedBySeriesId = next.Id;
        Touch(old, now);
        await SaveAsync(cancellationToken);
        var (created, conflicts) = await MaterializeAsync(next, now, cancellationToken);
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
        var today = Today(TimeZoneInfo.FindSystemTimeZoneById(series.TimeZone), now);
        await RemoveFromAsync(series, Max(request.FromDate!.Value, today), cancellationToken);
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
    private async Task<(int Created, IReadOnlyList<SeriesSkipResponse> Conflicts)> MaterializeAsync(LessonSeries series, DateTime nowUtc, CancellationToken cancellationToken)
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
                created++;
            }

            series.NextIndex = index + 1;
        }

        await SaveAsync(cancellationToken);
        return (created, conflicts);
    }

    /// <summary>Ends a series before <paramref name="from"/>: its scheduled, unchanged occurrences and skips from then on go.</summary>
    private async Task RemoveFromAsync(LessonSeries series, DateOnly from, CancellationToken cancellationToken)
    {
        var fromUtc = LessonTime.StartOfDayUtc(from, TimeZoneInfo.FindSystemTimeZoneById(series.TimeZone));
        var lessons = await db.Set<Lesson>()
            .Where(l => l.SeriesId == series.Id && l.Status == LessonStatus.Scheduled && !l.SeriesException && l.StartUtc >= fromUtc)
            .ToListAsync(cancellationToken);
        var ids = lessons.Select(l => l.Id).ToList();
        db.Set<LessonParticipant>().RemoveRange(await db.Set<LessonParticipant>().Where(p => ids.Contains(p.LessonId)).ToListAsync(cancellationToken));
        db.Set<Lesson>().RemoveRange(lessons);
        db.Set<LessonSeriesSkip>().RemoveRange(await db.Set<LessonSeriesSkip>().Where(s => s.SeriesId == series.Id && s.Date >= from).ToListAsync(cancellationToken));
        var end = from.AddDays(-1);
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

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static ConflictException Conflict() =>
        new("The series was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
