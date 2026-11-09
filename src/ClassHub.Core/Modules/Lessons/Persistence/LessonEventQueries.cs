using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Persistence;

/// <summary>
/// The lesson event outbox for Finance. <see cref="PendingAsync"/> reads every workspace (identifiers only; the consumer
/// handles each event in a scope bound to its workspace); <see cref="MarkProcessedAsync"/> runs in that scope.
/// </summary>
internal sealed class LessonEventQueries(ClassHubDbContext db, TimeProvider clock) : ILessonEventFeed, IUpcomingLessons
{
    public async Task<IReadOnlyList<UpcomingLesson>> ScheduledBetweenAsync(DateTime fromUtc, DateTime toUtc, (DateTime StartUtc, Guid LessonId)? after, int max, CancellationToken cancellationToken)
    {
        var query = db.Set<Lesson>().IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Status == LessonStatus.Scheduled && l.StartUtc >= fromUtc && l.StartUtc < toUtc);
        if (after is var (afterStart, afterId))
        {
            query = query.Where(l => l.StartUtc > afterStart || (l.StartUtc == afterStart && l.Id.CompareTo(afterId) > 0));
        }

        var lessons = await query
            .OrderBy(l => l.StartUtc).ThenBy(l => l.Id)
            .Take(max)
            .Select(l => new { l.Id, l.WorkspaceId, l.TeacherUserId, l.StartUtc })
            .ToListAsync(cancellationToken);
        var ids = lessons.Select(l => l.Id).ToList();
        var participants = await db.Set<LessonParticipant>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => ids.Contains(p.LessonId))
            .Select(p => new { p.LessonId, p.StudentId })
            .ToListAsync(cancellationToken);
        return [.. lessons.Select(l => new UpcomingLesson(l.Id, l.WorkspaceId, l.TeacherUserId, l.StartUtc,
            [.. participants.Where(p => p.LessonId == l.Id).Select(p => p.StudentId)]))];
    }

    public async Task<IReadOnlyList<PendingLessonEvent>> PendingAsync(int max, CancellationToken cancellationToken) =>
        await (from e in db.Set<LessonEvent>().IgnoreQueryFilters().AsNoTracking()
               join l in db.Set<Lesson>().IgnoreQueryFilters().AsNoTracking() on new { e.WorkspaceId, e.LessonId } equals new { l.WorkspaceId, LessonId = l.Id }
               where e.ProcessedAtUtc == null
               orderby e.OccurredAtUtc, e.Id
               select new PendingLessonEvent(e.Id, e.WorkspaceId, e.LessonId, l.TeacherUserId, e.Kind, e.OccurredAtUtc))
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<bool> MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Set<LessonEvent>()
            .Where(e => e.Id == eventId && e.ProcessedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ProcessedAtUtc, now), cancellationToken) > 0;
    }
}
