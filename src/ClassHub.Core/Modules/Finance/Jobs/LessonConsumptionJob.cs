using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Finance.Application;
using ClassHub.Core.Modules.Lessons.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Finance.Jobs;

/// <summary>Finance settings (section <c>Finance</c>).</summary>
public sealed class FinanceOptions
{
    public const string SectionName = "Finance";

    /// <summary>How often held lessons are turned into ledger entries.</summary>
    public int ConsumptionIntervalSeconds { get; set; } = 5;

    /// <summary>At most this many lesson events per pass (the rest follow in the next one).</summary>
    public int ConsumptionBatchSize { get; set; } = 200;
}

/// <summary>
/// Consumes the lesson event outbox (R2-P10). Pending events are read across workspaces (identifiers only); each is
/// handled in its own scope bound to its workspace as the lesson's teacher, so the normal tenant filters and the
/// finance lock apply. Running twice, or on two instances at once, consumes nothing twice.
/// </summary>
internal sealed partial class LessonConsumptionJob(IServiceScopeFactory scopes, IOptions<FinanceOptions> options, TimeProvider clock, ILogger<LessonConsumptionJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.ConsumptionIntervalSeconds)), clock);
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

    /// <summary>One pass; returns how many events this pass handled.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingLessonEvent> pending;
        await using (var scope = scopes.CreateAsyncScope())
        {
            pending = await scope.ServiceProvider.GetRequiredService<ILessonEventFeed>().PendingAsync(Math.Max(1, options.Value.ConsumptionBatchSize), cancellationToken);
        }

        var handled = 0;
        foreach (var lessonEvent in pending)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                if (await services.GetRequiredService<IWorkspaceMembershipLookup>().GetActiveRoleAsync(lessonEvent.WorkspaceId, lessonEvent.TeacherUserId, cancellationToken) is not { } role)
                {
                    LogNoTeacher(logger, lessonEvent.Id);
                    continue;
                }

                services.GetRequiredService<TenantContext>().Set(lessonEvent.WorkspaceId, lessonEvent.TeacherUserId, role);
                if (await services.GetRequiredService<LessonConsumption>().ProcessAsync(lessonEvent, cancellationToken))
                {
                    handled++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogEventFailed(logger, lessonEvent.Id, ex);
            }
        }

        return handled;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lesson event {EventId} waits: its teacher is no longer an active member of the workspace")]
    private static partial void LogNoTeacher(ILogger logger, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling lesson event {EventId} failed")]
    private static partial void LogEventFailed(ILogger logger, Guid eventId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The lesson consumption job failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
