using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Infrastructure.Web;

/// <summary>
/// CSRF protection for cookie-authenticated JSON endpoints: every unsafe request (POST, PUT, PATCH,
/// DELETE) must send the request token from the <c>XSRF-TOKEN</c> cookie in the <c>X-XSRF-TOKEN</c>
/// header (ASP.NET Core antiforgery double-submit). Safe methods pass through.
/// </summary>
public sealed class AntiforgeryHeaderFilter : IEndpointFilter
{
    /// <summary>Header the SPA sends with the request token.</summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>JavaScript-readable cookie that carries the request token to the SPA.</summary>
    public const string RequestTokenCookieName = "XSRF-TOKEN";

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
    /// Issues a fresh antiforgery token pair: the HttpOnly cookie token and the JavaScript-readable
    /// <c>XSRF-TOKEN</c> request-token cookie. Call after sign-in/sign-out because tokens are bound to the user.
    /// </summary>
    public static void IssueAntiforgeryTokens(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(http);
        http.Response.Cookies.Append(AntiforgeryHeaderFilter.RequestTokenCookieName, tokens.RequestToken ?? string.Empty, new CookieOptions
        {
            HttpOnly = false,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
        http.Response.Headers.CacheControl = "no-store";
    }
}
