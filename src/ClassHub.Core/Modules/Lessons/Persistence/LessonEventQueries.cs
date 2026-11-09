using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Persistence;

/// <summary>
/// The lesson event outbox for Finance. <see cref="PendingAsync"/> reads every workspace (identifiers only; the consumer
/// handles each event in a scope bound to its workspace); <see cref="MarkProcessedAsync"/> runs in that scope.
/// </summary>
internal sealed class LessonEventQueries(ClassHubDbContext db, TimeProvider clock) : ILessonEventFeed
{
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
