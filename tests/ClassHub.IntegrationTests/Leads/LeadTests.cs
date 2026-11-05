using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Leads.Application;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Leads;

/// <summary>R1-P5: public lead intake (spam rules, consent, D-10b) and the teacher's inbox (M03).</summary>
[Collection(ApiCollection.Name)]
public sealed class LeadTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record PublishedTeacher(TeacherSession Teacher, string Slug, Guid Math, OfferResponse PublicOffer) : IDisposable
    {
        public void Dispose() => Teacher.Dispose();
    }

    [Fact]
    public async Task The_form_offers_only_public_offers_and_the_teachers_subjects_with_the_consent_text()
    {
        using var site = await PublishedTeacherAsync();
        await CreateOfferAsync(site.Teacher, site.Math, "Скриена", showOnPublicSite: false);
        await CreateOfferAsync(site.Teacher, site.Math, "Неактивна", isActive: false);

        using var visitor = fixture.Factory.CreateClient();
        using var response = await visitor.GetAsync($"/api/public/sites/{site.Slug}/lead-form", Ct);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var form = (await response.Content.ReadFromJsonAsync<LeadFormResponse>(Ct))!;

        form.Available.ShouldBeTrue();
        form.FormToken.ShouldNotBeNullOrEmpty();
        form.Offers.Select(o => o.Name).ShouldBe(["Јавен час"]);
        form.Subjects.Select(s => s.Name).ShouldBe(["Математика"]);
        form.ConsentVersion.ShouldBe(LeadConsent.Version);
        form.ConsentText.ShouldBe(LeadConsent.Text("mk"));
        form.MinorNotice.ShouldContain("16");
    }

    [Fact]
    public async Task A_visitor_request_reaches_the_inbox_and_the_teacher_gets_a_minimal_email()
    {
        using var site = await PublishedTeacherAsync();
        var since = DateTime.UtcNow.AddSeconds(-1);

        using var visitor = fixture.Factory.CreateClient();
        using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads",
            Request(await TokenAsync(site.Slug), type: "LessonRequest", offerId: site.PublicOffer.Id, subjectId: site.Math, message: "Подготовка за матура"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldBe("{\"received\":true}"); // nothing is echoed
        var inbox = await ListAsync(site.Teacher);
        var item = inbox.Items.ShouldHaveSingleItem();
        (item.Type, item.Status, item.Name, item.Email, item.OfferTitle, item.SubjectId).ShouldBe(("LessonRequest", "New", "Марија Илиевска", "marija@example.test", "Јавен час", (Guid?)site.Math));
        inbox.NewCount.ShouldBe(1);

        var lead = await site.Teacher.Client.GetJsonAsync<LeadResponse>($"{LeadsPath(site.Teacher)}/{item.Id}");
        (lead.Phone, lead.Message, lead.ConsentVersion).ShouldBe(("+389 70 123 456", "Подготовка за матура", LeadConsent.Version));

        var mail = await fixture.WaitForEmailAsync(site.Teacher.Email, LeadEmails.NewLeadTemplate, since);
        mail.Subject.ShouldBe("Ново барање на вашата ClassHub страница");
        mail.TextBody.ShouldContain($"http://classhub.test/app/w/{site.Teacher.WorkspaceId}/leads");
        // Minimal data: nothing the visitor wrote is in the email.
        foreach (var secret in new[] { "Марија", "marija@example.test", "123 456", "матура" })
        {
            mail.TextBody.ShouldNotContain(secret);
            mail.HtmlBody.ShouldNotContain(secret);
        }
    }

    [Fact]
    public async Task Spam_is_dropped_silently_honeypot_and_too_fast()
    {
        using var site = await PublishedTeacherAsync();
        using var visitor = fixture.Factory.CreateClient();

        using (var honeypot = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(await TokenAsync(site.Slug), website: "http://spam.example"), Ct))
        {
            honeypot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        // A token fetched just now: sent faster than a person can fill in the form.
        var fresh = (await visitor.GetFromJsonAsync<LeadFormResponse>($"/api/public/sites/{site.Slug}/lead-form", Ct))!.FormToken!;
        using (var tooFast = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(fresh), Ct))
        {
            tooFast.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await ListAsync(site.Teacher)).Total.ShouldBe(0);
        await Task.Delay(300, Ct); // longer than the dispatch interval: a queued email would have arrived
        await using var db = fixture.CreateDbContext();
        (await db.DevMailbox.CountAsync(m => m.Template == LeadEmails.NewLeadTemplate && m.ToAddress == site.Teacher.Email, Ct)).ShouldBe(0);
        (await db.Set<ClassHub.Core.Infrastructure.Email.EmailOutboxMessage>().CountAsync(m => m.Template == LeadEmails.NewLeadTemplate && m.ToAddress == site.Teacher.Email, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_expired_or_foreign_form_tokens_are_refused()
    {
        using var site = await PublishedTeacherAsync();
        using var other = await PublishedTeacherAsync();
        using var visitor = fixture.Factory.CreateClient();

        foreach (var token in new[] { "forged", await TokenAsync(other.Slug), await TokenAsync(site.Slug, DateTimeOffset.UtcNow.AddHours(-25)) })
        {
            using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(token), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.FormExpired);
        }
    }

    [Fact]
    public async Task Requests_are_validated_and_only_public_active_offers_and_own_subjects_are_accepted()
    {
        using var site = await PublishedTeacherAsync();
        var hidden = await CreateOfferAsync(site.Teacher, site.Math, "Скриена", showOnPublicSite: false);
        var inactive = await CreateOfferAsync(site.Teacher, site.Math, "Неактивна", isActive: false);
        using var other = await PublishedTeacherAsync();
        var catalog = await site.Teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog");
        var physics = catalog.Subjects.Single(s => s.Code == "physics").Id;
        using var visitor = fixture.Factory.CreateClient();
        var token = await TokenAsync(site.Slug);

        foreach (var (body, field) in new (object Body, string Field)[]
                 {
                     (Request(token, name: " "), "name"),
                     (Request(token, email: "not-an-email"), "email"),
                     (Request(token, phone: "call me <b>"), "phone"),
                     (Request(token, message: null), "message"), // a message needs text
                     (Request(token, consent: false), "consent"),
                     (Request(token, ageConfirmed: false), "ageConfirmed"),
                     (Request(token, consentVersion: "2020-01-01"), "consentVersion"),
                     (Request(token, type: "Booking"), "type"),
                     (Request(token, name: new string('a', 101)), "name"),
                     (Request(token, type: "LessonRequest", offerId: hidden.Id), "offerId"),
                     (Request(token, type: "LessonRequest", offerId: inactive.Id), "offerId"),
                     (Request(token, type: "LessonRequest", offerId: other.PublicOffer.Id), "offerId"),
                     (Request(token, subjectId: physics), "subjectId"),
                 })
        {
            using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
            (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        // A lesson request may come without a message.
        using var ok = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(token, type: "LessonRequest", message: null), Ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await ListAsync(site.Teacher)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Each_site_accepts_a_limited_number_of_requests_per_hour()
    {
        using var site = await PublishedTeacherAsync();
        using var visitor = fixture.Factory.CreateClient();
        var token = await TokenAsync(site.Slug);
        var limit = new LeadOptions().MaxPerSitePerHour;

        for (var i = 0; i < limit; i++)
        {
            using var accepted = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(token), Ct);
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var limited = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(token), Ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await limited.ErrorCodeAsync()).ShouldBe(ErrorCodes.RateLimited);
        (await ListAsync(site.Teacher)).Total.ShouldBe(limit);
    }

    [Fact]
    public async Task Parallel_submissions_never_exceed_the_per_site_limit()
    {
        using var site = await PublishedTeacherAsync();
        var token = await TokenAsync(site.Slug);
        var limit = new LeadOptions().MaxPerSitePerHour;

        // Twice the quota at once, each from its own client: the lock makes check + insert atomic.
        var statuses = await Task.WhenAll(Enumerable.Range(0, limit * 2).Select(async _ =>
        {
            using var visitor = fixture.Factory.CreateClient();
            using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(token), Ct);
            return response.StatusCode;
        }));

        statuses.Count(s => s == HttpStatusCode.Accepted).ShouldBe(limit);
        statuses.Count(s => s == HttpStatusCode.TooManyRequests).ShouldBe(limit);
        (await ListAsync(site.Teacher)).Total.ShouldBe(limit);
        await using var db = fixture.CreateDbContext();
        // One queued email per accepted lead, none for the refused ones.
        (await db.Set<ClassHub.Core.Infrastructure.Email.EmailOutboxMessage>().CountAsync(m => m.Template == LeadEmails.NewLeadTemplate && m.ToAddress == site.Teacher.Email, Ct)).ShouldBe(limit);
    }

    [Fact]
    public async Task Unconfirmed_teachers_cannot_publish_or_receive_requests()
    {
        using var unconfirmed = await fixture.CreateTeacherAsync();
        var saved = await SaveSiteAsync(unconfirmed, UniqueSlug());
        using (var publish = await unconfirmed.Client.PostAsync($"/api/w/{unconfirmed.WorkspaceId}/site/publish", new { version = saved.Version }))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await publish.ErrorCodeAsync()).ShouldBe(ErrorCodes.EmailNotConfirmed);
        }

        // A published site whose owner is unconfirmed again: the form is hidden and submissions are refused.
        using var site = await PublishedTeacherAsync();
        var token = await TokenAsync(site.Slug);
        await fixture.SetEmailConfirmedAsync(site.Teacher, false);
        using var visitor = fixture.Factory.CreateClient();
        var form = (await visitor.GetFromJsonAsync<LeadFormResponse>($"/api/public/sites/{site.Slug}/lead-form", Ct))!;
        (form.Available, form.FormToken, form.Offers.Count).ShouldBe((false, null, 0));
        using var submit = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(token), Ct);
        submit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await submit.ErrorCodeAsync()).ShouldBe(ErrorCodes.LeadsUnavailable);
    }

    [Fact]
    public async Task Unknown_and_unpublished_sites_have_no_form()
    {
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var slug = UniqueSlug();
        await SaveSiteAsync(teacher, slug);
        using var visitor = fixture.Factory.CreateClient();

        (await visitor.GetAsync($"/api/public/sites/{slug}/lead-form", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/api/public/sites/{UniqueSlug()}/lead-form", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var post = await visitor.PostAsJsonAsync($"/api/public/sites/{slug}/leads", Request(await TokenAsync(slug)), Ct);
        post.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_teacher_moves_a_lead_through_its_statuses_with_notes_and_archive()
    {
        using var site = await PublishedTeacherAsync();
        var id = await SubmitAsync(site);
        var path = $"{LeadsPath(site.Teacher)}/{id}";
        var lead = await site.Teacher.Client.GetJsonAsync<LeadResponse>(path);

        lead = await StatusAsync(site.Teacher, path, "Contacted", lead.Version);
        lead.Status.ShouldBe("Contacted");
        using (var stale = await site.Teacher.Client.PutAsync($"{path}/status", new { status = "Converted", version = lead.Version - 1 }))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        }

        lead = await StatusAsync(site.Teacher, path, "Closed", lead.Version);
        using (var invalid = await site.Teacher.Client.PutAsync($"{path}/status", new { status = "Converted", version = lead.Version }))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await invalid.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        using (var note = await site.Teacher.Client.PostAsync($"{path}/notes", new { text = "  Јавив се, ќе пробаме во среда.  " }))
        {
            note.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var empty = await site.Teacher.Client.PostAsync($"{path}/notes", new { text = "   " }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var archived = await site.Teacher.Client.PostAsync($"{path}/archive", new { version = lead.Version }))
        {
            archived.StatusCode.ShouldBe(HttpStatusCode.OK);
            lead = (await archived.Content.ReadFromJsonAsync<LeadResponse>(Ct))!;
        }

        lead.Archived.ShouldBeTrue();
        lead.Notes.ShouldHaveSingleItem().Text.ShouldBe("Јавив се, ќе пробаме во среда.");
        (await ListAsync(site.Teacher)).Total.ShouldBe(0);
        (await ListAsync(site.Teacher, "archived=true")).Items.ShouldHaveSingleItem().Id.ShouldBe(id);

        using var restored = await site.Teacher.Client.PostAsync($"{path}/restore", new { version = lead.Version });
        (await restored.Content.ReadFromJsonAsync<LeadResponse>(Ct))!.Archived.ShouldBeFalse();
        (await ListAsync(site.Teacher)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task The_inbox_filters_searches_and_pages()
    {
        using var site = await PublishedTeacherAsync();
        await SubmitAsync(site, name: "Ана Ристова", email: "ana@example.test", message: "Физика за прва година");
        await SubmitAsync(site, name: "Бојан Колев", email: "bojan@example.test", type: "LessonRequest", message: null);
        var third = await SubmitAsync(site, name: "Цвета Ангелова", email: "cveta@example.test", message: "Помош за тест");
        var lead = await site.Teacher.Client.GetJsonAsync<LeadResponse>($"{LeadsPath(site.Teacher)}/{third}");
        await StatusAsync(site.Teacher, $"{LeadsPath(site.Teacher)}/{third}", "Contacted", lead.Version);

        (await ListAsync(site.Teacher)).Items.Select(i => i.Name).ShouldBe(["Цвета Ангелова", "Бојан Колев", "Ана Ристова"]); // newest first
        (await ListAsync(site.Teacher, "status=Contacted")).Items.ShouldHaveSingleItem().Id.ShouldBe(third);
        (await ListAsync(site.Teacher, "type=LessonRequest")).Items.ShouldHaveSingleItem().Name.ShouldBe("Бојан Колев");
        (await ListAsync(site.Teacher, $"q={Uri.EscapeDataString("физика")}")).Items.ShouldHaveSingleItem().Name.ShouldBe("Ана Ристова");
        (await ListAsync(site.Teacher, "q=bojan@")).Items.ShouldHaveSingleItem().Name.ShouldBe("Бојан Колев");
        (await ListAsync(site.Teacher, $"q={Uri.EscapeDataString("%")}")).Total.ShouldBe(0); // LIKE wildcards are literal
        var page = await ListAsync(site.Teacher, "page=2&pageSize=2");
        (page.Total, page.Items.Count, page.NewCount).ShouldBe((3, 1, 2));

        using var badStatus = await site.Teacher.Client.GetAsync($"{LeadsPath(site.Teacher)}?status=Spam");
        badStatus.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Leads_are_isolated_between_workspaces()
    {
        using var site = await PublishedTeacherAsync();
        var id = await SubmitAsync(site);
        using var intruder = await fixture.CreateTeacherAsync();
        var path = $"{LeadsPath(site.Teacher)}/{id}";

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, LeadsPath(site.Teacher));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, $"{path}/status", new { status = "Closed", version = 0 });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/notes", new { text = "x" });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/archive", new { version = 0 });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"{path}/restore", new { version = 0 });
        // Even inside its own workspace route, another workspace's lead id is "not found".
        using var foreign = await intruder.Client.GetAsync($"{LeadsPath(intruder)}/{id}");
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(intruder)).Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_database_keeps_a_lead_offer_inside_the_same_workspace()
    {
        using var site = await PublishedTeacherAsync();
        using var other = await PublishedTeacherAsync();
        var id = await SubmitAsync(site);

        await using var db = fixture.CreateDbContext();
        var error = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE Leads SET OfferId = {other.PublicOffer.Id} WHERE Id = {id}", Ct));
        error.Number.ShouldBe(547); // composite tenant FK
    }

    private async Task<PublishedTeacher> PublishedTeacherAsync()
    {
        var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var catalog = await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog");
        var math = catalog.Subjects.Single(s => s.Code == "math").Id;
        using (var profile = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile", new { firstName = "Ана", lastName = "Петровска", subjectIds = new[] { math } }))
        {
            profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var offer = await CreateOfferAsync(teacher, math, "Јавен час");
        var slug = UniqueSlug();
        var saved = await SaveSiteAsync(teacher, slug);
        using (var publish = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/site/publish", new { version = saved.Version }))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync(Ct));
        }

        return new PublishedTeacher(teacher, slug, math, offer);
    }

    /// <summary>A form token issued 10 seconds ago (a person had time to fill in the form).</summary>
    private Task<string> TokenAsync(string slug, DateTimeOffset? issuedAt = null) =>
        fixture.WithServiceAsync<LeadFormTokens, string>(tokens => Task.FromResult(tokens.Issue(slug, issuedAt ?? DateTimeOffset.UtcNow.AddSeconds(-10))));

    private async Task<Guid> SubmitAsync(PublishedTeacher site, string name = "Марија Илиевска", string email = "marija@example.test", string type = "Message", string? message = "Здраво")
    {
        using var visitor = fixture.Factory.CreateClient();
        using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Request(await TokenAsync(site.Slug), name: name, email: email, type: type, message: message), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<Guid>($"SELECT TOP (1) Id AS [Value] FROM Leads WHERE WorkspaceId = {site.Teacher.WorkspaceId} AND Email = {email} ORDER BY CreatedAtUtc DESC").SingleAsync(Ct);
    }

    private static object Request(
        string token,
        string type = "Message",
        string? name = "Марија Илиевска",
        string? email = "marija@example.test",
        string? phone = "+389 70 123 456",
        string? message = "Здраво, ми треба помош по математика.",
        Guid? offerId = null,
        Guid? subjectId = null,
        bool consent = true,
        bool ageConfirmed = true,
        string consentVersion = LeadConsent.Version,
        string? website = "") =>
        new { type, name, email, phone, message, offerId, subjectId, consent, ageConfirmed, consentVersion, formToken = token, website };

    private static async Task<LeadResponse> StatusAsync(TeacherSession teacher, string path, string status, int version)
    {
        using var response = await teacher.Client.PutAsync($"{path}/status", new { status, version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LeadResponse>(Ct))!;
    }

    private static Task<LeadListResponse> ListAsync(TeacherSession teacher, string query = "") =>
        teacher.Client.GetJsonAsync<LeadListResponse>($"{LeadsPath(teacher)}?{query}");

    private static string LeadsPath(TeacherSession teacher) => $"/api/w/{teacher.WorkspaceId}/leads";

    private static string UniqueSlug() => $"l-{Guid.NewGuid().ToString("N")[..12]}";

    private static async Task<SiteResponse> SaveSiteAsync(TeacherSession teacher, string slug)
    {
        using var response = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/site",
            new { slug, template = "classic", palette = "ocean", language = "mk", draft = new { sections = SiteService.DefaultContent("mk").Sections }, version = (int?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
    }

    private static async Task<OfferResponse> CreateOfferAsync(TeacherSession teacher, Guid subject, string title, bool showOnPublicSite = true, bool isActive = true)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/offers", new
        {
            kind = "SingleLesson",
            title,
            subjectId = subject,
            durationMinutes = 60,
            priceMinor = 120_000L,
            mode = "Online",
            isActive,
            showOnPublicSite,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(Ct))!;
    }
}
