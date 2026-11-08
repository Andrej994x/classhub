using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassHub.Api.Pages;
using ClassHub.Api.Platform;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClassHub.UnitTests.Sites;

/// <summary>R1-P4: SEO head, public texts, palettes, robots/sitemap, page-view filter, Vite assets and the render allow-list.</summary>
public sealed partial class PublicSiteRulesTests
{
    private static SiteRenderModel Model(
        string? name = "Ана Петровска",
        string? heroTitle = "Часови по математика",
        string? heroText = "Помагам на учениците.",
        IReadOnlyList<SiteSectionItem>? faq = null,
        IReadOnlyList<SiteOfferView>? offers = null,
        string? photo = "/ana/photo?v=abc") =>
        new("ana", "classic", SitePalettes.All[0], "mk",
            [
                new SiteSection(SiteSectionTypes.Hero, true, heroTitle, heroText, ShowPhoto: true),
                new SiteSection(SiteSectionTypes.About, true, "За мене", "Предавам 10 години."),
                .. faq is null ? Array.Empty<SiteSection>() : [new SiteSection(SiteSectionTypes.Faq, true, "ЧПП", null, Items: faq)],
            ],
            name, photo, new SiteTeaching(["Математика"], [], [], "Online"),
            offers ?? [new SiteOfferView("Час", null, "SingleLesson", "Математика", 60, 1200.00m, "MKD", "Online", null, null)],
            []);

    [Fact]
    public void The_seo_head_uses_the_configured_base_url_and_the_visible_texts()
    {
        var seo = SiteSeo.Build(Model(), "https://classhub.app/");

        seo.Title.ShouldBe("Ана Петровска — Часови по математика");
        seo.Description.ShouldBe("Помагам на учениците.");
        seo.CanonicalUrl.ShouldBe("https://classhub.app/ana");
        seo.ImageUrl.ShouldBe("https://classhub.app/ana/photo?v=abc");
        seo.OgLocale.ShouldBe("mk_MK");
    }

    [Fact]
    public void Titles_and_descriptions_fall_back_and_are_cut_at_a_word_boundary()
    {
        var longText = string.Join(' ', Enumerable.Repeat("математика", 40));
        var seo = SiteSeo.Build(Model(name: null, heroText: null, photo: null) with { Sections = [new SiteSection(SiteSectionTypes.About, true, "За мене", longText)] }, "https://classhub.app");

        seo.Title.ShouldBe("ana");
        seo.ImageUrl.ShouldBeNull();
        seo.Description.Length.ShouldBeLessThanOrEqualTo(SiteSeo.DescriptionMaxLength);
        seo.Description.ShouldEndWith("математика…");
    }

    [Fact]
    public void Json_ld_describes_the_person_offers_and_faq_and_escapes_markup()
    {
        var seo = SiteSeo.Build(Model(heroText: "</script><script>alert(1)</script>", faq: [new SiteSectionItem("Онлајн?", null, "Да & <b>секако</b>")]), "https://classhub.app");

        seo.JsonLd.ShouldNotContain("<");
        seo.JsonLd.ShouldNotContain(">");
        seo.JsonLd.ShouldNotContain("&");
        seo.JsonLd.ShouldContain("Ана Петровска"); // Cyrillic stays readable
        using var json = JsonDocument.Parse(seo.JsonLd);
        var person = json.RootElement.GetProperty("@graph")[0];
        person.GetProperty("description").GetString().ShouldBe("</script><script>alert(1)</script>");
        var offer = person.GetProperty("makesOffer")[0];
        (offer.GetProperty("price").GetString(), offer.GetProperty("priceCurrency").GetString()).ShouldBe(("1200.00", "MKD"));
        offer.GetProperty("itemOffered").GetProperty("serviceType").GetString().ShouldBe("Математика");
        var answer = json.RootElement.GetProperty("@graph")[1].GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString();
        answer.ShouldBe("Да & <b>секако</b>");
    }

    [Theory]
    [InlineData("MKD", 120_000L, 2, "1200.00")]
    [InlineData("EUR", 4_999L, 2, "49.99")]
    [InlineData("JPY", 5_000L, 0, "5000")]
    [InlineData("RSD", 2_500L, 0, "2500")]
    [InlineData("KWD", 12_345L, 3, "12.345")]
    public void Prices_use_the_minor_units_of_their_currency(string currency, long minor, int digits, string major)
    {
        Currencies.MinorDigits(currency).ShouldBe(digits);
        Currencies.ToMajorUnits(minor, currency).ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(major);
    }

