using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Infrastructure.Web;

/// <summary>
/// CSRF protection for cookie-authenticated JSON endpoints: every unsafe request (POST, PUT, PATCH,
/// DELETE) must send the antiforgery request token in the <c>X-XSRF-TOKEN</c> header, matching the
/// HttpOnly antiforgery cookie (ASP.NET Core antiforgery). The SPA gets the request token from the
/// response of <c>GET /api/auth/antiforgery</c> (body and header) and keeps it in memory; no
/// JavaScript-readable cookie is used. Safe methods pass through.
/// </summary>
public sealed class AntiforgeryHeaderFilter : IEndpointFilter
{
    /// <summary>Header carrying the request token (sent by the SPA, and returned when tokens are issued).</summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>HttpOnly cookie with the antiforgery cookie token.</summary>
    public const string CookieTokenCookieName = ".ClassHub.Antiforgery";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) ||
            HttpMethods.IsOptions(http.Request.Method) || HttpMethods.IsTrace(http.Request.Method))
        {
            return await next(context);
        }

        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The antiforgery token is missing or invalid. Reload the page and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.AntiforgeryInvalid });
        }

        return await next(context);
    }
}

public static class AntiforgeryEndpointExtensions
{
    /// <summary>Requires a valid antiforgery header on unsafe requests to all endpoints of the group.</summary>
    public static RouteGroupBuilder RequireAntiforgeryHeader(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.AddEndpointFilter<AntiforgeryHeaderFilter>();
        return group;
    }

    /// <summary>Requires a valid antiforgery header on an unsafe request to this endpoint.</summary>
    public static RouteHandlerBuilder RequireAntiforgeryHeader(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<AntiforgeryHeaderFilter>();
    }

    /// <summary>
    /// Issues a fresh antiforgery token pair for the current user: stores the HttpOnly cookie token and
    /// returns the request token, which is also sent in the <c>X-XSRF-TOKEN</c> response header. Call
    /// after sign-in/sign-out because tokens are bound to the user. The response must not be cached.
    /// </summary>
    public static string IssueAntiforgeryTokens(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(http);
        var requestToken = tokens.RequestToken ?? string.Empty;
        http.Response.Headers[AntiforgeryHeaderFilter.HeaderName] = requestToken;
        http.Response.Headers.CacheControl = "no-store";
        return requestToken;
    }
}
