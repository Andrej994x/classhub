using ClassHub.Core.Modules.Sites.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Pages;

/// <summary>
/// Read-only preview of a site draft for the editor's iframe, rendered by the same partial as the public page. Access
/// is by signed, short-lived token only (no session needed); invalid or expired tokens get a plain 404. Never indexed,
/// never cached (no output-cache policy, <c>no-store</c>).
/// </summary>
[AllowAnonymous]
public sealed class PreviewModel(ISitePreview preview) : PageModel
{
    public SiteRenderModel Site { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer"; // the token is in the URL
        var model = await preview.ReadAsync(token, cancellationToken);
        if (model is null)
        {
            return NotFound();
        }

        Site = model;
        return Page();
    }
}
