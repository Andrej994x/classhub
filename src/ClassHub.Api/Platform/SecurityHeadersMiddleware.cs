namespace ClassHub.Api.Platform;

/// <summary>
/// Adds security headers to every response. API and health responses get a deny-all CSP; public Razor pages
/// (<see cref="PublicPageAttribute"/>) get a strict CSP without inline styles or framing; other pages (SPA, preview)
/// get a same-origin CSP. A response that already set a CSP keeps it.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    /// <summary>CSP for JSON/API responses: nothing may load or frame them.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    /// <summary>CSP for HTML pages (SPA and Razor public pages): same-origin resources only.</summary>
    public const string PageContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    /// <summary>
    /// CSP for public pages (landing page, teacher sites): only same-origin scripts (islands) and stylesheets, no inline
    /// code or styles, no plugins, never framed. JSON-LD data blocks are not executed and need no exception.
    /// </summary>
    public const string PublicPageContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; " +
        "manifest-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;
            var isApi = path.StartsWithSegments("/api") || path.StartsWithSegments("/health") || path.StartsWithSegments("/openapi");
            var isPublicPage = !isApi && context.GetEndpoint()?.Metadata.GetMetadata<PublicPageAttribute>() is not null;

            headers.XContentTypeOptions = "nosniff";
            // A page may tighten it (the token-bearing site preview sends no referrer at all).
            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

            // The development-only API reference UI (Scalar) loads its own scripts; no CSP there.
            var isDevApiReference = environment.IsDevelopment() && path.StartsWithSegments("/scalar");
            if (!isDevApiReference && !headers.ContainsKey("Content-Security-Policy"))
            {
                headers.ContentSecurityPolicy = isApi ? ApiContentSecurityPolicy : isPublicPage ? PublicPageContentSecurityPolicy : PageContentSecurityPolicy;
            }

            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers.XFrameOptions = isApi || isPublicPage ? "DENY" : "SAMEORIGIN";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
