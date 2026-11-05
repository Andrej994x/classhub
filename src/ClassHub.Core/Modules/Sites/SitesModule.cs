using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Sites;

/// <summary>The teacher's public site (M02): model, draft editing and preview (R1-P3), publishing and public pages (R1-P4).</summary>
public sealed class SitesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SiteService>();
        services.AddScoped<SiteStatus>();
        services.AddScoped<ISiteStatus>(sp => sp.GetRequiredService<SiteStatus>());
        services.AddScoped<ISitePageViewStats>(sp => sp.GetRequiredService<SiteStatus>());
        services.AddScoped<SiteSlugQueries>();
        services.AddScoped<SitePreviewTokens>();
        services.AddScoped<ISitePreview, SitePreview>();
        services.AddScoped<SiteRenderer>();
        services.AddScoped<PublicSiteQueries>();
        services.AddScoped<ISiteDirectory, SiteDirectoryQueries>();
        services.AddScoped<ISiteModeration, SiteModerationQueries>();
        services.AddScoped<IPublicSites, PublicSites>();
        services.AddScoped<PublicSiteCache>();
        services.AddScoped<IPublicSiteCache>(sp => sp.GetRequiredService<PublicSiteCache>());

        // "{slug:siteslug}": only well-formed, non-reserved slugs reach the public page routes, so app areas such as
        // /login or /app never match them.
        services.Configure<RouteOptions>(options => options.SetParameterPolicy<SiteSlugRouteConstraint>(SiteSlugRouteConstraint.Name));

        // In-memory output cache for anonymous public pages. The base rules of the default policy stay: only GET/HEAD
        // 200 responses of unauthenticated requests without Set-Cookie are stored.
        services.AddOutputCache(options =>
        {
            options.SizeLimit = 64L * 1024 * 1024;
            options.AddPolicy(PublicSiteCaching.SitePolicy, policy => policy
                .Expire(PublicSiteCaching.SiteLifetime)
                .SetVaryByRouteValue("slug")
                .SetVaryByQuery(PublicSiteCaching.NoQueryKey)
                .Tag(PublicSiteCaching.AllSitesTag));
            options.AddPolicy(PublicSiteCaching.TestPolicy, policy => policy
                .Expire(PublicSiteCaching.SiteLifetime)
                .SetVaryByRouteValue("slug", "testSlug")
                .SetVaryByQuery(PublicSiteCaching.NoQueryKey)
                .Tag(PublicSiteCaching.AllSitesTag));
            options.AddPolicy(PublicSiteCaching.StaticPolicy, policy => policy
                .Expire(PublicSiteCaching.StaticLifetime)
                .SetVaryByQuery(PublicSiteCaching.NoQueryKey));
            options.AddPolicy(PublicSiteCaching.SitemapPolicy, policy => policy
                .Expire(PublicSiteCaching.StaticLifetime)
                .SetVaryByQuery(PublicSiteCaching.NoQueryKey)
                .Tag(PublicSiteCaching.SitemapTag));
        });
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
            .WithSummary("Save the draft (slug, template, palette, language, sections). Visitors see the draft only after publishing.");

        site.MapPost("/publish", async (PublishSiteRequest request, SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.PublishAsync(request, ct)))
            .RequireAuthorization(manage).WithName("PublishSite")
            .WithSummary("Publish the saved draft at /{slug}. 422 SITE_NOT_CREATED before the first save; 409 when the version is stale.");

        site.MapPost("/unpublish", async (PublishSiteRequest request, SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.UnpublishAsync(request, ct)))
            .RequireAuthorization(manage).WithName("UnpublishSite")
            .WithSummary("Take the public page offline (404). The draft is kept.");

        site.MapGet("/slug-availability", async (string? slug, SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.CheckSlugAsync(slug, ct)))
            .RequireAuthorization(read).WithName("CheckSiteSlug");

        site.MapPost("/preview", async (SiteService sites, CancellationToken ct) => TypedResults.Ok(await sites.CreatePreviewAsync(ct)))
            .RequireAuthorization(read).WithName("CreateSitePreview")
            .WithSummary("A signed preview URL for the current draft, valid for 15 minutes.");
    }
}
