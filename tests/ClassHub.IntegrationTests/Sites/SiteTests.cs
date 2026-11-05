using System.Net;
using System.Net.Http.Json;
using ClassHub.Api.Platform;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Sites;

/// <summary>R1-P3: site model, draft editing, slugs and the signed preview (M02).</summary>
[Collection(ApiCollection.Name)]
public sealed class SiteTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_teacher_gets_suggestions_and_nothing_is_stored_until_the_first_save()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile", new { firstName = "Ана", lastName = "Петровска" });

        var site = await teacher.Client.GetJsonAsync<SiteResponse>(SitePath(teacher));

        site.Exists.ShouldBeFalse();
        site.Slug.ShouldBe("ana-petrovska");
        (site.Template, site.Palette, site.Language).ShouldBe(("classic", "ocean", "mk"));
        site.Draft.Sections!.Select(s => s.Type).ShouldBe(SiteSectionTypes.All);
        site.Version.ShouldBeNull();
    }

    [Fact]
    public async Task The_teacher_saves_the_draft_and_later_edits_need_the_current_version()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        var created = await SaveAsync(teacher, Body(slug));
        created.Exists.ShouldBeTrue();
        created.Version.ShouldBe(1);
        created.IsPublished.ShouldBeFalse();

        var edited = await SaveAsync(teacher, Body(slug, template: "modern", palette: "forest", language: "en", version: 1));
        (edited.Template, edited.Palette, edited.Language, edited.Version).ShouldBe(("modern", "forest", "en", 2));

        using var stale = await teacher.Client.PutAsync(SitePath(teacher), Body(slug, template: "minimal", version: 1));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Saving_the_site_completes_the_onboarding_step()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var step = async () => (await teacher.Client.GetJsonAsync<ClassHub.Core.Modules.Teachers.Contracts.OnboardingResponse>($"/api/w/{teacher.WorkspaceId}/onboarding"))
            .Steps.Single(s => s.Key == ClassHub.Core.Modules.Teachers.Contracts.OnboardingSteps.PublicPage);

        (await step()).Completed.ShouldBeFalse();
        await SaveAsync(teacher, Body(UniqueSlug()));
        (await step()).Completed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_slug_belongs_to_one_site_and_availability_reflects_it()
    {
        using var first = await fixture.CreateTeacherAsync();
        using var second = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        await SaveAsync(first, Body(slug));

        using (var taken = await second.Client.PutAsync(SitePath(second), Body(slug)))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await taken.ErrorCodeAsync()).ShouldBe(ErrorCodes.SlugTaken);
        }

        (await Availability(second, slug)).ShouldBe(new SlugAvailabilityResponse(slug, false, "taken"));
        (await Availability(first, slug)).Available.ShouldBeTrue(); // its own slug
        (await Availability(second, "admin")).Reason.ShouldBe("reserved");
        (await Availability(second, "Ana")).Reason.ShouldBe("format");
        (await Availability(second, UniqueSlug())).Available.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_claims_of_the_same_slug_have_exactly_one_winner()
    {
        var teachers = new List<TeacherSession>();
        for (var i = 0; i < 4; i++)
        {
            teachers.Add(await fixture.CreateTeacherAsync());
        }

        try
        {
            var slug = UniqueSlug();
            var responses = await Task.WhenAll(teachers.Select(t => t.Client.PutAsync(SitePath(t), Body(slug))));
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            foreach (var lost in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            {
                lost.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                (await lost.ErrorCodeAsync()).ShouldBe(ErrorCodes.SlugTaken);
            }

            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
        finally
        {
            teachers.ForEach(t => t.Dispose());
        }
    }

    [Theory]
    [InlineData("slug", "admin")]
    [InlineData("slug", "Ana-Math")]
    [InlineData("template", "fancy")]
    [InlineData("palette", "neon")]
    [InlineData("language", "de")]
    public async Task Invalid_site_settings_are_rejected(string field, string value)
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(Body(UniqueSlug()), System.Text.Json.JsonSerializerOptions.Web)!.AsObject();
        body[field] = value;

        using var response = await teacher.Client.PutAsync(SitePath(teacher), body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Section_content_is_validated()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var sections = SiteService.DefaultContent("mk").Sections!;

        foreach (var invalid in new[]
                 {
                     sections.Skip(1).ToArray(),
                     sections.Append(sections[0]).ToArray(),
                     sections.Select(s => s.Type == SiteSectionTypes.About ? s with { Text = new string('x', 3001) } : s).ToArray(),
                     sections.Select(s => s.Type == SiteSectionTypes.Hero ? s with { Items = [new SiteSectionItem("x", null, null)] } : s).ToArray(),
                     sections.Select(s => s.Type == SiteSectionTypes.Faq ? s with { Items = Enumerable.Repeat(new SiteSectionItem("q", null, "a"), 31).ToArray() } : s).ToArray(),
                 })
        {
            using var response = await teacher.Client.PutAsync(SitePath(teacher), Body(UniqueSlug(), sections: invalid));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Theory]
    [InlineData("{\"slug\":\"t-nullsections1\",\"template\":\"classic\",\"palette\":\"ocean\",\"language\":\"mk\",\"draft\":{\"sections\":null}}")]
    [InlineData("{\"slug\":\"t-nullsections2\",\"template\":\"classic\",\"palette\":\"ocean\",\"language\":\"mk\",\"draft\":{}}")]
    [InlineData("{\"slug\":\"t-nullsections3\",\"template\":\"classic\",\"palette\":\"ocean\",\"language\":\"mk\"}")]
    [InlineData("{\"slug\":\"t-nullsections4\",\"template\":\"classic\",\"palette\":\"ocean\",\"language\":\"mk\",\"draft\":{\"sections\":[null]}}")]
    [InlineData("{\"slug\":\"t-nullsections5\",\"template\":\"classic\",\"palette\":\"ocean\",\"language\":\"mk\",\"draft\":{\"sections\":[{\"type\":\"faq\",\"visible\":true,\"items\":[null]}]}}")]
    public async Task Missing_or_null_sections_are_a_validation_error_not_a_crash(string json)
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await teacher.Client.SendContentAsync(HttpMethod.Put, SitePath(teacher), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_preview_renders_the_draft_as_encoded_text_and_is_never_indexed()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var sections = SiteService.DefaultContent("en").Sections!.Select(s => s.Type switch
        {
            SiteSectionTypes.About => s with { Text = "I teach maths <script>alert('x')</script> & physics." },
            SiteSectionTypes.Faq => s with { Visible = true, Items = [new SiteSectionItem("Do you teach online?", null, "Yes, <b>always</b>.")] },
            SiteSectionTypes.Testimonials => s with { Visible = false, Items = [new SiteSectionItem("Hidden author", null, "Hidden quote")] },
            _ => s,
        }).ToArray();
        await SaveAsync(teacher, Body(UniqueSlug(), language: "en", palette: "plum", sections: sections));

        using var issued = await teacher.Client.PostAsync($"{SitePath(teacher)}/preview");
        issued.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await issued.Content.ReadFromJsonAsync<SitePreviewResponse>(Ct))!;
        preview.Url.ShouldStartWith("/preview/");
        preview.ExpiresAtUtc.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(14));

        // Opened without a session (the iframe only has the token).
        using var anonymous = await ApiClient.CreateAsync(fixture);
        using var page = await anonymous.GetAsync(preview.Url);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        page.Headers.GetValues("X-Robots-Tag").Single().ShouldContain("noindex");
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();
        // The URL carries the token: no Referer may ever include it (e.g. on the profile photo request).
        page.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        var html = await page.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow, noarchive\" />");
        html.ShouldContain("<html lang=\"en\">");
        html.ShouldContain("&lt;script&gt;alert(&#x27;x&#x27;)&lt;/script&gt; &amp; physics.");
        html.ShouldNotContain("<script>alert");
        html.ShouldContain("Yes, &lt;b&gt;always&lt;/b&gt;.");
        html.ShouldContain("class=\"palette-plum "); // the plum palette (a class of the public stylesheet, no inline styles)
        html.ShouldNotContain("Hidden quote"); // hidden sections are not rendered
    }

    [Fact]
    public async Task The_preview_shows_the_teacher_photo_without_a_session_through_the_token()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using (var upload = await teacher.Client.SendContentAsync(HttpMethod.Put, $"/api/w/{teacher.WorkspaceId}/profile/photo",
                   new MultipartFormDataContent { { new ByteArrayContent(ClassHub.UnitTests.Files.TestImages.Png(300, 300)), "file", "me.png" } }))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await SaveAsync(teacher, Body(UniqueSlug()));
        using var issued = await teacher.Client.PostAsync($"{SitePath(teacher)}/preview");
        var url = (await issued.Content.ReadFromJsonAsync<SitePreviewResponse>(Ct))!.Url;
        using var anonymous = await ApiClient.CreateAsync(fixture);
        var html = await (await anonymous.GetAsync(url)).Content.ReadAsStringAsync(Ct);
        var photoUrl = System.Text.RegularExpressions.Regex.Match(html, "<img src=\"([^\"]+)\"").Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
        photoUrl.ShouldStartWith($"{url}/photo?v=");

        using var photo = await anonymous.GetAsync(photoUrl);
        photo.StatusCode.ShouldBe(HttpStatusCode.OK);
        photo.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        photo.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);

        // Without a valid token there is no photo; the private endpoint still needs a member session.
        using (var forged = await anonymous.GetAsync("/preview/forged/photo"))
        {
            forged.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var privateWithoutSession = await anonymous.GetAsync($"/api/w/{teacher.WorkspaceId}/profile/photo");
        privateWithoutSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_slug_with_a_trailing_newline_is_a_validation_error()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var response = await teacher.Client.PutAsync(SitePath(teacher), Body("admin\n"));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("slug", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_tampered_and_expired_preview_tokens_get_404()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        await SaveAsync(teacher, Body(UniqueSlug()));
        using var issued = await teacher.Client.PostAsync($"{SitePath(teacher)}/preview");
        var url = (await issued.Content.ReadFromJsonAsync<SitePreviewResponse>(Ct))!.Url;
        using var anonymous = await ApiClient.CreateAsync(fixture);

        string expiredUrl;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<SitePreviewTokens>();
            var valid = tokens.Validate(url["/preview/".Length..]).ShouldNotBeNull();
            expiredUrl = "/preview/" + tokens.Issue(valid.SiteId, valid.WorkspaceId, valid.UserId, TimeSpan.FromMinutes(-1)).Token;
        }

        foreach (var bad in new[] { "/preview/not-a-token", url[..^4] + "AAAA", expiredUrl })
        {
            using var page = await anonymous.GetAsync(bad);
            page.StatusCode.ShouldBe(HttpStatusCode.NotFound, bad);
        }
    }

    [Fact]
    public async Task A_preview_needs_a_saved_site()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var response = await teacher.Client.PostAsync($"{SitePath(teacher)}/preview");
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.SiteNotCreated);
    }

    [Fact]
    public async Task Another_teacher_cannot_read_change_check_or_preview_the_site()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var slug = UniqueSlug();
        await SaveAsync(owner, Body(slug));
        var path = SitePath(owner);

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, path, Body(slug, template: "modern", version: 1));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, $"{path}/slug-availability?slug={slug}");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/preview");

        (await owner.Client.GetJsonAsync<SiteResponse>(path)).Template.ShouldBe("classic");
    }

    [Fact]
    public void Every_spa_prefix_is_a_reserved_slug() =>
        SpaHosting.Prefixes.Append(SpaHosting.DevMailboxPrefix).Select(p => p.TrimStart('/').Split('/')[0]).ShouldAllBe(s => SiteSlugs.Reserved.Contains(s));

    private static string SitePath(TeacherSession teacher) => $"/api/w/{teacher.WorkspaceId}/site";

    private static string UniqueSlug() => $"t-{Guid.NewGuid().ToString("N")[..12]}";

    private static object Body(string slug, string template = "classic", string palette = "ocean", string language = "mk", int? version = null, IReadOnlyList<SiteSection>? sections = null) =>
        new { slug, template, palette, language, draft = new { sections = sections ?? SiteService.DefaultContent(language).Sections }, version };

    private static async Task<SiteResponse> SaveAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PutAsync(SitePath(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
    }

    private static Task<SlugAvailabilityResponse> Availability(TeacherSession teacher, string slug) =>
        teacher.Client.GetJsonAsync<SlugAvailabilityResponse>($"{SitePath(teacher)}/slug-availability?slug={Uri.EscapeDataString(slug)}");
}
