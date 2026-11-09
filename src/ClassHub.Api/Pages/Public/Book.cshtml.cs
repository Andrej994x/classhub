using ClassHub.Api.Platform;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Pages.Public;

/// <summary>
/// The booking page of a published site at <c>/{slug}/book</c> (R2-P6): the page frame only; the booking island loads the
/// offers and free times from the API. Not indexed (the free times change all the time); 404 + noindex for unknown sites.
/// </summary>
[AllowAnonymous]
[PublicPage]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public sealed class BookModel(IPublicSites sites) : PageModel
{
    public PublicSitePage? Current { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, CancellationToken cancellationToken)
    {
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
        Response.Headers["X-Robots-Tag"] = "noindex";
        var page = await sites.FindAsync(slug, cancellationToken);
        if (page is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        Current = page;
        return Page();
    }
}
