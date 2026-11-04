using ClassHub.Core.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Identity;

/// <summary>Response of <c>GET /api/auth/antiforgery</c>: the request token for the <c>X-XSRF-TOKEN</c> header.</summary>
public sealed record AntiforgeryTokenResponse(string RequestToken);

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

        // Issues the antiforgery token the SPA must send (X-XSRF-TOKEN) on every unsafe request.
        auth.MapGet("/antiforgery", (HttpContext http) =>
                TypedResults.Ok(new AntiforgeryTokenResponse(http.IssueAntiforgeryTokens())))
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issue an antiforgery request token (also in the X-XSRF-TOKEN response header).");
    }
}
