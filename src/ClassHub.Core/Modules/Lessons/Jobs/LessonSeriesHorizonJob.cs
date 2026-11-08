using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Lessons.Application;
using ClassHub.Core.Modules.Lessons.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Lessons.Jobs;

/// <summary>Lesson settings (section <c>Lessons</c>).</summary>
public sealed class LessonOptions
{
    public const string SectionName = "Lessons";

    /// <summary>How often recurring lessons are extended to the 12-week horizon.</summary>
    public int HorizonCheckSeconds { get; set; } = 3600;

    /// <summary>At most this many series are extended per pass (the rest follow in the next one).</summary>
    public int HorizonBatchSize { get; set; } = 200;
}

/// <summary>
/// Keeps every active series materialized <see cref="LessonSeries.HorizonWeeks"/> weeks ahead (R2-P5). Each series is
/// extended in its own scope bound to its workspace as its teacher (only while the teacher is still an active member),
/// so the normal tenant filters, the schedule lock and the overlap checks apply exactly as for the teacher's own edits.
/// </summary>
internal sealed partial class LessonSeriesHorizonJob(IServiceScopeFactory scopes, IOptions<LessonOptions> options, TimeProvider clock, ILogger<LessonSeriesHorizonJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, options.Value.HorizonCheckSeconds));
        using var timer = new PeriodicTimer(interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    /// <summary>One pass; returns how many lessons were created.</summary>
    public Task<int> RunOnceAsync(CancellationToken cancellationToken) => RunAtAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);

    /// <summary>One pass as of <paramref name="nowUtc"/> (tests move time forward with it).</summary>
    internal async Task<int> RunAtAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<(Guid Id, Guid WorkspaceId, Guid TeacherUserId)> candidates;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
            // Every tenant (system scope): only identifiers leave this query. A series still needs work when it was not
            // replaced, has not ended before today (in any time zone) and its next occurrence is before its horizon.
            var yesterday = DateOnly.FromDateTime(nowUtc).AddDays(-1);
            var open = await db.Set<LessonSeries>().IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.ReplacedBySeriesId == null && (s.EndDate == null || s.EndDate >= yesterday) && (s.Count == null || s.NextIndex < s.Count))
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.WorkspaceId, s.TeacherUserId, s.TimeZone, s.StartDate, s.IntervalWeeks, s.EndDate, s.Count, s.NextIndex })
                .ToListAsync(cancellationToken);
            // Only series whose next occurrence exists and is within the horizon the service uses (in the series' own time
            // zone): every candidate makes progress, so one with nothing to do never takes a turn from another.
            candidates = [.. open.Where(s => SeriesPattern.Includes(s.StartDate, s.IntervalWeeks, s.EndDate, s.Count, s.NextIndex)
                    && SeriesPattern.DateOf(s.StartDate, s.IntervalWeeks, s.NextIndex) < LessonSeries.HorizonEnd(TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone), nowUtc))
                .Select(s => (s.Id, s.WorkspaceId, s.TeacherUserId))];
        }

        var created = 0;
        var worked = 0;
        foreach (var (id, workspaceId, teacher) in candidates)
        {
            if (worked >= options.Value.HorizonBatchSize)
            {
                break; // the rest follow in the next pass
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                if (await services.GetRequiredService<IWorkspaceMembershipLookup>().GetActiveRoleAsync(workspaceId, teacher, cancellationToken) is not { } role)
                {
                    continue; // the teacher left the workspace: nothing is scheduled on their behalf (and it takes no turn)
                }

                worked++;
                services.GetRequiredService<TenantContext>().Set(workspaceId, teacher, role);
                created += await services.GetRequiredService<LessonSeriesService>().ExtendAsync(id, nowUtc, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSeriesFailed(logger, id, ex);
            }
        }

        if (created > 0)
        {
            LogCreated(logger, created);
        }

        return created;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} recurring lessons were created up to the horizon")]
    private static partial void LogCreated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Extending lesson series {SeriesId} failed")]
    private static partial void LogSeriesFailed(ILogger logger, Guid seriesId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The lesson series horizon job failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
