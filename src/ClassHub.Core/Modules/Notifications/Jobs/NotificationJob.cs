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

    /// <summary>Lessons read per page while planning (every page of the window is read in each pass).</summary>
    public int PlanningPageSize { get; set; } = 500;

    /// <summary>A pass claims batch after batch (oldest due first) until nothing is due, at most this many batches.</summary>
    public int MaxBatchesPerPass { get; set; } = 20;
}

/// <summary>
/// The notification dispatcher (R2-P11): plans reminders from the schedule, claims due notifications with a lease
/// (<c>UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK</c> like the email outbox: locking, not row-versioning, semantics
/// although the database uses READ_COMMITTED_SNAPSHOT; two dispatchers never claim the same row), and sends each in a
/// scope bound to its workspace as its teacher. Pending, failed and claimed rows are read across workspaces as
/// identifiers only.
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
        // Batch after batch, oldest due first, until nothing is due: a backlog never leaves a notice waiting for a later pass.
        for (var batches = 0; batches < Math.Max(1, options.Value.MaxBatchesPerPass); batches++)
        {
            var claims = await ClaimAsync(nowUtc, cancellationToken);
            sent += await SendAllAsync(claims, nowUtc, cancellationToken);
            if (claims.Count < Math.Max(1, options.Value.BatchSize))
            {
                break;
            }
        }

        await FailExhaustedAsync(nowUtc, cancellationToken);
        return sent;
    }

    private async Task<int> SendAllAsync(IReadOnlyList<Claim> claims, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var sent = 0;
        foreach (var claim in claims)
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

        return sent;
    }

    /// <summary>
    /// A reminder for every student of every scheduled lesson starting soon: the whole window, page by page, so no lesson
    /// waits behind others. One per lesson, student and start time; only a withdrawn one (the lesson moved away and back)
    /// is followed by a new attempt.
    /// </summary>
    private async Task PlanRemindersAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var until = nowUtc.AddHours(Math.Max(1, options.Value.PlanningHours));
        var pageSize = Math.Max(1, options.Value.PlanningPageSize);
        (DateTime, Guid)? after = null;
        while (true)
        {
            IReadOnlyList<UpcomingLesson> page;
            await using (var scope = scopes.CreateAsyncScope())
            {
                page = await scope.ServiceProvider.GetRequiredService<IUpcomingLessons>().ScheduledBetweenAsync(nowUtc, until, after, pageSize, cancellationToken);
            }

            if (page.Count == 0)
            {
                return;
            }

            await PlanPageAsync(page, nowUtc, cancellationToken);
            if (page.Count < pageSize)
            {
                return;
            }

            after = (page[^1].StartUtc, page[^1].LessonId);
        }
    }

    private async Task PlanPageAsync(IReadOnlyList<UpcomingLesson> page, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = page
            .SelectMany(l => l.StudentIds.Select(s => (Lesson: l, StudentId: s, Due: NotificationRules.ReminderDueAtUtc(l.StartUtc, nowUtc))))
            .Where(r => r.Due is not null)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var lessonIds = candidates.Select(c => c.Lesson.LessonId).Distinct().ToList();
        var reminders = await db.Set<Notification>().IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Kind == NotificationKind.LessonReminder && lessonIds.Contains(n.LessonId!.Value))
            .Select(n => new { n.LessonId, n.StudentId, n.LessonStartUtc, n.Status, n.Outcome })
            .ToListAsync(cancellationToken);
        foreach (var (lesson, studentId, due) in candidates)
        {
            var forThisTime = reminders.Where(r => r.LessonId == lesson.LessonId && r.StudentId == studentId && r.LessonStartUtc == lesson.StartUtc).ToList();
            // Only a reminder that no longer applied (withdrawn, or found outdated when sent: the lesson had moved away and
            // is back) is followed by a new one; one that was sent, opted out, without an address or failed stays the outcome.
            if (forThisTime.Any(r => !Replaceable(r.Status, r.Outcome)))
            {
                continue;
            }

            var key = NotificationRules.ReminderKey(lesson.LessonId, studentId, lesson.StartUtc, forThisTime.Count);
            AddReminder(db, lesson, studentId, due!.Value, key, nowUtc);
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

    private static bool Replaceable(NotificationStatus status, string? outcome) =>
        status == NotificationStatus.Cancelled || (status == NotificationStatus.Suppressed && outcome == NotificationSender.Outdated);

    private static void AddReminder(ClassHubDbContext db, UpcomingLesson lesson, Guid studentId, DateTime due, string key, DateTime nowUtc) =>
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

    private sealed record Claim(Guid Id, Guid WorkspaceId, Guid TeacherUserId);

    /// <summary>Claims due notifications for this dispatcher (skipping rows another one holds).</summary>
    private async Task<IReadOnlyList<Claim>> ClaimAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var batch = Math.Max(1, options.Value.BatchSize);
        var lease = nowUtc.AddSeconds(Math.Max(10, options.Value.LeaseSeconds));
        var maxAttempts = Math.Max(1, options.Value.MaxAttempts);
        // The oldest due first (an ordered batch, then claimed): fair under a backlog.
        return await db.Database.SqlQuery<Claim>($"""
            WITH due AS (
                SELECT TOP ({batch}) n.*
                FROM [Notifications] AS n WITH (UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK)
                WHERE n.[Status] = 1 AND n.[DueAtUtc] <= {nowUtc} AND (n.[LockedUntilUtc] IS NULL OR n.[LockedUntilUtc] < {nowUtc}) AND n.[Attempts] < {maxAttempts}
                ORDER BY n.[DueAtUtc], n.[Id])
            UPDATE due
            SET [LockedUntilUtc] = {lease}, [Attempts] = [Attempts] + 1
            OUTPUT inserted.[Id], inserted.[WorkspaceId], inserted.[TeacherUserId]
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
