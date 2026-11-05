using ClassHub.Api.Platform;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Pages.Public;

/// <summary>The marketing landing page: <c>/</c> (Macedonian) and <c>/en</c> (English). Static and cacheable.</summary>
[AllowAnonymous]
[PublicPage]
[OutputCache(PolicyName = PublicSiteCaching.StaticPolicy, Tags = new[] { PublicSiteCaching.LandingTag })]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public sealed class LandingModel(IOptions<AppLinkOptions> links) : PageModel
{
    public string Language { get; private set; } = "mk";

    public string BaseUrl => links.Value.PublicBaseUrl.TrimEnd('/');

    public string Text(string key) => PublicText.Get(Language, key);

    // Route only: a query value (?lang=en) must never change the shared cached page for "/".
    public void OnGet([FromRoute] string? lang)
    {
        Language = lang == "en" ? "en" : "mk";
        Response.Headers[HeaderNames.CacheControl] = "no-cache";
    }
}
