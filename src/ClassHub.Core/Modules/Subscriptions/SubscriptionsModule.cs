using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Subscriptions.Application;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Subscriptions.Domain;
using ClassHub.Core.Modules.Subscriptions.Jobs;
using ClassHub.Core.Modules.Subscriptions.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Subscriptions;

/// <summary>
/// SaaS plans and entitlements (M14, R1-P10). Every workspace is on Free unless staff activated Pro for a period;
/// limits apply to new creations; expiry never deletes data. No automatic billing (payment provider later).
/// </summary>
public sealed class SubscriptionsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.SectionName));
        services.AddScoped<SubscriptionQueries>();
        services.AddScoped<PlanCatalog>();
        services.AddScoped<EntitlementService>();
        services.AddScoped<IEntitlementService>(sp => sp.GetRequiredService<EntitlementService>());
        services.AddScoped<ISubscriptionStats>(sp => sp.GetRequiredService<EntitlementService>());
        services.AddScoped<SubscriptionAdminService>();
        services.AddSingleton<SubscriptionExpiryJob>();
        services.AddHostedService(sp => sp.GetRequiredService<SubscriptionExpiryJob>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(WorkspacesModule.WorkspaceRoute).WithTags("Plan").RequireAntiforgeryHeader()
            .MapGet("/plan", async (EntitlementService service, CancellationToken ct) => TypedResults.Ok(await service.GetTeacherPlanAsync(ct)))
            .RequireAuthorization(WorkspacePolicies.Read)
            .WithName("GetWorkspacePlan")
            .WithSummary("The workspace's plan, its limits and what is in use. Teachers cannot change it (platform staff do).");

        var admin = endpoints.MapGroup("/admin").WithTags("Admin").RequireAntiforgeryHeader().RequireAuthorization(ClassHubPolicies.PlatformStaff);

        admin.MapGet("/plans", async (SubscriptionAdminService service, CancellationToken ct) => TypedResults.Ok(await service.ListPlansAsync(ct)))
            .WithName("ListPlans");

        admin.MapPut("/plans/{code}", async (string code, UpdatePlanLimitsRequest request, SubscriptionAdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdatePlanAsync(code, request, ct)))
            .WithValidation<UpdatePlanLimitsRequest>()
            .RequireAuthorization(ClassHubPolicies.PlatformOwner)
            .WithName("UpdatePlanLimits")
            .WithSummary("Platform Owner only (D-39): new values for every limit of the plan.");

        admin.MapGet("/workspaces/{workspaceId:guid}/subscription", async (Guid workspaceId, SubscriptionAdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAsync(workspaceId, ct)))
            .WithName("GetWorkspaceSubscription");

        admin.MapPost("/workspaces/{workspaceId:guid}/subscription/pro", async (Guid workspaceId, ActivateProRequest request, HttpContext http, SubscriptionAdminService service) =>
                TypedResults.Ok(await service.ActivateProAsync(http.User, workspaceId, request, http.RequestAborted)))
            .WithValidation<ActivateProRequest>()
            .WithName("ActivatePro")
            .WithSummary("Activate Pro for N months, or extend an active Pro period from its end, recording the payment (D-30).");
    }
}

internal sealed class ActivateProRequestValidator : AbstractValidator<ActivateProRequest>
{
    public ActivateProRequestValidator()
    {
        RuleFor(x => x.Months).NotNull().InclusiveBetween(1, 36);
        RuleFor(x => x.AmountMinor).NotNull().InclusiveBetween(0, SubscriptionEvent.MaxAmountMinor);
        RuleFor(x => x.AmountMinor).Equal(0).When(x => x.PaymentMethod == PaymentMethods.Complimentary)
            .WithMessage("A complimentary activation has no amount.");
        RuleFor(x => x.AmountMinor).GreaterThan(0).When(x => x.PaymentMethod is not null && x.PaymentMethod != PaymentMethods.Complimentary)
            .WithMessage("Enter the amount that was paid.");
        RuleFor(x => x.Currency).Must(Currencies.IsActiveIsoCode).WithMessage("Unknown currency.");
        RuleFor(x => x.PaymentMethod).Must(m => m is not null && PaymentMethods.All.Contains(m)).WithMessage("Unknown payment method.");
        RuleFor(x => x.PaymentReference).MaximumLength(SubscriptionEvent.ReferenceMaxLength);
        RuleFor(x => x.PaidOn).NotNull();
        RuleFor(x => x.Note).MaximumLength(SubscriptionEvent.NoteMaxLength);
    }
}

internal sealed class UpdatePlanLimitsRequestValidator : AbstractValidator<UpdatePlanLimitsRequest>
{
    public UpdatePlanLimitsRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Limits).NotNull()
            .Must(l => l!.Count == PlanLimits.All.Count && PlanLimits.All.All(k => l.Count(v => v.Key == k) == 1))
            .WithMessage("Send every limit exactly once.");
        RuleForEach(x => x.Limits).ChildRules(limit =>
            limit.RuleFor(v => v.Value).NotNull().InclusiveBetween(0, PlanLimit.MaxValue));
    }
}
