using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Moderation.Application;
using ClassHub.Core.Modules.Moderation.Contracts;
using ClassHub.Core.Modules.Moderation.Domain;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Moderation;

/// <summary>
/// Moderation (M15, R1-P11): anonymous reports from the public pages, the staff queue, and hiding/showing a site,
/// a section or a test. Reporter details are platform data only: no teacher endpoint ever returns them.
/// </summary>
public sealed class ModerationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ModerationOptions>(configuration.GetSection(ModerationOptions.SectionName));
        services.AddScoped<ReportIntake>();
        services.AddScoped<ModerationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/public/sites/{slug:siteslug}").WithTags("Public reports").AllowAnonymous()
            .MapPost("/reports", async (string slug, SubmitReportRequest request, ReportIntake intake, CancellationToken ct) =>
                {
                    await intake.SubmitAsync(slug, request, ct);
                    return TypedResults.Accepted((string?)null);
                })
            .WithValidation<SubmitReportRequest>()
            .RequireRateLimiting(RateLimitPolicies.PublicForm)
            .WithName("SubmitReport")
            .WithSummary("Report content of a public page: the site, a section or a test. Always 202 once the content exists.");

        var admin = endpoints.MapGroup("/admin").WithTags("Admin").RequireAntiforgeryHeader().RequireAuthorization(ClassHubPolicies.PlatformStaff);

        admin.MapGet("/reports", async (string? status, int? page, ModerationService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(status, page ?? 1, ct)))
            .WithName("ListReports")
            .WithSummary("The moderation queue, newest first; filter by status (Open, Resolved, Dismissed).");

        admin.MapGet("/reports/{reportId:guid}", async (Guid reportId, ModerationService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAsync(reportId, ct)))
            .WithName("GetReport");

        admin.MapPost("/reports/{reportId:guid}/hide", async (Guid reportId, HideContentRequest request, HttpContext http, ModerationService service) =>
                TypedResults.Ok(await service.HideAsync(http.User, reportId, request, http.RequestAborted)))
            .WithValidation<HideContentRequest>()
            .WithName("HideReportedContent")
            .WithSummary("Hide the reported content with a reason (shown to the teacher); resolves every open report about it.");

        admin.MapPost("/reports/{reportId:guid}/dismiss", async (Guid reportId, ModerationDecisionRequest request, HttpContext http, ModerationService service) =>
                TypedResults.Ok(await service.DismissAsync(http.User, reportId, request, http.RequestAborted)))
            .WithValidation<ModerationDecisionRequest>()
            .WithName("DismissReport");

        admin.MapGet("/moderation/hidden", async (ModerationService service, CancellationToken ct) => TypedResults.Ok(await service.ListHiddenAsync(ct)))
            .WithName("ListHiddenContent");

        admin.MapPost("/moderation/restore", async (RestoreContentRequest request, ModerationService service, CancellationToken ct) =>
            {
                await service.RestoreAsync(request, ct);
                return TypedResults.NoContent();
            })
            .WithValidation<RestoreContentRequest>()
            .WithName("RestoreHiddenContent")
            .WithSummary("Show hidden content again, with a reason (audited; the teacher is told).");
    }
}

internal sealed class SubmitReportRequestValidator : AbstractValidator<SubmitReportRequest>
{
    public SubmitReportRequestValidator()
    {
        RuleFor(x => x.Target).Must(t => t is not null && ReportNames.Targets.ContainsKey(t)).WithMessage("Choose what you are reporting.");
        RuleFor(x => x.SectionType).NotEmpty().MaximumLength(AbuseReport.SectionTypeMaxLength).When(x => x.Target == "section");
        RuleFor(x => x.TestSlug).NotEmpty().MaximumLength(80).When(x => x.Target == "test");
        RuleFor(x => x.Reason).Must(r => r is not null && ReportNames.Reasons.ContainsKey(r)).WithMessage("Choose a reason.");
        RuleFor(x => x.Details).MaximumLength(AbuseReport.DetailsMaxLength);
        RuleFor(x => x.Details).NotEmpty().When(x => x.Reason == "other").WithMessage("Tell us what is wrong.");
        RuleFor(x => x.Email).MaximumLength(AbuseReport.EmailMaxLength).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Website).MaximumLength(200);
    }
}

/// <summary>Dismiss: an optional note.</summary>
internal sealed class ModerationDecisionRequestValidator : AbstractValidator<ModerationDecisionRequest>
{
    public ModerationDecisionRequestValidator() => RuleFor(x => x.Reason).MaximumLength(AbuseReport.NoteMaxLength);
}

/// <summary>Hide: the reason is required (the teacher sees it).</summary>
internal sealed class HideContentRequestValidator : AbstractValidator<HideContentRequest>
{
    public HideContentRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 3 and <= AbuseReport.NoteMaxLength)
            .WithMessage($"Give a reason (3 to {AbuseReport.NoteMaxLength} characters); the teacher sees it.");
}

internal sealed class RestoreContentRequestValidator : AbstractValidator<RestoreContentRequest>
{
    public RestoreContentRequestValidator()
    {
        RuleFor(x => x.WorkspaceId).NotNull();
        RuleFor(x => x.Target).Must(t => t is not null && ReportNames.Targets.ContainsKey(t)).WithMessage("Unknown target.");
        RuleFor(x => x.SectionType).NotEmpty().MaximumLength(AbuseReport.SectionTypeMaxLength).When(x => x.Target == "section");
        RuleFor(x => x.TestId).NotNull().When(x => x.Target == "test");
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 3 and <= AbuseReport.NoteMaxLength)
            .WithMessage($"Give a reason (3 to {AbuseReport.NoteMaxLength} characters).");
    }
}
