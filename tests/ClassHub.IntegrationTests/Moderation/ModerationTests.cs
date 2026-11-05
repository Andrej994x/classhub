using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Moderation.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using Microsoft.Extensions.DependencyInjection;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Moderation;

/// <summary>R1-P11: reports, the moderation queue, hidden content on every public path and the cache, catalog admin.</summary>
[Collection(ApiCollection.Name)]
public sealed class ModerationTests(ClassHubApiFixture fixture)
{
    private const string ReporterEmail = "reporter.private@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reports_are_validated_and_bots_are_ignored()
    {
        using var site = await PublishedAsync();
        using var visitor = fixture.Factory.CreateClient();

        (await ReportAsync(visitor, "no-such-site-xyz", new { target = "site", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReportAsync(visitor, site.Slug, new { target = "everything", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReportAsync(visitor, site.Slug, new { target = "site", reason = "boring" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReportAsync(visitor, site.Slug, new { target = "site", reason = "other" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // details needed
        (await ReportAsync(visitor, site.Slug, new { target = "section", sectionType = "nonsense", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReportAsync(visitor, site.Slug, new { target = "test", testSlug = "no-such-test", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var bot = await ReportAsync(visitor, site.Slug, new { target = "site", reason = "spam", website = "http://spam.example" });
        bot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await CountReportsAsync(site.Teacher.WorkspaceId)).ShouldBe(0);
    }

    [Fact]
    public async Task Reports_per_site_are_capped_per_hour()
    {
        using var site = await PublishedAsync();
        using var visitor = fixture.Factory.CreateClient();

        for (var i = 0; i < 22; i++)
        {
            (await ReportAsync(visitor, site.Slug, new { target = "site", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await CountReportsAsync(site.Teacher.WorkspaceId)).ShouldBe(20);
    }

    [Fact]
    public async Task Hiding_a_site_removes_it_from_every_public_path_and_tells_the_teacher_without_the_reporter()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var site = await PublishedAsync(withTest: true);
        using var visitor = fixture.Factory.CreateClient();
        (await visitor.GetAsync($"/{site.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // cached now
        await ReportAsync(visitor, site.Slug, new { target = "site", reason = "misleading", details = "Fake diplomas", email = ReporterEmail });
        await ReportAsync(visitor, site.Slug, new { target = "site", reason = "spam" });
        var since = DateTime.UtcNow.AddSeconds(-1);

        var queue = await admin.Client.GetJsonAsync<AdminReportPage>("/api/admin/reports?status=Open");
        var report = queue.Items.First(r => r.WorkspaceId == site.Teacher.WorkspaceId && r.Reason == "misleading");
        report.ShouldSatisfyAllConditions(
            () => report.ReporterEmail.ShouldBe(ReporterEmail),
            () => report.Details.ShouldBe("Fake diplomas"),
            () => report.SiteSlug.ShouldBe(site.Slug),
            () => report.OpenReportsForTarget.ShouldBe(2),
            () => report.TargetHidden.ShouldBeFalse());

        (await admin.Client.PostAsync($"/api/admin/reports/{report.Id}/hide", new { reason = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var hidden = await DecideAsync(admin, report.Id, "hide", "Misleading qualifications on the page");
        hidden.Status.ShouldBe("Resolved");
        hidden.TargetHidden.ShouldBeTrue();
        hidden.OpenReportsForTarget.ShouldBe(0); // the other report about the site is resolved too

        (await visitor.GetAsync($"/{site.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/{site.Slug}/tests/{site.Test!.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/api/public/sites/{site.Slug}/lead-form", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetStringAsync("/sitemap.xml", Ct)).ShouldNotContain($"/{site.Slug}<");
        (await ReportAsync(visitor, site.Slug, new { target = "site", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The teacher sees why, but not who reported; republishing does not bring the site back.
        var own = await site.Teacher.Client.GetJsonAsync<SiteResponse>($"/api/w/{site.Teacher.WorkspaceId}/site");
        own.Moderation.ShouldNotBeNull();
        own.Moderation.SiteHidden.ShouldBeTrue();
        own.Moderation.SiteReason.ShouldBe("Misleading qualifications on the page");
        var mail = await fixture.WaitForEmailAsync(site.Teacher.Email, "moderation.hidden", since);
        mail.TextBody.ShouldContain("Misleading qualifications on the page");
        mail.TextBody.ShouldNotContain(ReporterEmail);
        mail.TextBody.ShouldNotContain("Fake diplomas");
        await ChangeSiteAsync(site.Teacher, "unpublish");
        await ChangeSiteAsync(site.Teacher, "publish");
        (await visitor.GetAsync($"/{site.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await admin.Client.GetJsonAsync<List<HiddenContentResponse>>("/api/admin/moderation/hidden");
        list.ShouldContain(h => h.WorkspaceId == site.Teacher.WorkspaceId && h.Target == "site");

        using (var restore = await admin.Client.PostAsync("/api/admin/moderation/restore", new { workspaceId = site.Teacher.WorkspaceId, target = "site", reason = "Qualifications verified" }))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await visitor.GetAsync($"/{site.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Client.PostAsync("/api/admin/moderation/restore", new { workspaceId = site.Teacher.WorkspaceId, target = "site", reason = "Again" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await fixture.WaitForEmailAsync(site.Teacher.Email, "moderation.restored", since);

        await using var db = fixture.CreateDbContext();
        var key = site.Teacher.WorkspaceId.ToString();
        (await db.Set<AuditLogEntry>().Where(e => e.TargetId == key && e.Action.StartsWith("moderation.content.")).Select(e => e.Action).ToListAsync(Ct))
            .ShouldBe(["moderation.content.hidden", "moderation.content.restored"], ignoreOrder: true);
    }

    [Fact]
    public async Task Hiding_a_section_removes_only_that_section()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var site = await PublishedAsync();
        using var visitor = fixture.Factory.CreateClient();
        (await visitor.GetStringAsync($"/{site.Slug}", Ct)).ShouldContain("data-section=\"hero\"");
        await ReportAsync(visitor, site.Slug, new { target = "section", sectionType = "hero", reason = "inappropriate" });
        var report = (await admin.Client.GetJsonAsync<AdminReportPage>("/api/admin/reports?status=Open")).Items.First(r => r.WorkspaceId == site.Teacher.WorkspaceId);

        await DecideAsync(admin, report.Id, "hide", "Inappropriate photo");

        var page = await visitor.GetStringAsync($"/{site.Slug}", Ct);
        page.ShouldNotContain("data-section=\"hero\"");
        page.ShouldContain("data-section=");
        (await visitor.GetAsync($"/{site.Slug}/photo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var own = await site.Teacher.Client.GetJsonAsync<SiteResponse>($"/api/w/{site.Teacher.WorkspaceId}/site");
        own.Moderation!.SiteHidden.ShouldBeFalse();
        own.Moderation.Sections.ShouldHaveSingleItem().ShouldSatisfyAllConditions(s => s.Type.ShouldBe("hero"), s => s.Reason.ShouldBe("Inappropriate photo"));
        // A hidden section cannot be reported again.
        (await ReportAsync(visitor, site.Slug, new { target = "section", sectionType = "hero", reason = "spam" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parallel_decisions_about_different_sections_of_one_site_are_both_kept()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var otherAdmin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var site = await PublishedAsync();
        using var visitor = fixture.Factory.CreateClient();
        var moderated = await fixture.WithServiceAsync<ISiteModeration, ModeratedSite?>(s => s.FindPublicAsync(site.Slug, Ct));
        var sections = moderated!.VisibleSectionTypes.Take(2).ToList();
        sections.Count.ShouldBe(2);
        foreach (var section in sections)
        {
            await ReportAsync(visitor, site.Slug, new { target = "section", sectionType = section, reason = "spam" });
        }

        var reports = (await admin.Client.GetJsonAsync<AdminReportPage>("/api/admin/reports?status=Open")).Items.Where(r => r.WorkspaceId == site.Teacher.WorkspaceId).ToList();
        reports.Count.ShouldBe(2);

        var responses = await Task.WhenAll(
            admin.Client.PostAsync($"/api/admin/reports/{reports[0].Id}/hide", new { reason = "First decision" }),
            otherAdmin.Client.PostAsync($"/api/admin/reports/{reports[1].Id}/hide", new { reason = "Second decision" }));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var own = await site.Teacher.Client.GetJsonAsync<SiteResponse>($"/api/w/{site.Teacher.WorkspaceId}/site");
        own.Moderation!.Sections.Select(h => h.Type).ShouldBe(sections, ignoreOrder: true);
        var page = await visitor.GetStringAsync($"/{site.Slug}", Ct);
        foreach (var section in sections)
        {
            page.ShouldNotContain($"data-section=\"{section}\"");
        }
    }

    [Fact]
    public async Task Parallel_decisions_about_one_report_conflict()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var otherAdmin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var site = await PublishedAsync();
        using var visitor = fixture.Factory.CreateClient();
        await ReportAsync(visitor, site.Slug, new { target = "site", reason = "spam" });
        var report = (await admin.Client.GetJsonAsync<AdminReportPage>("/api/admin/reports?status=Open")).Items.First(r => r.WorkspaceId == site.Teacher.WorkspaceId);

        var responses = await Task.WhenAll(
            admin.Client.PostAsync($"/api/admin/reports/{report.Id}/hide", new { reason = "First decision" }),
            otherAdmin.Client.PostAsync($"/api/admin/reports/{report.Id}/hide", new { reason = "Second decision" }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        await using var db = fixture.CreateDbContext();
        var key = site.Teacher.WorkspaceId.ToString();
        (await db.Set<AuditLogEntry>().CountAsync(e => e.TargetId == key && e.Action == "moderation.content.hidden", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Hiding_a_test_removes_its_page_attempts_link_and_sitemap_entry()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        using var site = await PublishedAsync(withTest: true);
        using var visitor = fixture.Factory.CreateClient();
        var test = site.Test!;
        (await visitor.GetStringAsync($"/{site.Slug}", Ct)).ShouldContain($"/{site.Slug}/tests/{test.Slug}");
        await ReportAsync(visitor, site.Slug, new { target = "test", testSlug = test.Slug, reason = "copyright" });
        var report = (await admin.Client.GetJsonAsync<AdminReportPage>("/api/admin/reports?status=Open")).Items.First(r => r.WorkspaceId == site.Teacher.WorkspaceId);
        report.TestTitle.ShouldBe(test.Title);

        await DecideAsync(admin, report.Id, "hide", "Copied questions");

        (await visitor.GetAsync($"/{site.Slug}/tests/{test.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetStringAsync($"/{site.Slug}", Ct)).ShouldNotContain($"/tests/{test.Slug}");
        (await visitor.GetStringAsync("/sitemap.xml", Ct)).ShouldNotContain($"/tests/{test.Slug}<");
        using (var attempt = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/tests/{test.Slug}/attempts", new { version = test.Version, answers = Array.Empty<object>() }, Ct))
        {
            attempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var own = await site.Teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"/api/w/{site.Teacher.WorkspaceId}/tests/{test.Id}");
        own.HiddenReason.ShouldBe("Copied questions");
        own.Status.ShouldBe("Published"); // the teacher's data is unchanged
        (await site.Teacher.Client.GetJsonAsync<List<DiagnosticTestListItem>>($"/api/w/{site.Teacher.WorkspaceId}/tests")).Single(t => t.Id == test.Id).HiddenByModeration.ShouldBeTrue();
    }

    [Fact]
    public async Task Reports_can_be_dismissed_once_and_teachers_cannot_see_the_queue()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var site = await PublishedAsync();
        using var visitor = fixture.Factory.CreateClient();
        await ReportAsync(visitor, site.Slug, new { target = "site", reason = "spam", email = ReporterEmail });
        var report = (await admin.Client.GetJsonAsync<AdminReportPage>("/api/admin/reports?status=Open")).Items.First(r => r.WorkspaceId == site.Teacher.WorkspaceId);

        (await DecideAsync(admin, report.Id, "dismiss", "Not spam")).Status.ShouldBe("Dismissed");
        using (var again = await admin.Client.PostAsync($"/api/admin/reports/{report.Id}/hide", new { reason = "Changed my mind" }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await again.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        (await visitor.GetAsync($"/{site.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await site.Teacher.Client.GetAsync("/api/admin/reports")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await site.Teacher.Client.GetAsync($"/api/admin/reports/{report.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await site.Teacher.Client.GetAsync($"/api/w/{site.Teacher.WorkspaceId}/site")).Content.ReadAsStringAsync(Ct)).ShouldNotContain(ReporterEmail);
    }

    [Fact]
    public async Task Staff_manage_the_catalog_and_deactivated_entries_leave_new_choices()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        var catalog = await admin.Client.GetJsonAsync<AdminCatalogResponse>("/api/admin/catalog");
        var category = catalog.Categories.First();
        var code = $"robotics-{Guid.NewGuid():N}"[..20];

        AdminCatalogEntry created;
        using (var create = await admin.Client.PostAsync("/api/admin/catalog/subjects", new { code, nameMk = "Роботика", nameEn = "Robotics", sortOrder = 500, categoryId = category.Id }))
        {
            create.StatusCode.ShouldBe(HttpStatusCode.OK, await create.Content.ReadAsStringAsync(Ct));
            created = (await create.Content.ReadFromJsonAsync<AdminCatalogEntry>(Ct))!;
        }

        (await admin.Client.PostAsync("/api/admin/catalog/subjects", new { code, nameMk = "Друг", nameEn = "Other", categoryId = category.Id })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.Client.PostAsync("/api/admin/catalog/subjects", new { code = "Bad Code", nameMk = "X", nameEn = "X", categoryId = category.Id })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.ShouldContain(s => s.Id == created.Id);

        // The teacher uses the subject; then staff deactivate it.
        using (var profile = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile", new { firstName = "Ана", lastName = "Петровска", subjectIds = new[] { created.Id } }))
        {
            profile.StatusCode.ShouldBe(HttpStatusCode.OK, await profile.Content.ReadAsStringAsync(Ct));
        }

        using (var deactivate = await admin.Client.PutAsync($"/api/admin/catalog/subjects/{created.Id}", new { nameMk = "Роботика", nameEn = "Robotics", sortOrder = 500, isActive = false }))
        {
            deactivate.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await deactivate.Content.ReadFromJsonAsync<AdminCatalogEntry>(Ct))!.IsActive.ShouldBeFalse();
        }

        (await admin.Client.PutAsync($"/api/admin/catalog/subjects/{created.Id}", new { code = "renamed", nameMk = "Р", nameEn = "R" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.ShouldNotContain(s => s.Id == created.Id);
        // Never deleted: the teacher's saved reference stays in the database; it is only not offered for new choices.
        await using (var db = fixture.CreateDbContext())
        {
            (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM TeacherProfileSubjects WHERE SubjectId = {created.Id}").SingleAsync(Ct)).ShouldBe(1);
        }

        using (var reuse = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests", new
        {
            title = "Роботика",
            subjectId = created.Id,
            difficulty = "Beginner",
            questions = new[] { new { text = "?", points = 1, options = new[] { new { text = "a", isCorrect = true }, new { text = "b", isCorrect = false } } } },
        }))
        {
            reuse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Where it is already used it stays valid: the teacher can still edit and save.
        var loaded = await (await teacher.Client.GetAsync($"/api/w/{teacher.WorkspaceId}/profile")).ReadJsonAsync();
        using (var profile = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile", new { firstName = "Ана", lastName = "Петровска-Ристова", subjectIds = new[] { created.Id }, version = loaded.GetProperty("version").GetInt32() }))
        {
            profile.StatusCode.ShouldBe(HttpStatusCode.OK, await profile.Content.ReadAsStringAsync(Ct));
        }

        (await teacher.Client.GetAsync("/api/admin/catalog")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var check = fixture.CreateDbContext();
        (await check.Set<AuditLogEntry>().Where(e => e.TargetId == code).Select(e => e.Action).ToListAsync(Ct))
            .ShouldBe(["catalog.subject.created", "catalog.subject.updated"], ignoreOrder: true);
    }

    [Fact]
    public async Task Tests_and_offers_that_already_use_a_deactivated_subject_stay_editable()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        var category = (await admin.Client.GetJsonAsync<AdminCatalogResponse>("/api/admin/catalog")).Categories.First();
        AdminCatalogEntry subject;
        using (var create = await admin.Client.PostAsync("/api/admin/catalog/subjects", new { code = $"chess-{Guid.NewGuid():N}"[..16], nameMk = "Шах", nameEn = "Chess", categoryId = category.Id }))
        {
            subject = (await create.Content.ReadFromJsonAsync<AdminCatalogEntry>(Ct))!;
        }

        var questions = new[] { new { text = "?", points = 1, options = new[] { new { text = "a", isCorrect = true }, new { text = "b", isCorrect = false } } } };
        DiagnosticTestResponse test;
        using (var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests", new { title = "Шах 1", subjectId = subject.Id, difficulty = "Beginner", questions }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            test = (await created.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
        }

        OfferResponse offer;
        var offerBody = new { kind = "SingleLesson", title = "Шах час", description = "Час", subjectId = subject.Id, durationMinutes = 60, priceMinor = 100_000, mode = "Online", isActive = true, showOnPublicSite = true };
        using (var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/offers", offerBody))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            offer = (await created.Content.ReadFromJsonAsync<OfferResponse>(Ct))!;
        }

        (await admin.Client.PutAsync($"/api/admin/catalog/subjects/{subject.Id}", new { nameMk = "Шах", nameEn = "Chess", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var edit = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/tests/{test.Id}", new { title = "Шах 1 (поправено)", subjectId = subject.Id, difficulty = "Beginner", questions, version = test.Version }))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync(Ct));
        }

        using (var edit = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/offers/{offer.Id}", new { offerBody.kind, title = "Шах час (нов)", offerBody.description, offerBody.subjectId, offerBody.durationMinutes, offerBody.priceMinor, offerBody.mode, offerBody.isActive, offerBody.showOnPublicSite, version = offer.Version }))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync(Ct));
        }

        // A new test cannot pick the deactivated subject.
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests", new { title = "Шах 2", subjectId = subject.Id, difficulty = "Beginner", questions })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed record Published(TeacherSession Teacher, string Slug, DiagnosticTestResponse? Test) : IDisposable
    {
        public void Dispose() => Teacher.Dispose();
    }

    private async Task<Published> PublishedAsync(bool withTest = false)
    {
        var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        DiagnosticTestResponse? test = null;
        if (withTest)
        {
            var subject = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;
            using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests", new
            {
                title = "Тест по математика",
                subjectId = subject,
                difficulty = "Beginner",
                questions = new[] { new { text = "1 + 1?", points = 1, options = new[] { new { text = "2", isCorrect = true }, new { text = "3", isCorrect = false } } } },
            });
            var draft = (await created.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
            using var published = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests/{draft.Id}/publish", new { version = draft.Version });
            published.StatusCode.ShouldBe(HttpStatusCode.OK);
            test = (await published.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
        }

        var slug = $"mod-{Guid.NewGuid():N}"[..20];
        using (var save = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/site", new { slug, template = "classic", palette = "ocean", language = "mk", draft = new { sections = SiteService.DefaultContent("mk").Sections } }))
        {
            save.StatusCode.ShouldBe(HttpStatusCode.OK, await save.Content.ReadAsStringAsync(Ct));
        }

        await ChangeSiteAsync(teacher, "publish");
        return new Published(teacher, slug, test);
    }

    private static async Task ChangeSiteAsync(TeacherSession teacher, string action)
    {
        var site = await teacher.Client.GetJsonAsync<SiteResponse>($"/api/w/{teacher.WorkspaceId}/site");
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/site/{action}", new { version = site.Version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static Task<HttpResponseMessage> ReportAsync(HttpClient visitor, string slug, object body) =>
        visitor.PostAsJsonAsync($"/api/public/sites/{slug}/reports", body, Ct);

    private static async Task<AdminReportResponse> DecideAsync(StaffSession admin, Guid reportId, string action, string reason)
    {
        using var response = await admin.Client.PostAsync($"/api/admin/reports/{reportId}/{action}", new { reason });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AdminReportResponse>(Ct))!;
    }

    private async Task<int> CountReportsAsync(Guid workspaceId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM AbuseReports WHERE WorkspaceId = {workspaceId}").SingleAsync(Ct);
    }
}
