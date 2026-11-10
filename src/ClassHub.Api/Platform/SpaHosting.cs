using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Platform;

/// <summary>
/// Serves the built React SPA (web/ → wwwroot/dist) from the API's origin, so cookies, antiforgery and CSP
/// work without CORS. Only the SPA's own areas fall back to index.html; /api, health checks and the Razor public
/// pages (landing page at /, teacher sites at /{slug}) are never answered with the SPA.
/// </summary>
internal static class SpaHosting
{
    /// <summary>Path prefixes owned by the SPA (must match web/src/app/routes.tsx and the reserved slugs).</summary>
    public static readonly string[] Prefixes =
        ["/app", "/student", "/admin", "/login", "/register", "/verify-email", "/forgot-password", "/reset-password"];

    /// <summary>The dev mailbox page; mapped only where the dev mailbox API exists (Development, Testing, E2E).</summary>
    public const string DevMailboxPrefix = "/dev/mailbox";

    public const string IndexFile = "dist/index.html";

    /// <summary>The service worker (R3-P7); it lives under /dist/ but controls the SPA areas /app/ and /student/.</summary>
    public const string ServiceWorkerFile = "/dist/sw.js";

    /// <summary>
    /// Static files (before routing/authorization): hashed assets are immutable, everything else (index.html, the
    /// service worker, manifests, the offline page) is revalidated on every use.
    /// </summary>
    public static WebApplication UseClassHubStaticFiles(this WebApplication app)
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path;
                var headers = context.Context.Response.Headers;
                headers[HeaderNames.CacheControl] =
                    path.StartsWithSegments("/dist/assets") ? "public, max-age=31536000, immutable" : "no-cache";
                if (path.Equals(ServiceWorkerFile, StringComparison.Ordinal))
                {
                    // Lets the worker under /dist/ be registered for the SPA scopes; pwa.ts registers only /app/ and /student/.
                    headers["Service-Worker-Allowed"] = "/";
                }
            },
        });
        return app;
    }

    /// <summary>Maps the SPA entry point for every SPA prefix (anonymous: the data APIs are protected).</summary>
    public static WebApplication MapClassHubSpa(this WebApplication app)
    {
        var environment = app.Environment;
        var prefixes = environment.IsDevelopment() || environment.IsEnvironment("Testing") || environment.IsEnvironment("E2E")
            ? [.. Prefixes, DevMailboxPrefix]
            : Prefixes;
        foreach (var prefix in prefixes)
        {
            app.MapFallbackToFile($"{prefix}/{{**path}}", IndexFile, NoCache()).AllowAnonymous();
        }

        return app;
    }

    private static StaticFileOptions NoCache() => new()
    {
        OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
    };
}
