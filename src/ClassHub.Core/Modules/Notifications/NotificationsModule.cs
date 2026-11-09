using System.Security.Claims;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Notifications.Application;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Jobs;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        services.AddScoped<NotificationPreferences>();
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.AddSingleton<NotificationJob>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationJob>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var preferences = endpoints.MapGroup("/me/notification-preferences").WithTags("Notifications").RequireAntiforgeryHeader();

        preferences.MapGet("", async (ClaimsPrincipal user, NotificationPreferences service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(UserId(user), ct)))
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("GetNotificationPreferences")
            .WithSummary("The optional notifications (lesson reminders, new homework) of the signed-in user; on unless switched off.");

        preferences.MapPut("", async (UpdateNotificationPreferencesRequest request, ClaimsPrincipal user, NotificationPreferences service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(UserId(user), request, ct)))
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithName("UpdateNotificationPreferences");
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
