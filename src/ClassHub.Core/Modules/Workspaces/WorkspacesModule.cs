using System.Security.Claims;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Workspaces.Application;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Persistence;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Workspaces;

/// <summary>Teacher workspaces (tenants): provisioning, membership lookup and workspace settings.</summary>
public sealed class WorkspacesModule : IModule
{
    /// <summary>Route prefix of every workspace-scoped API: <c>/api/w/{workspaceId}</c>.</summary>
    public const string WorkspaceRoute = "/w/{workspaceId:guid}";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WorkspaceAccessQueries>();
        services.AddScoped<IWorkspaceMembershipLookup>(sp => sp.GetRequiredService<WorkspaceAccessQueries>());
        services.AddScoped<IWorkspaceProvisioning, WorkspaceProvisioning>();
        services.AddScoped<WorkspaceService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me/workspaces", async (ClaimsPrincipal user, WorkspaceAccessQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListForUserAsync(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct)))
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithTags("Workspaces")
            .WithName("ListMyWorkspaces");

        var workspace = endpoints.MapGroup(WorkspaceRoute).WithTags("Workspaces").RequireAntiforgeryHeader();

        workspace.MapGet("/", async (WorkspaceService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .RequireAuthorization(WorkspacePolicies.Read)
            .WithName("GetWorkspace");

        workspace.MapPatch("/", async (UpdateWorkspaceRequest request, WorkspaceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(request, ct)))
            .WithValidation<UpdateWorkspaceRequest>()
            .RequireAuthorization(WorkspacePolicies.Manage)
            .WithName("UpdateWorkspace");
    }
}
