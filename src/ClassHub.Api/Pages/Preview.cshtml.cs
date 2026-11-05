using ClassHub.Core.Modules.Sites.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Pages;

/// <summary>
/// Read-only preview of a site draft for the editor's iframe (R1-P3). Access is by signed, short-lived token only
/// (no session needed); invalid or expired tokens get a plain 404. Never indexed, never cached. Full templates come
/// with the public sites (R1-P4); this page shows the draft's structure, texts, colours and language.
/// </summary>
[AllowAnonymous]
public sealed class PreviewModel(ISitePreview preview) : PageModel
{
    public SitePreviewModel Site { get; private set; } = null!;

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

    public static string Label(string language, string key) => (language, key) switch
    {
        ("en", "preview") => "Preview — not published",
        (_, "preview") => "Преглед — не е објавено",
        ("en", "subjects") => "Your subjects from the profile appear here.",
        (_, "subjects") => "Тука се прикажуваат предметите од профилот.",
        ("en", "offers") => "Your offers marked “show on public site” appear here.",
        (_, "offers") => "Тука се прикажуваат понудите означени за јавната страница.",
        ("en", "contact") => "The contact form appears here once the page is published.",
        (_, "contact") => "Формуларот за контакт се прикажува кога страницата ќе се објави.",
        _ => key,
    };
}
