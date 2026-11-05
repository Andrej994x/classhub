using System.Globalization;
using System.Text;
using System.Xml;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Api.Platform;

/// <summary>
/// Root-level public endpoints next to the Razor pages: the public site photo, the preview photo, sitemap.xml and
/// robots.txt. Absolute URLs always come from the configured public base URL, never from the request's Host header.
/// </summary>
internal static class PublicSiteEndpoints
{
    public static WebApplication MapPublicSiteEndpoints(this WebApplication app)
    {
        // The hero photo of a published site (only while the published hero shows it). The URL carries the photo
        // version, so a changed photo is a new URL; an hour of browser caching bounds how long an unpublished photo
        // stays in caches.
        app.MapGet("/{slug:siteslug}/photo", async Task<IResult> (string slug, HttpContext http, IPublicSites sites, CancellationToken ct) =>
            {
                if (await sites.OpenPhotoAsync(slug, ct) is not { } photo)
                {
                    return Results.NotFound();
                }

                http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=3600";
                return Results.Stream(photo.Content, photo.ContentType);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .ExcludeFromDescription();

        // The hero photo of a site preview, authorized by the same signed token as the page.
        app.MapGet("/preview/{token}/photo", async Task<IResult> (string token, HttpContext http, ISitePreview preview, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers["Referrer-Policy"] = "no-referrer";
                http.Response.Headers["X-Robots-Tag"] = "noindex";
                return await preview.OpenPhotoAsync(token, ct) is { } photo ? Results.Stream(photo.Content, photo.ContentType) : Results.NotFound();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();

        app.MapGet("/sitemap.xml", async (IPublicSites sites, IOptions<AppLinkOptions> links, CancellationToken ct) =>
                Results.Text(Sitemap(links.Value.PublicBaseUrl, await sites.ListForSitemapAsync(ct)), "application/xml", Encoding.UTF8))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .CacheOutput(PublicSiteCaching.SitemapPolicy)
            .ExcludeFromDescription();

        app.MapGet("/robots.txt", (IOptions<AppLinkOptions> links) => Results.Text(Robots(links.Value.PublicBaseUrl), "text/plain", Encoding.UTF8))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .CacheOutput(PublicSiteCaching.StaticPolicy)
            .ExcludeFromDescription();

        return app;
    }

    internal static string Sitemap(string publicBaseUrl, IReadOnlyList<SitemapEntry> entries)
    {
        var baseUrl = publicBaseUrl.TrimEnd('/');
        var output = new StringBuilder();
        using (var xml = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            void Url(string location, DateTime? lastModified)
            {
                xml.WriteStartElement("url");
                xml.WriteElementString("loc", location);
                if (lastModified is { } date)
                {
                    xml.WriteElementString("lastmod", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                xml.WriteEndElement();
            }

            Url($"{baseUrl}/", null);
            Url($"{baseUrl}/en", null);
            foreach (var entry in entries)
            {
                Url($"{baseUrl}/{entry.Slug}", entry.LastModifiedUtc);
                foreach (var test in entry.Tests ?? [])
                {
                    Url($"{baseUrl}/{entry.Slug}/tests/{test.Slug}", test.LastModifiedUtc);
                }
            }

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        // StringBuilder output declares UTF-16; the response is UTF-8.
        return output.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// App areas are excluded. Each rule is "/area/" plus "/area$": a plain "/app" would be a prefix rule that also
    /// blocks teacher sites such as /applied-math.
    /// </summary>
    internal static string Robots(string publicBaseUrl)
    {
        var builder = new StringBuilder("User-agent: *\n");
        foreach (var area in SpaHosting.Prefixes.Concat(["/api", "/preview", "/dev"]))
        {
            builder.Append("Disallow: ").Append(area).Append("/\n");
            builder.Append("Disallow: ").Append(area).Append("$\n");
        }

        builder.Append("\nSitemap: ").Append(publicBaseUrl.TrimEnd('/')).Append("/sitemap.xml\n");
        return builder.ToString();
    }
}
