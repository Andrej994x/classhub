using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.Modules.Lessons.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Finance.Application;

/// <summary>Ledger entries naming a lesson (consumptions, corrections, reversals): the history keeps that lesson.</summary>
internal sealed class FinanceLessonReferences(ClassHubDbContext db) : ILessonReferences
{
    public async Task<IReadOnlySet<Guid>> ReferencedAsync(IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken)
    {
        if (lessonIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = lessonIds.ToList();
        return (await db.Set<LessonLedgerEntry>().AsNoTracking().Where(e => e.LessonId != null && ids.Contains(e.LessonId.Value))
            .Select(e => e.LessonId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }
}
