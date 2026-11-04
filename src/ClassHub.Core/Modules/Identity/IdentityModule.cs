using ClassHub.Core.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Identity;

/// <summary>
/// Authentication endpoints (<c>/api/auth</c>). R0-P3 provides the antiforgery token endpoint; accounts,
/// sign-in and password flows are added in R0-P4.
/// </summary>
public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth").WithTags("Auth");

        // Issues the antiforgery token pair the SPA needs before any unsafe request.
        auth.MapGet("/antiforgery", (HttpContext http) =>
            {
                http.IssueAntiforgeryTokens();
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issue antiforgery tokens (XSRF-TOKEN cookie).");
    }
}
