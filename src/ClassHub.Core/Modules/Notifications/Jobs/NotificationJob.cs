using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Notifications.Application;
using ClassHub.Core.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Notifications.Jobs;

/// <summary>Notification settings (section <c>Notifications</c>).</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public int IntervalSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 50;

    /// <summary>How long a claimed notification belongs to one dispatcher before another may try it.</summary>
    public int LeaseSeconds { get; set; } = 120;

    public int MaxAttempts { get; set; } = 5;

    /// <summary>Reminders are planned for scheduled lessons starting within this many hours.</summary>
    public int PlanningHours { get; set; } = 26;
}

/// <summary>
/// The notification dispatcher (R2-P11): plans reminders from the schedule, claims due notifications with a lease
/// (<c>UPDLOCK, READPAST</c>: two dispatchers never claim the same row), and sends each in a scope bound to its
/// workspace as its teacher. Pending, failed and claimed rows are read across workspaces as identifiers only.
/// </summary>
internal sealed partial class NotificationJob(IServiceScopeFactory scopes, IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds)), clock);
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

    /// <summary>One pass; returns how many notifications were sent.</summary>
    public Task<int> RunOnceAsync(CancellationToken cancellationToken) => RunAtAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);

    /// <summary>One pass as of <paramref name="nowUtc"/> (tests move time forward with it).</summary>
    internal async Task<int> RunAtAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await PlanRemindersAsync(nowUtc, cancellationToken);
        var sent = 0;
        foreach (var claim in await ClaimAsync(nowUtc, cancellationToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                if (await services.GetRequiredService<IWorkspaceMembershipLookup>().GetActiveRoleAsync(claim.WorkspaceId, claim.TeacherUserId, cancellationToken) is not { } role)
                {
                    await CloseAsync(services.GetRequiredService<ClassHubDbContext>(), claim.Id, NotificationStatus.Suppressed, "teacher", cancellationToken);
                    continue;
                }

                services.GetRequiredService<TenantContext>().Set(claim.WorkspaceId, claim.TeacherUserId, role);
                if (await services.GetRequiredService<NotificationSender>().SendAsync(claim.Id, nowUtc, cancellationToken) == NotificationStatus.Sent)
                {
                    sent++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The lease runs out and the next pass tries again, until the attempts are used up.
                LogNotificationFailed(logger, claim.Id, ex);
            }
        }

        await FailExhaustedAsync(nowUtc, cancellationToken);
        return sent;
    }

    /// <summary>A reminder for every student of every scheduled lesson starting soon (once: unique dedupe keys).</summary>
    private async Task PlanRemindersAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var upcoming = await scope.ServiceProvider.GetRequiredService<IUpcomingLessons>()
            .ScheduledBetweenAsync(nowUtc, nowUtc.AddHours(Math.Max(1, options.Value.PlanningHours)), 2_000, cancellationToken);
        var wanted = upcoming
            .SelectMany(l => l.StudentIds.Select(s => (Lesson: l, StudentId: s, Due: NotificationRules.ReminderDueAtUtc(l.StartUtc, nowUtc))))
            .Where(r => r.Due is not null)
            .Select(r => (r.Lesson, r.StudentId, Due: r.Due!.Value, Key: NotificationRules.DedupeKey(NotificationKind.LessonReminder, r.Lesson.LessonId, r.StudentId, r.Lesson.StartUtc)))
            .ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var keys = wanted.Select(w => w.Key).ToList();
        var existing = (await db.Set<Notification>().IgnoreQueryFilters().AsNoTracking().Where(n => keys.Contains(n.DedupeKey)).Select(n => n.DedupeKey).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (lesson, studentId, due, key) in wanted.Where(w => !existing.Contains(w.Key)))
        {
            db.Set<Notification>().Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = lesson.WorkspaceId,
                Kind = NotificationKind.LessonReminder,
                DedupeKey = key,
                StudentId = studentId,
                TeacherUserId = lesson.TeacherUserId,
                LessonId = lesson.LessonId,
                LessonStartUtc = lesson.StartUtc,
                DueAtUtc = due,
                CreatedAtUtc = nowUtc,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear(); // another dispatcher planned it at the same moment
            }
        }
    }

    private sealed record Claim(Guid Id, Guid WorkspaceId, Guid TeacherUserId);

    /// <summary>Claims due notifications for this dispatcher (skipping rows another one holds).</summary>
    private async Task<IReadOnlyList<Claim>> ClaimAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var batch = Math.Max(1, options.Value.BatchSize);
        var lease = nowUtc.AddSeconds(Math.Max(10, options.Value.LeaseSeconds));
        var maxAttempts = Math.Max(1, options.Value.MaxAttempts);
        return await db.Database.SqlQuery<Claim>($"""
            UPDATE TOP ({batch}) n
            SET n.[LockedUntilUtc] = {lease}, n.[Attempts] = n.[Attempts] + 1
            OUTPUT inserted.[Id], inserted.[WorkspaceId], inserted.[TeacherUserId]
            FROM [Notifications] AS n WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE n.[Status] = 1 AND n.[DueAtUtc] <= {nowUtc} AND (n.[LockedUntilUtc] IS NULL OR n.[LockedUntilUtc] < {nowUtc}) AND n.[Attempts] < {maxAttempts}
            """).ToListAsync(cancellationToken);
    }

    /// <summary>Pending notifications that used up their attempts (and are not held) fail.</summary>
    private async Task FailExhaustedAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var maxAttempts = Math.Max(1, options.Value.MaxAttempts);
        await db.Set<Notification>().IgnoreQueryFilters()
            .Where(n => n.Status == NotificationStatus.Pending && n.Attempts >= maxAttempts && (n.LockedUntilUtc == null || n.LockedUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Failed).SetProperty(n => n.Outcome, "attempts").SetProperty(n => n.Version, n => n.Version + 1), cancellationToken);
    }

    private static Task CloseAsync(ClassHubDbContext db, Guid id, NotificationStatus status, string outcome, CancellationToken cancellationToken) =>
        db.Set<Notification>().IgnoreQueryFilters().Where(n => n.Id == id && n.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, status).SetProperty(n => n.Outcome, outcome).SetProperty(n => n.LockedUntilUtc, (DateTime?)null)
                .SetProperty(n => n.Version, n => n.Version + 1), cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending notification {NotificationId} failed")]
    private static partial void LogNotificationFailed(ILogger logger, Guid notificationId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The notification job failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
