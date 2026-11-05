using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassHub.Api.Platform;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Offers.Domain;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Sites;

/// <summary>R1-P4: publishing, the public Razor site at /{slug}, SEO, sitemap/robots, output cache and page views.</summary>
[Collection(ApiCollection.Name)]
public sealed partial class PublicSiteTests(ClassHubApiFixture fixture)
{
    private const string BrowserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unknown_and_unpublished_sites_answer_404_with_noindex()
    {
        using var visitor = Visitor();
        using (var unknown = await visitor.GetAsync($"/{UniqueSlug()}", Ct))
        {
            await AssertNotFoundPageAsync(unknown);
        }

        using var teacher = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        await SaveAsync(teacher, Body(slug));
        using var unpublished = await visitor.GetAsync($"/{slug}", Ct);
        await AssertNotFoundPageAsync(unpublished);
    }

    [Fact]
    public async Task A_published_site_renders_complete_html_with_meta_tags_and_json_ld()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        await ProfileAsync(teacher, "Ана", "Петровска", [math]);
        await CreateOfferAsync(teacher, math, "Јавен час", price: 120_000);
        await CreateOfferAsync(teacher, math, "Скриен час", showOnPublicSite: false);
        await CreateOfferAsync(teacher, math, "Неактивен час", isActive: false);
        var archived = await CreateOfferAsync(teacher, math, "Архивиран час");
        (await teacher.Client.DeleteAsync($"/api/w/{teacher.WorkspaceId}/offers/{archived.Id}")).Dispose();

        var slug = UniqueSlug();
        var sections = Sections("mk", hero: ("Часови по математика", "Помагам на учениците да ја разберат математиката."),
            faq: [new SiteSectionItem("Дали предавате онлајн?", null, "Да, преку видео повик.")],
            hiddenTestimonial: "Тајна изјава");
        var saved = await SaveAsync(teacher, Body(slug, sections: sections));
        await PublishAsync(teacher, saved.Version);

        using var visitor = Visitor();
        using var response = await visitor.GetAsync($"/{slug}", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        html.ShouldContain("<html lang=\"mk\">");
        html.ShouldContain("<title>Ана Петровска — Часови по математика</title>");
        html.ShouldContain("<meta name=\"description\" content=\"Помагам на учениците да ја разберат математиката.\" />");
        html.ShouldContain($"<link rel=\"canonical\" href=\"http://classhub.test/{slug}\" />");
        html.ShouldContain($"<meta property=\"og:url\" content=\"http://classhub.test/{slug}\" />");
        html.ShouldContain("<meta property=\"og:locale\" content=\"mk_MK\" />");
        html.ShouldContain("<meta name=\"twitter:card\" content=\"summary\" />");
        html.ShouldNotContain("name=\"robots\"");

        // Only public, active, non-archived offers and only visible sections reach the page.
        html.ShouldContain("Јавен час");
        html.ShouldContain("1.200,00 MKD");
        html.ShouldNotContain("Скриен час");
        html.ShouldNotContain("Неактивен час");
        html.ShouldNotContain("Архивиран час");
        html.ShouldNotContain("Тајна изјава");
        html.ShouldNotContain("data-section=\"testimonials\"");
        html.ShouldNotContain("data-section=\"about\""); // visible but empty: no empty box for visitors
        html.ShouldContain("data-section=\"subjects\""); // filled from the profile

        using var jsonLd = JsonDocument.Parse(JsonLd(html));
        var graph = jsonLd.RootElement.GetProperty("@graph");
        var person = graph[0];
        person.GetProperty("@type").GetString().ShouldBe("Person");
        person.GetProperty("name").GetString().ShouldBe("Ана Петровска");
        person.GetProperty("url").GetString().ShouldBe($"http://classhub.test/{slug}");
        person.GetProperty("knowsAbout").EnumerateArray().Select(s => s.GetString()).ShouldBe(["Математика"]);
        var offer = person.GetProperty("makesOffer").EnumerateArray().ShouldHaveSingleItem();
        (offer.GetProperty("name").GetString(), offer.GetProperty("price").GetString(), offer.GetProperty("priceCurrency").GetString())
            .ShouldBe(("Јавен час", "1200.00", "MKD"));
        var faq = graph[1];
        faq.GetProperty("@type").GetString().ShouldBe("FAQPage");
        faq.GetProperty("mainEntity")[0].GetProperty("name").GetString().ShouldBe("Дали предавате онлајн?");

        // Strict public CSP, never framed, nothing user-specific (no cookies), revalidated by browsers.
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([SecurityHeadersMiddleware.PublicPageContentSecurityPolicy]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Fact]
    public async Task Teacher_text_is_encoded_and_cannot_break_out_of_the_json_ld_block()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        const string attack = "</script><script>alert(1)</script><b onmouseover=alert(2)>";
        var slug = UniqueSlug();
        var saved = await SaveAsync(teacher, Body(slug, sections: Sections("mk", hero: (attack, attack))));
        await PublishAsync(teacher, saved.Version);

        using var visitor = Visitor();
        var html = await visitor.GetStringAsync($"/{slug}", Ct);

        html.ShouldNotContain("<script>alert(1)</script>");
        html.ShouldNotContain("<b onmouseover");
        html.ShouldContain("&lt;/script&gt;&lt;script&gt;alert(1)&lt;/script&gt;");
        // One inline script element: the JSON-LD data block (island scripts are same-origin files with src).
        InlineScriptOpenings().Matches(html).Count.ShouldBe(1);
        using var jsonLd = JsonDocument.Parse(JsonLd(html));
        jsonLd.RootElement.GetProperty("@graph")[0].GetProperty("description").GetString().ShouldStartWith("</script>");
    }

