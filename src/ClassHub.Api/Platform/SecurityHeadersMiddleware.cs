namespace ClassHub.Api.Platform;

/// <summary>
/// Adds security headers to every response. API and health responses get a deny-all CSP; pages get a
/// same-origin CSP. A response that already set a CSP (for example a page that needs a nonce) keeps it.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    /// <summary>CSP for JSON/API responses: nothing may load or frame them.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    /// <summary>CSP for HTML pages (SPA and Razor public pages): same-origin resources only.</summary>
    public const string PageContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;
            var isApi = path.StartsWithSegments("/api") || path.StartsWithSegments("/health") || path.StartsWithSegments("/openapi");

            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

            // The development-only API reference UI (Scalar) loads its own scripts; no CSP there.
            var isDevApiReference = environment.IsDevelopment() && path.StartsWithSegments("/scalar");
            if (!isDevApiReference && !headers.ContainsKey("Content-Security-Policy"))
            {
                headers.ContentSecurityPolicy = isApi ? ApiContentSecurityPolicy : PageContentSecurityPolicy;
            }

            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers.XFrameOptions = isApi ? "DENY" : "SAMEORIGIN";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
