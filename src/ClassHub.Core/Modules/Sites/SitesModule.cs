using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Sites;

/// <summary>The teacher's public site (M02): model, draft editing and preview (R1-P3); rendering and publishing in R1-P4.</summary>
public sealed class SitesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SiteService>();
        services.AddScoped<SiteSlugQueries>();
        services.AddScoped<SitePreviewTokens>();
        services.AddScoped<ISitePreview, SitePreview>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/site").WithTags("Site").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.SiteRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.SiteManage);

        site.MapGet("/", async (SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.GetAsync(ct)))
            .RequireAuthorization(read).WithName("GetSite")
            .WithSummary("The site draft, or suggestions to start from when it does not exist yet (exists = false).");

        site.MapPut("/", async (SaveSiteRequest request, SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.SaveAsync(request, ct)))
            .WithValidation<SaveSiteRequest>()
            .RequireAuthorization(manage).WithName("SaveSite")
            .WithSummary("Save the draft (slug, template, palette, language, sections). Visitors see nothing until publishing (R1-P4).");

        site.MapGet("/slug-availability", async (string? slug, SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.CheckSlugAsync(slug, ct)))
            .RequireAuthorization(read).WithName("CheckSiteSlug");

        site.MapPost("/preview", async (SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.CreatePreviewAsync(ct)))
            .RequireAuthorization(read).WithName("CreateSitePreview")
            .WithSummary("A signed preview URL for the current draft, valid for 15 minutes.");
    }
}
