using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Homework.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Homework.Application;

/// <summary>Active assignments for notifications (the archive filter hides archived homework).</summary>
internal sealed class HomeworkNotices(ClassHubDbContext db) : IHomeworkNotices
{
    public async Task<HomeworkNotice?> FindAsync(Guid homeworkId, Guid studentId, CancellationToken cancellationToken) =>
        await (from a in db.Set<HomeworkAssignment>().AsNoTracking()
               join h in db.Set<HomeworkTask>().AsNoTracking() on a.HomeworkId equals h.Id
               where a.HomeworkId == homeworkId && a.StudentId == studentId
               select new HomeworkNotice(h.Id, a.Id, h.Title, h.DueDate))
            .SingleOrDefaultAsync(cancellationToken);
}