    [Fact]
    public void Public_prices_and_details_are_formatted_in_the_site_language_with_the_currency_code()
    {
        var offer = new SiteOfferView("Пакет", null, "Package", null, 60, 1200m, "MKD", "Hybrid", 10, null);

        SiteViewHelpers.Price(offer, "mk").ShouldBe("1.200,00 MKD");
        SiteViewHelpers.Price(offer, "en").ShouldBe("1,200.00 MKD");
        SiteViewHelpers.Details(offer, "en").ShouldBe("60 min · 10 lessons · Online and in person");
    }

    [Fact]
    public void Both_languages_have_the_same_public_texts()
    {
        PublicText.Mk.Keys.Order(StringComparer.Ordinal).ShouldBe(PublicText.En.Keys.Order(StringComparer.Ordinal));
        PublicText.Mk.Values.ShouldAllBe(v => !string.IsNullOrWhiteSpace(v));
        PublicText.En.Values.ShouldAllBe(v => !string.IsNullOrWhiteSpace(v));
    }

    [Fact]
    public void The_public_stylesheet_defines_every_palette_with_the_api_colours()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "web", "src", "public", "public.css"));
        foreach (var palette in SitePalettes.All)
        {
            var block = Regex.Match(css, $@"\.palette-{palette.Key}\s*\{{(?<body>[^}}]*)\}}").Groups["body"].Value;
            block.ShouldNotBeEmpty(palette.Key);
            string Value(string variable) => Regex.Match(block, $@"--site-{variable}:\s*(#[0-9a-fA-F]{{6}})").Groups[1].Value.ToUpperInvariant();
            (Value("primary"), Value("on-primary"), Value("bg"), Value("surface"), Value("text"), Value("muted"))
                .ShouldBe((palette.Primary, palette.OnPrimary, palette.Background, palette.Surface, palette.Text, palette.Muted), palette.Key);
        }
    }

    [Theory]
    [InlineData("ana-math", true)]
    [InlineData("applied-math", true)]
    [InlineData("app", false)]
    [InlineData("login", false)]
    [InlineData("en", false)]
    [InlineData("Ana", false)]
    [InlineData("favicon.ico", false)]
    public void The_slug_route_constraint_matches_only_site_addresses(string value, bool matches) =>
        new SiteSlugRouteConstraint().Match(null, null, "slug", new RouteValueDictionary { ["slug"] = value }, RouteDirection.IncomingRequest).ShouldBe(matches);

    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36", null, true)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", null, false)]
    [InlineData("facebookexternalhit/1.1", null, false)]
    [InlineData("Mozilla/5.0 HeadlessChrome/130.0", null, false)]
    [InlineData("curl/8.5.0", null, false)]
    [InlineData("", null, false)]
    [InlineData("Mozilla/5.0 Chrome/130.0", "prefetch;prerender", false)]
    public void Page_views_count_only_likely_visitors(string userAgent, string? secPurpose, bool counted)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = userAgent;
        if (secPurpose is not null)
        {
            context.Request.Headers["Sec-Purpose"] = secPurpose;
        }

        SitePageViewMiddleware.IsLikelyVisitor(context.Request).ShouldBe(counted);
    }

    [Fact]
    public void Robots_excludes_app_areas_without_blocking_similar_site_addresses()
    {
        var robots = PublicSiteEndpoints.Robots("https://classhub.app/");

        foreach (var area in new[] { "/app", "/student", "/admin", "/api", "/preview", "/login" })
        {
            robots.ShouldContain($"Disallow: {area}/\n");
            robots.ShouldContain($"Disallow: {area}$\n");
            robots.ShouldNotContain($"Disallow: {area}\n");
        }

        robots.ShouldEndWith("Sitemap: https://classhub.app/sitemap.xml\n");
    }

    [Fact]
    public void The_sitemap_is_valid_utf8_xml_with_absolute_urls()
    {
        var xml = PublicSiteEndpoints.Sitemap("https://classhub.app/", [new SitemapEntry("ana-math", new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc))]);

        xml.ShouldStartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        document.Descendants(ns + "loc").Select(l => l.Value).ShouldBe(["https://classhub.app/", "https://classhub.app/en", "https://classhub.app/ana-math"]);
        document.Descendants(ns + "lastmod").Single().Value.ShouldBe("2026-11-05");
    }

    /// <summary>
    /// The render model is the allow-list of what a public page can show. A new field must be added here on purpose
    /// (and checked: never account data, student data, notes, internal ids or anything unpublished).
    /// </summary>
    [Fact]
    public void The_public_render_model_exposes_only_the_allow_listed_fields()
    {
        static IEnumerable<string> Shape(Type type, string prefix) =>
            type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).SelectMany(p =>
            {
                var element = p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                    ? p.PropertyType.GetGenericArguments()[0]
                    : p.PropertyType;
                var name = $"{prefix}{p.Name}";
                return element.Namespace == typeof(SiteRenderModel).Namespace ? Shape(element, name + ".") : [name];
            });

        Shape(typeof(PublicSitePage), string.Empty).ShouldBe(
        [
            "CacheTag",
            "Seo.CanonicalUrl", "Seo.Description", "Seo.ImageUrl", "Seo.JsonLd", "Seo.OgLocale", "Seo.Title",
            "Site.BookingEnabled",
            "Site.Language",
            "Site.Offers.Currency", "Site.Offers.Description", "Site.Offers.DurationMinutes", "Site.Offers.Kind", "Site.Offers.LessonCount",
            "Site.Offers.Mode", "Site.Offers.Price", "Site.Offers.Seats", "Site.Offers.Subject", "Site.Offers.Title",
            "Site.Palette.Background", "Site.Palette.Key", "Site.Palette.Muted", "Site.Palette.OnPrimary", "Site.Palette.Primary",
            "Site.Palette.Surface", "Site.Palette.Text",
            "Site.PhotoUrl",
            "Site.Sections.Items.Subtitle", "Site.Sections.Items.Text", "Site.Sections.Items.Title",
            "Site.Sections.ShowPhoto", "Site.Sections.Text", "Site.Sections.Title", "Site.Sections.Type", "Site.Sections.Visible",
            "Site.Slug",
            "Site.TeacherName",
            "Site.Teaching.Languages", "Site.Teaching.Levels", "Site.Teaching.Mode", "Site.Teaching.Subjects",
            "Site.Template",
            "Site.Tests.Slug", "Site.Tests.Title",
        ]);
    }

    [Fact]
    public void Vite_assets_resolve_entries_with_their_imported_styles_once_per_response()
    {
        var root = Directory.CreateTempSubdirectory("classhub-vite-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "dist", ".vite"));
            File.WriteAllText(Path.Combine(root, "dist", ".vite", "manifest.json"), """
                {
                  "src/public/public.css": { "file": "assets/public-abc.css", "isEntry": true },
                  "src/islands/copy-link.tsx": { "file": "assets/island-copy-link-1.js", "isEntry": true, "imports": ["_shared.js"] },
                  "_shared.js": { "file": "assets/shared-2.js", "css": ["assets/shared-3.css"] }
                }
                """);
            var manifest = new ViteManifest(new FakeEnvironment(root), NullLogger<ViteManifest>.Instance);
            var http = new DefaultHttpContext();

            ViteTags.For(manifest, http, "src/public/public.css").ShouldBe("<link rel=\"stylesheet\" href=\"/dist/assets/public-abc.css\" />");
            ViteTags.For(manifest, http, "src/islands/copy-link.tsx")
                .ShouldBe("<link rel=\"stylesheet\" href=\"/dist/assets/shared-3.css\" /><script type=\"module\" src=\"/dist/assets/island-copy-link-1.js\"></script>");
            ViteTags.For(manifest, http, "src/islands/copy-link.tsx").ShouldBeEmpty(); // a second island of the same kind
            ViteTags.For(manifest, http, "src/islands/unknown.tsx").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Without_a_build_pages_render_without_assets()
    {
        var root = Directory.CreateTempSubdirectory("classhub-vite-").FullName;
        try
        {
            var manifest = new ViteManifest(new FakeEnvironment(root), NullLogger<ViteManifest>.Instance);
            ViteTags.For(manifest, new DefaultHttpContext(), "src/public/public.css").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClassHub.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (ClassHub.slnx) not found.");
    }

    private sealed class FakeEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(webRoot);
        public string ApplicationName { get; set; } = "ClassHub.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
