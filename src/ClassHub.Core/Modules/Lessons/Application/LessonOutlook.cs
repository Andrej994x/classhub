using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>The current workspace's lessons ahead, for the teacher's dashboard (R2-P12).</summary>
internal sealed class LessonOutlook(ClassHubDbContext db) : ILessonOutlook
{
    public async Task<LessonsAhead> AheadAsync(DateTime nowUtc, DateTime untilUtc, int max, CancellationToken cancellationToken)
    {
        var ahead = db.Set<Lesson>().AsNoTracking()
            .Where(l => l.Status == LessonStatus.Scheduled && l.EndUtc > nowUtc && l.StartUtc < untilUtc);
        var count = await ahead.CountAsync(cancellationToken);
        var first = await ahead.OrderBy(l => l.StartUtc).ThenBy(l => l.Id).Take(Math.Max(0, max))
            .Select(l => new { l.Id, l.Title, l.StartUtc, l.EndUtc, l.TimeZone, l.Kind })
            .ToListAsync(cancellationToken);
        var ids = first.Select(l => l.Id).ToList();
        var participants = await db.Set<LessonParticipant>().AsNoTracking().Where(p => ids.Contains(p.LessonId))
            .Select(p => new { p.LessonId, p.StudentId }).ToListAsync(cancellationToken);
        return new LessonsAhead(count, [.. first.Select(l => new LessonAhead(l.Id, l.Title, l.StartUtc, l.EndUtc, l.TimeZone, l.Kind,
            [.. participants.Where(p => p.LessonId == l.Id).Select(p => p.StudentId)]))]);
    }
}
