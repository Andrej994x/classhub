using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Admin.Application;
using ClassHub.Core.Modules.Admin.Contracts;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Admin;

/// <summary>
/// Platform administration (<c>/api/admin</c>, M15, R1-P9). Every endpoint needs a platform role and a session signed
/// in with the authenticator (<c>platform:staff</c>); granting and revoking Platform Admin needs the Platform Owner and
/// a fresh authenticator code. Metadata and aggregates only.
/// </summary>
public sealed class AdminModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddScoped<AdminService>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").WithTags("Admin").RequireAntiforgeryHeader().RequireAuthorization(ClassHubPolicies.PlatformStaff);

        admin.MapGet("/overview", async (AdminService service, CancellationToken ct) => TypedResults.Ok(await service.GetOverviewAsync(ct)))
            .WithName("GetAdminOverview")
            .WithSummary("Platform numbers: users, teachers, registrations, published sites.");

        admin.MapGet("/users", async (string? q, string? status, bool? staff, int? page, AdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListUsersAsync(q, status, staff, page ?? 1, ct)))
            .WithName("ListAdminUsers")
            .WithSummary("Search accounts by email (or id), status and platform role; 20 per page, newest first.");

        admin.MapGet("/users/{userId:guid}", async (Guid userId, AdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetUserAsync(userId, ct)))
            .WithName("GetAdminUser");

        admin.MapPost("/users/{userId:guid}/suspend", async (Guid userId, AdminStatusRequest request, HttpContext http, AdminService service) =>
                TypedResults.Ok(await service.SuspendAsync(http.User, userId, request, http.RequestAborted)))
            .WithValidation<AdminStatusRequest>()
            .WithName("SuspendUser")
            .WithSummary("Suspend an account (reason required): sessions end, sign-in is refused, the public site is hidden.");

        admin.MapPost("/users/{userId:guid}/reactivate", async (Guid userId, AdminStatusRequest request, HttpContext http, AdminService service) =>
                TypedResults.Ok(await service.ReactivateAsync(http.User, userId, request, http.RequestAborted)))
            .WithValidation<AdminStatusRequest>()
            .WithName("ReactivateUser");

        admin.MapPost("/users/{userId:guid}/admin-role/grant", async (Guid userId, AdminRoleRequest request, HttpContext http, AdminService service) =>
                TypedResults.Ok(await service.SetAdminRoleAsync(http.User, userId, grant: true, request, http.RequestAborted)))
            .WithValidation<AdminRoleRequest>()
            .RequireAuthorization(ClassHubPolicies.PlatformOwner)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("GrantPlatformAdmin")
            .WithSummary("Platform Owner only, with a fresh authenticator code.");

        admin.MapPost("/users/{userId:guid}/admin-role/revoke", async (Guid userId, AdminRoleRequest request, HttpContext http, AdminService service) =>
                TypedResults.Ok(await service.SetAdminRoleAsync(http.User, userId, grant: false, request, http.RequestAborted)))
            .WithValidation<AdminRoleRequest>()
            .RequireAuthorization(ClassHubPolicies.PlatformOwner)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("RevokePlatformAdmin")
            .WithSummary("Platform Owner only, with a fresh authenticator code.");

        admin.MapGet("/audit", async (string? action, Guid? userId, Guid? workspaceId, DateOnly? from, DateOnly? to, int? page, AdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAuditAsync(action, userId, workspaceId, from, to, page ?? 1, ct)))
            .WithName("ListAuditLog")
            .WithSummary("Audit entries, newest first, filtered by action prefix, user (actor or target), workspace and UTC days.");
    }
}

internal sealed class AdminStatusRequestValidator : AbstractValidator<AdminStatusRequest>
{
    public const int ReasonMaxLength = 500;

    public AdminStatusRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 3 and <= ReasonMaxLength)
            .WithMessage($"Give a reason (3 to {ReasonMaxLength} characters).");
}

internal sealed class AdminRoleRequestValidator : AbstractValidator<AdminRoleRequest>
{
    public AdminRoleRequestValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
}
