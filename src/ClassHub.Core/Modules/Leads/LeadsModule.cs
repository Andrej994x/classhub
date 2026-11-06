using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Leads.Application;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Leads;

/// <summary>Leads (M03): the public contact/lesson-request intake and the teacher's inbox (R1-P5).</summary>
public sealed class LeadsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LeadOptions>().Bind(configuration.GetSection(LeadOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<LeadQueries>();
        services.AddScoped<LeadService>();
        services.AddScoped<ILeadConversion, LeadConversion>();
        services.AddScoped<ILeadStats, LeadStatsService>();
        services.AddScoped<LeadIntake>();
        services.AddScoped<LeadFormTokens>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Public, anonymous: no session and no antiforgery header (nothing here uses the visitor's cookies).
        var site = endpoints.MapGroup("/public/sites/{slug:siteslug}").WithTags("Public leads").AllowAnonymous();

        site.MapGet("/lead-form", async (string slug, HttpContext http, LeadIntake intake, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return TypedResults.Ok(await intake.GetFormAsync(slug, ct));
            })
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("GetLeadForm")
            .WithSummary("Contact form data of a published site: availability, a signed form token, offers, subjects and the consent text.");

        site.MapPost("/leads", async (string slug, SubmitLeadRequest request, LeadIntake intake, CancellationToken ct) =>
            {
                await intake.SubmitAsync(slug, request, ct);
                return TypedResults.Accepted((string?)null, new LeadReceivedResponse(true));
            })
            .WithValidation<SubmitLeadRequest>()
            .RequireRateLimiting(RateLimitPolicies.PublicForm)
            .WithName("SubmitLead")
            .WithSummary("Send a message or lesson request to the teacher. Always 202 without echoing data; spam is dropped silently.");

        var leads = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/leads").WithTags("Leads").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.LeadsRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.LeadsManage);

        leads.MapGet("/", async (string? status, string? type, string? q, bool? archived, int? page, int? pageSize, LeadService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(status, type, q, archived ?? false, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(read).WithName("ListLeads")
            .WithSummary("The inbox, newest first: filter by status, type, text (name, email, message) and archived.");

        leads.MapGet("/{leadId:guid}", async (Guid leadId, LeadService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(leadId, ct)))
            .RequireAuthorization(read).WithName("GetLead");

        leads.MapPut("/{leadId:guid}/status", async (Guid leadId, ChangeLeadStatusRequest request, LeadService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ChangeStatusAsync(leadId, request, ct)))
            .WithValidation<ChangeLeadStatusRequest>()
            .RequireAuthorization(manage).WithName("ChangeLeadStatus")
            .WithSummary("New → Contacted → Converted, close at any point, reopen a closed lead. 422 INVALID_STATUS_TRANSITION; 409 stale version.");

        leads.MapPost("/{leadId:guid}/notes", async (Guid leadId, AddLeadNoteRequest request, LeadService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.AddNoteAsync(leadId, request, ct)))
            .WithValidation<AddLeadNoteRequest>()
            .RequireAuthorization(manage).WithName("AddLeadNote");

        leads.MapPost("/{leadId:guid}/archive", async (Guid leadId, LeadVersionRequest request, LeadService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(leadId, request, archived: true, ct)))
            .WithValidation<LeadVersionRequest>()
            .RequireAuthorization(manage).WithName("ArchiveLead");

        leads.MapPost("/{leadId:guid}/restore", async (Guid leadId, LeadVersionRequest request, LeadService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(leadId, request, archived: false, ct)))
            .WithValidation<LeadVersionRequest>()
            .RequireAuthorization(manage).WithName("RestoreLead");
    }
}
