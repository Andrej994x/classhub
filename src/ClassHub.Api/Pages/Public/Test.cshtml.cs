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
/// The indexable intro of a published diagnostic test at <c>/{slug}/tests/{testSlug}</c>; the island runs the test.
/// 404 + noindex when the site or the test is not public. Cached like the site page (tagged by workspace).
/// </summary>
[AllowAnonymous]
[PublicPage]
[OutputCache(PolicyName = PublicSiteCaching.TestPolicy)]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public sealed class TestModel(IPublicSites sites) : PageModel
{
    public PublicTestPage? Current { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, [FromRoute] string testSlug, CancellationToken cancellationToken)
    {
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        var page = await sites.FindTestAsync(slug, testSlug, cancellationToken);
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