    [Fact]
    public async Task Draft_changes_stay_private_until_published_again_and_unpublishing_takes_the_page_offline()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        var saved = await SaveAsync(teacher, Body(slug, sections: Sections("mk", hero: ("Прва верзија", null))));
        var published = await PublishAsync(teacher, saved.Version);
        published.IsPublished.ShouldBeTrue();
        published.PublishedAtUtc.ShouldNotBeNull();
        published.HasUnpublishedChanges.ShouldBeFalse();

        var edited = await SaveAsync(teacher, Body(slug, template: "modern", version: published.Version, sections: Sections("mk", hero: ("Втора верзија", null))));
        edited.HasUnpublishedChanges.ShouldBeTrue();
        using var visitor = Visitor();
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldContain("Прва верзија");

        var republished = await PublishAsync(teacher, edited.Version);
        republished.HasUnpublishedChanges.ShouldBeFalse();
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldContain("Втора верзија");

        var unpublished = await UnpublishAsync(teacher, republished.Version);
        (unpublished.IsPublished, unpublished.PublishedAtUtc).ShouldBe((false, null));
        unpublished.Draft.Sections!.Single(s => s.Type == SiteSectionTypes.Hero).Title.ShouldBe("Втора верзија"); // draft kept
        using (var gone = await visitor.GetAsync($"/{slug}", Ct))
        {
            await AssertNotFoundPageAsync(gone);
        }

