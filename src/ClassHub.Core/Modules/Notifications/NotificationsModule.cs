using System.Security.Claims;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Notifications.Application;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.Modules.Notifications.Jobs;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Notifications;

/// <summary>Notifications (M12, R2-P11): lesson changes, reminders and new homework by email (D-26), with preferences.</summary>
public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationScheduler, NotificationScheduler>();
        services.AddScoped<NotificationSender>();
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.AddSingleton<NotificationJob>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationJob>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var preferences = endpoints.MapGroup("/me/notification-preferences").WithTags("Notifications").RequireAntiforgeryHeader();

        preferences.MapGet("", async (ClaimsPrincipal user, ClassHubDbContext db, CancellationToken ct) =>
            {
                var saved = await db.Set<NotificationPreference>().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == UserId(user), ct);
                return TypedResults.Ok(new NotificationPreferencesResponse(saved?.LessonReminders ?? true, saved?.HomeworkEmails ?? true));
            })
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("GetNotificationPreferences")
            .WithSummary("The optional notifications (lesson reminders, new homework) of the signed-in user; on unless switched off.");

        preferences.MapPut("", async (UpdateNotificationPreferencesRequest request, ClaimsPrincipal user, ClassHubDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var userId = UserId(user);
                var saved = await db.Set<NotificationPreference>().SingleOrDefaultAsync(p => p.UserId == userId, ct);
                if (saved is null)
                {
                    saved = new NotificationPreference { UserId = userId };
                    db.Set<NotificationPreference>().Add(saved);
                }

                saved.LessonReminders = request.LessonReminders ?? saved.LessonReminders;
                saved.HomeworkEmails = request.HomeworkEmails ?? saved.HomeworkEmails;
                saved.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(new NotificationPreferencesResponse(saved.LessonReminders, saved.HomeworkEmails));
            })
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("UpdateNotificationPreferences");
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
