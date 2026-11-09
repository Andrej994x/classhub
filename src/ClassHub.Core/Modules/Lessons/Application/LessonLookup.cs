using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

internal sealed class LessonLookup(ClassHubDbContext db) : ILessonLookup
{
    public async Task<IReadOnlyDictionary<Guid, LessonSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, LessonSummary>();
        }

        var list = ids.Distinct().ToList();
        var lessons = await db.Set<Lesson>().AsNoTracking()
            .Where(l => list.Contains(l.Id))
            .Select(l => new { l.Id, l.Title, l.StartUtc, l.TimeZone, l.Status })
            .ToListAsync(cancellationToken);
        return lessons.ToDictionary(l => l.Id, l => new LessonSummary(l.Id, l.Title, l.StartUtc, l.TimeZone, l.Status.ToString()));
    }
}
