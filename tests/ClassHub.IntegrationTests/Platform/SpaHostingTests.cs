using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>R0-P6: the built SPA is served from the API origin; only SPA areas fall back to index.html.</summary>
public sealed class SpaHostingTests : IClassFixture<SpaHostingTests.Host>
{
    private const string IndexMarker = "<!-- classhub-spa-index -->";
    private readonly Host _host;

    public SpaHostingTests(Host host) => _host = host;

    /// <summary>A host whose web root contains a fake built SPA (dist/index.html and one hashed asset).</summary>
    public sealed class Host : IDisposable
    {
        public string WebRoot { get; } = Directory.CreateTempSubdirectory("classhub-webroot-").FullName;

        public PlatformApiFactory Factory { get; }

        public Host()
        {
            Directory.CreateDirectory(Path.Combine(WebRoot, "dist", "assets"));
            File.WriteAllText(Path.Combine(WebRoot, "dist", "index.html"), $"<!doctype html><html><body>{IndexMarker}<div id=\"root\"></div></body></html>");
            File.WriteAllText(Path.Combine(WebRoot, "dist", "assets", "index-abc123.js"), "console.log('spa')");
            Directory.CreateDirectory(Path.Combine(WebRoot, "dist", ".vite"));
            File.WriteAllText(Path.Combine(WebRoot, "dist", ".vite", "manifest.json"),
                """{ "src/public/public.css": { "file": "assets/public-abc123.css", "isEntry": true } }""");
            Factory = new PlatformApiFactory(includeProbeModule: false, settings: new Dictionary<string, string?>
            {
                [WebHostDefaults.WebRootKey] = WebRoot,
            });
        }

        public void Dispose()
        {
            Factory.Dispose();
            try
            {
                Directory.Delete(WebRoot, recursive: true);
            }
            catch (IOException)
            {
                // A file may still be open briefly; the temp folder is harmless.
            }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/app")]
    [InlineData("/app/settings")]
    [InlineData("/student")]
    [InlineData("/admin/users/42")]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/verify-email")]
    [InlineData("/reset-password")]
    public async Task Spa_areas_are_served_index_html_without_caching(string path)
    {
        using var client = _host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(IndexMarker);
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
        string.Join(",", response.Headers.GetValues("Content-Security-Policy")).ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task Hashed_assets_are_cached_immutably()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/dist/assets/index-abc123.js", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldContain("immutable");
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/auth/me")]
    public async Task Api_routes_never_fall_back_to_the_spa(string path)
    {
        using var client = _host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain(IndexMarker);
    }

    [Theory]
    [InlineData("/", "mk")]
    [InlineData("/en", "en")]
    public async Task The_root_is_the_server_rendered_landing_page_with_the_built_public_stylesheet(string path, string language)
    {
        using var client = _host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldNotContain(IndexMarker);
        html.ShouldContain($"<html lang=\"{language}\">");
        html.ShouldContain("<link rel=\"stylesheet\" href=\"/dist/assets/public-abc123.css\" />");
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ClassHub.Api.Platform.SecurityHeadersMiddleware.PublicPageContentSecurityPolicy]);
    }

    [Fact]
    public async Task Paths_outside_the_spa_areas_are_not_answered_with_the_spa()
    {
        // Top-level paths belong to public teacher sites (/{slug}, Razor Pages).
        using var client = _host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/ana-math", UriKind.Relative), Ct);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain(IndexMarker);
    }
}
