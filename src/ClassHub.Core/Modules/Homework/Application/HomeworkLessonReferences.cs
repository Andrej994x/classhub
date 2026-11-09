using ClassHub.Core.Modules.Homework.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Homework.Application;

/// <summary>Homework linked to a lesson (archived homework included): that lesson must stay.</summary>
internal sealed class HomeworkLessonReferences(HomeworkQueries queries) : ILessonReferences
{
    public async Task<IReadOnlySet<Guid>> ReferencedAsync(IReadOnlyCollection<Guid> lessonIds, CancellationToken cancellationToken)
    {
        if (lessonIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = lessonIds.ToList();
        return (await queries.WithArchived().Where(h => h.LessonId != null && ids.Contains(h.LessonId.Value))
            .Select(h => h.LessonId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }
}
