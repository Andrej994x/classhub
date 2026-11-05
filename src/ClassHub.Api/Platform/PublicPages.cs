using System.Text.RegularExpressions;
using ClassHub.Core.Modules.Sites.Contracts;

namespace ClassHub.Api.Platform;

/// <summary>
/// Marks a server-rendered public page (landing page, teacher sites). Such responses get the strict public CSP (no
/// inline script or style, no framing) instead of the SPA page policy.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PublicPageAttribute : Attribute;

/// <summary>A public site page whose successful responses count as page views (D-32).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CountsSitePageViewsAttribute : Attribute;

/// <summary>
/// Counts views of public site pages after the response, also for responses served from the output cache (it runs
/// before the cache middleware). D-32: a counter per site and UTC day only. Not counted: non-GET requests, error
/// responses, signed-in users (mostly the teacher checking the page), prefetches, and requests without a browser-like
/// user agent (crawlers, link previews, monitors, scripts).
/// </summary>
internal sealed partial class SitePageViewMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPublicSites sites)
    {
        await next(context);

        if (!HttpMethods.IsGet(context.Request.Method)
            || context.Response.StatusCode != StatusCodes.Status200OK
            || context.GetEndpoint()?.Metadata.GetMetadata<CountsSitePageViewsAttribute>() is null
            || context.User.Identity?.IsAuthenticated == true
            || !IsLikelyVisitor(context.Request)
            || context.Request.RouteValues["slug"] is not string slug)
        {
            return;
        }

        // The response is complete; counting must not depend on the client still listening.
        await sites.RecordViewAsync(slug, CancellationToken.None);
    }

    internal static bool IsLikelyVisitor(HttpRequest request)
    {
        var userAgent = request.Headers.UserAgent.ToString();
        if (userAgent.Length == 0 || NonVisitorAgents().IsMatch(userAgent))
        {
            return false;
        }

        var purpose = $"{request.Headers["Sec-Purpose"]} {request.Headers["Purpose"]} {request.Headers["X-Moz"]}";
        return !purpose.Contains("prefetch", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"bot|crawl|spider|slurp|facebookexternalhit|embedly|preview|monitor|headless|lighthouse|curl|wget|python|java/|go-http|okhttp|httpclient|axios|node-fetch|postman",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonVisitorAgents();
}
