using ClassHub.Api.Platform;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Pages.Public;

/// <summary>
/// A teacher's published site at <c>/{slug}</c>. Unknown, unpublished and hidden sites (suspended owner) answer 404
/// with <c>noindex</c>. Anonymous responses are output-cached (tagged by workspace, evicted on publish and on changes
/// that show on the page); authenticated requests are never served from or stored in the cache. Nothing on the page
/// depends on the viewer.
/// </summary>
[AllowAnonymous]
[PublicPage]
[CountsSitePageViews]
[OutputCache(PolicyName = PublicSiteCaching.SitePolicy)]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public sealed class SiteModel(IPublicSites sites) : PageModel
{
    public PublicSitePage? Current { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        // Browsers and proxies revalidate; the server-side output cache does the caching (and can be evicted).
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        var page = await sites.FindAsync(slug, cancellationToken);
        if (page is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            Response.Headers["X-Robots-Tag"] = "noindex";
            return Page();
        }

        HttpContext.Features.Get<IOutputCacheFeature>()?.Context.Tags.Add(page.CacheTag);
        Current = page;
        return Page();
    }
}
