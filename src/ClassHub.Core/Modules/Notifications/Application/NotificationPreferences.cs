using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Notifications.Application;

/// <summary>A user's choices for the optional notifications (R2-P11); missing means on.</summary>
internal sealed class NotificationPreferences(ClassHubDbContext db, TimeProvider clock)
{
    public async Task<NotificationPreferencesResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var saved = await db.Set<NotificationPreference>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return new NotificationPreferencesResponse(saved?.LessonReminders ?? true, saved?.HomeworkEmails ?? true);
    }

    /// <summary>
    /// Applies the given choices (missing ones stay). The first save creates the row; when another request created it at
    /// the same moment, the choices are applied to that row instead (never a lost change or an error).
    /// </summary>
    public async Task<NotificationPreferencesResponse> UpdateAsync(Guid userId, UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var saved = await db.Set<NotificationPreference>().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (saved is null)
            {
                saved = new NotificationPreference { UserId = userId };
                db.Set<NotificationPreference>().Add(saved);
            }

            saved.LessonReminders = request.LessonReminders ?? saved.LessonReminders;
            saved.HomeworkEmails = request.HomeworkEmails ?? saved.HomeworkEmails;
            saved.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new NotificationPreferencesResponse(saved.LessonReminders, saved.HomeworkEmails);
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex) && attempt < 2)
            {
                db.ChangeTracker.Clear(); // created meanwhile by another request: apply the choices to that row
            }
        }
    }
}