        await using var db = fixture.CreateDbContext();
        var actions = await db.Set<AuditLogEntry>().Where(a => a.WorkspaceId == teacher.WorkspaceId && a.Action.StartsWith("site."))
            .OrderBy(a => a.OccurredAtUtc).Select(a => a.Action).ToListAsync(Ct);
        actions.ShouldBe(["site.published", "site.published", "site.unpublished"]);
    }

    [Fact]
    public async Task Cached_pages_are_served_until_a_change_that_shows_on_them_evicts_them()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        await ProfileAsync(teacher, "Ана", "Петровска", [math]);
        var slug = UniqueSlug();
        await PublishAsync(teacher, (await SaveAsync(teacher, Body(slug))).Version);
        using var visitor = Visitor();
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldContain("Ана Петровска");

        // Changed behind the application's back: the cached page (any query string) still shows the old name.
        await RenameInDatabaseAsync(teacher, "Скришно");
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldContain("Ана Петровска");
        (await visitor.GetStringAsync($"/{slug}?utm_source=test", Ct)).ShouldContain("Ана Петровска");

        // Signed-in requests never use the cache.
        (await (await teacher.Client.GetAsync($"/{slug}")).Content.ReadAsStringAsync(Ct)).ShouldContain("Скришно Петровска");

        // A profile change through the API evicts the page; so does an offer change.
        await ProfileAsync(teacher, "Марија", "Петровска", [math]);
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldContain("Марија Петровска");
        await CreateOfferAsync(teacher, math, "Нова понуда");
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldContain("Нова понуда");
    }

    [Fact]
    public async Task A_suspended_owner_site_is_hidden_and_returns_after_reactivation()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        await PublishAsync(teacher, (await SaveAsync(teacher, Body(slug))).Version);
        using var visitor = Visitor();
        (await visitor.GetAsync($"/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // now cached

        await fixture.WithServiceAsync<IUserAccountStatus, bool>(async s => { await s.SuspendAsync(teacher.Me.Id, Ct); return true; });
        using (var hidden = await visitor.GetAsync($"/{slug}", Ct))
        {
            await AssertNotFoundPageAsync(hidden);
        }

        (await visitor.GetStringAsync("/sitemap.xml", Ct)).ShouldNotContain($"/{slug}<");
        (await visitor.GetAsync($"/{slug}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await fixture.WithServiceAsync<IUserAccountStatus, bool>(async s => { await s.ReactivateAsync(teacher.Me.Id, Ct); return true; });
        (await visitor.GetAsync($"/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_sitemap_lists_published_sites_and_robots_keeps_crawlers_out_of_the_app()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var published = UniqueSlug();
        await PublishAsync(teacher, (await SaveAsync(teacher, Body(published))).Version);
        using var other = await fixture.CreateTeacherAsync();
        var draftOnly = UniqueSlug();
        await SaveAsync(other, Body(draftOnly));

        using var visitor = Visitor();
        using var sitemapResponse = await visitor.GetAsync("/sitemap.xml", Ct);
        sitemapResponse.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        var sitemap = XDocument.Parse(await sitemapResponse.Content.ReadAsStringAsync(Ct));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = sitemap.Descendants(ns + "loc").Select(l => l.Value).ToList();
        locations.ShouldContain("http://classhub.test/");
        locations.ShouldContain("http://classhub.test/en");
        locations.ShouldContain($"http://classhub.test/{published}");
        locations.ShouldNotContain($"http://classhub.test/{draftOnly}");

        var robots = await visitor.GetStringAsync("/robots.txt", Ct);
        robots.ShouldContain("Disallow: /app/\nDisallow: /app$\n");
        robots.ShouldContain("Disallow: /api/\n");
        robots.ShouldNotContain("Disallow: /app\n"); // a plain prefix would also block sites such as /applied-math
        robots.ShouldContain("Sitemap: http://classhub.test/sitemap.xml");
    }

    [Fact]
    public async Task Only_visitor_page_views_are_counted_also_when_served_from_the_cache()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        await PublishAsync(teacher, (await SaveAsync(teacher, Body(slug))).Version);

        using var visitor = Visitor();
        (await visitor.GetAsync($"/{slug}", Ct)).Dispose();
        (await visitor.GetAsync($"/{slug}", Ct)).Dispose(); // from the output cache
        using var bot = Visitor("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");
        (await bot.GetAsync($"/{slug}", Ct)).Dispose();
        using var noAgent = fixture.Factory.CreateClient();
        (await noAgent.GetAsync($"/{slug}", Ct)).Dispose();
        using var prefetch = new HttpRequestMessage(HttpMethod.Get, $"/{slug}");
        prefetch.Headers.Add("Sec-Purpose", "prefetch");
        (await visitor.SendAsync(prefetch, Ct)).Dispose();
        (await teacher.Client.GetAsync($"/{slug}")).Dispose(); // signed in
        (await visitor.GetAsync($"/{UniqueSlug()}", Ct)).Dispose(); // 404

        var site = await teacher.Client.GetJsonAsync<SiteResponse>(SitePath(teacher));
        site.ViewsLast30Days.ShouldBe(2);

        // D-32: one counter row per site and day, nothing about the visitor.
        await using var db = fixture.CreateDbContext();
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SitePageViews'").ToListAsync(Ct);
        columns.Order(StringComparer.Ordinal).ShouldBe(["Day", "SiteId", "Views", "WorkspaceId"]);
    }

    [Fact]
    public async Task Concurrent_first_views_of_the_day_are_all_counted()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        await PublishAsync(teacher, (await SaveAsync(teacher, Body(slug))).Version);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.WithServiceAsync<IPublicSites, bool>(async sites =>
        {
            await sites.RecordViewAsync(slug, Ct);
            return true;
        })));

        (await teacher.Client.GetJsonAsync<SiteResponse>(SitePath(teacher))).ViewsLast30Days.ShouldBe(8);
    }

    [Fact]
    public async Task The_public_photo_is_served_only_while_published_and_shown_in_the_hero()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using (var upload = await teacher.Client.SendContentAsync(HttpMethod.Put, $"/api/w/{teacher.WorkspaceId}/profile/photo",
                   new MultipartFormDataContent { { new ByteArrayContent(ClassHub.UnitTests.Files.TestImages.Png(300, 300)), "file", "me.png" } }))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var slug = UniqueSlug();
        using var visitor = Visitor();
        var saved = await SaveAsync(teacher, Body(slug));
        (await visitor.GetAsync($"/{slug}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // not published

        var published = await PublishAsync(teacher, saved.Version);
        var html = await visitor.GetStringAsync($"/{slug}", Ct);
        var photoUrl = ImageSource().Match(html).Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
        photoUrl.ShouldStartWith($"/{slug}/photo?v=");
        html.ShouldContain($"<meta property=\"og:image\" content=\"http://classhub.test{photoUrl.Replace("&", "&amp;", StringComparison.Ordinal)}\" />");
        using (var photo = await visitor.GetAsync(photoUrl, Ct))
        {
            photo.StatusCode.ShouldBe(HttpStatusCode.OK);
            photo.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        }

        var noPhoto = Sections("mk", hero: ("Без фотографија", null), showPhoto: false);
        var hidden = await SaveAsync(teacher, Body(slug, version: published.Version, sections: noPhoto));
        await PublishAsync(teacher, hidden.Version);
        (await visitor.GetAsync($"/{slug}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetStringAsync($"/{slug}", Ct)).ShouldNotContain("og:image");
    }

    [Fact]
    public async Task Publishing_needs_a_saved_site_the_current_version_and_the_own_workspace()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using (var early = await teacher.Client.PostAsync($"{SitePath(teacher)}/publish", new { version = (int?)null }))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await early.ErrorCodeAsync()).ShouldBe(ErrorCodes.SiteNotCreated);
        }

        var saved = await SaveAsync(teacher, Body(UniqueSlug()));
        using (var stale = await teacher.Client.PostAsync($"{SitePath(teacher)}/publish", new { version = saved.Version - 1 }))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        }

        using var intruder = await fixture.CreateTeacherAsync();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{SitePath(teacher)}/publish", new { version = saved.Version });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{SitePath(teacher)}/unpublish", new { version = saved.Version });
        (await teacher.Client.GetJsonAsync<SiteResponse>(SitePath(teacher))).IsPublished.ShouldBeFalse();
    }

    [Fact]
    public async Task Publishing_completes_the_onboarding_step()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var step = async () => (await teacher.Client.GetJsonAsync<OnboardingResponse>($"/api/w/{teacher.WorkspaceId}/onboarding"))
            .Steps.Single(s => s.Key == OnboardingSteps.Publish);

        (await step()).ShouldBe(new OnboardingStep(OnboardingSteps.Publish, false, true));
        await PublishAsync(teacher, (await SaveAsync(teacher, Body(UniqueSlug()))).Version);
        (await step()).Completed.ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_the_address_of_a_published_site_moves_it_at_once()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var oldSlug = UniqueSlug();
        var published = await PublishAsync(teacher, (await SaveAsync(teacher, Body(oldSlug))).Version);
        using var visitor = Visitor();
        (await visitor.GetAsync($"/{oldSlug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // cached

        var newSlug = UniqueSlug();
        await SaveAsync(teacher, Body(newSlug, version: published.Version));

        (await visitor.GetAsync($"/{oldSlug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/{newSlug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var sitemap = await visitor.GetStringAsync("/sitemap.xml", Ct);
        sitemap.ShouldContain($"/{newSlug}<");
        sitemap.ShouldNotContain($"/{oldSlug}<");
    }

    [Fact]
    public async Task The_landing_page_is_server_rendered_in_both_languages()
    {
        using var visitor = Visitor();
        using var mk = await visitor.GetAsync("/", Ct);
        var html = await mk.Content.ReadAsStringAsync(Ct);
        mk.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<html lang=\"mk\">");
        html.ShouldContain("<link rel=\"canonical\" href=\"http://classhub.test/\" />");
        html.ShouldContain("<link rel=\"alternate\" hreflang=\"en\" href=\"http://classhub.test/en\" />");
        html.ShouldContain("href=\"/register\"");
        mk.Headers.GetValues("Content-Security-Policy").ShouldBe([SecurityHeadersMiddleware.PublicPageContentSecurityPolicy]);

        var en = await visitor.GetStringAsync("/en", Ct);
        en.ShouldContain("<html lang=\"en\">");
        en.ShouldContain("<link rel=\"canonical\" href=\"http://classhub.test/en\" />");
    }

    [Fact]
    public async Task Query_strings_cannot_change_the_cached_landing_page()
    {
        await fixture.WithServiceAsync<Microsoft.AspNetCore.OutputCaching.IOutputCacheStore, bool>(async store =>
        {
            await store.EvictByTagAsync(PublicSiteCaching.LandingTag, Ct); // cold cache
            return true;
        });
        using var visitor = Visitor();

        (await visitor.GetStringAsync("/?lang=en", Ct)).ShouldContain("<html lang=\"mk\">");
        (await visitor.GetStringAsync("/", Ct)).ShouldContain("<html lang=\"mk\">");
        using var unknown = await visitor.GetAsync($"/{UniqueSlug()}?slug=other", Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_public_read_only_request_cannot_write_tenant_data()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetPublicReadOnly(teacher.WorkspaceId);
        var db = scope.ServiceProvider.GetRequiredService<ClassHub.Core.Infrastructure.Persistence.ClassHubDbContext>();
        db.Set<Offer>().Add(new Offer { Id = Guid.NewGuid(), Title = "x", Currency = "MKD", Kind = OfferKind.SingleLesson, Mode = OfferMode.Online, DurationMinutes = 60 });

        await Should.ThrowAsync<TenantIsolationException>(() => db.SaveChangesAsync(Ct));
        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<TenantContext>().Set(teacher.WorkspaceId, teacher.Me.Id, ClassHub.Core.SharedKernel.Tenancy.WorkspaceRole.Owner));
    }

    private static async Task AssertNotFoundPageAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.GetValues("X-Robots-Tag").ShouldBe(["noindex"]);
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("<meta name=\"robots\" content=\"noindex\" />");
        html.ShouldContain("Страницата не е пронајдена");
    }

    private HttpClient Visitor(string userAgent = BrowserAgent)
    {
        var client = fixture.Factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    private async Task RenameInDatabaseAsync(TeacherSession teacher, string firstName)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"UPDATE TeacherProfiles SET FirstName = {firstName} WHERE WorkspaceId = {teacher.WorkspaceId}", Ct);
    }

    private static string JsonLd(string html) => JsonLdBlock().Match(html).Groups[1].Value;

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdBlock();

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptOpenings();

    [GeneratedRegex("<img src=\"([^\"]+)\"")]
    private static partial Regex ImageSource();

    private static string SitePath(TeacherSession teacher) => $"/api/w/{teacher.WorkspaceId}/site";

    private static string UniqueSlug() => $"p-{Guid.NewGuid().ToString("N")[..12]}";

    private static object Body(string slug, string template = "classic", int? version = null, IReadOnlyList<SiteSection>? sections = null) =>
        new { slug, template, palette = "ocean", language = "mk", draft = new { sections = sections ?? SiteService.DefaultContent("mk").Sections }, version };

    /// <summary>The default sections with a given hero, optional visible FAQ items and a hidden testimonial.</summary>
    private static IReadOnlyList<SiteSection> Sections(string language, (string? Title, string? Text) hero, IReadOnlyList<SiteSectionItem>? faq = null,
        string? hiddenTestimonial = null, bool showPhoto = true) =>
        [
            .. SiteService.DefaultContent(language).Sections!.Select(s => s.Type switch
            {
                SiteSectionTypes.Hero => s with { Title = hero.Title, Text = hero.Text, ShowPhoto = showPhoto },
                SiteSectionTypes.Faq when faq is not null => s with { Visible = true, Items = faq },
                SiteSectionTypes.Testimonials when hiddenTestimonial is not null => s with { Visible = false, Items = [new SiteSectionItem("Ученик", null, hiddenTestimonial)] },
                _ => s,
            }),
        ];

    private static async Task<SiteResponse> SaveAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PutAsync(SitePath(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
    }

    private static Task<SiteResponse> PublishAsync(TeacherSession teacher, int? version) => ChangeAsync(teacher, "publish", version);

    private static Task<SiteResponse> UnpublishAsync(TeacherSession teacher, int? version) => ChangeAsync(teacher, "unpublish", version);

    private static async Task<SiteResponse> ChangeAsync(TeacherSession teacher, string action, int? version)
    {
        using var response = await teacher.Client.PostAsync($"{SitePath(teacher)}/{action}", new { version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
    }

    private static async Task ProfileAsync(TeacherSession teacher, string firstName, string lastName, IReadOnlyList<Guid> subjects)
    {
        var current = await teacher.Client.GetJsonAsync<TeacherProfileResponse>($"/api/w/{teacher.WorkspaceId}/profile");
        using var response = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile",
            new { firstName, lastName, subjectIds = subjects, languages = new[] { "mk" }, teachingMode = "Online", version = current.Version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<OfferResponse> CreateOfferAsync(TeacherSession teacher, Guid subject, string title, long price = 150_000, bool showOnPublicSite = true, bool isActive = true)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/offers", new
        {
            kind = "SingleLesson",
            title,
            description = "Индивидуален час",
            subjectId = subject,
            durationMinutes = 60,
            priceMinor = price,
            mode = "Online",
            isActive,
            showOnPublicSite,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(Ct))!;
    }

    private static async Task<Guid> SubjectAsync(TeacherSession teacher, string code) =>
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == code).Id;
}
