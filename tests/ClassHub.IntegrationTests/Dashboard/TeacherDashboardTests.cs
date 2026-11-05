using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Dashboard;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Leads.Application;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Dashboard;

/// <summary>R1-P8: the teacher dashboard shows exactly the workspace's own numbers for the chosen period.</summary>
[Collection(ApiCollection.Name)]
public sealed class TeacherDashboardTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_teacher_starts_with_zeros_and_a_full_period_of_days()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var dashboard = await DashboardAsync(teacher, 7);

        (dashboard.Days, dashboard.Leads.New, dashboard.Leads.InPeriod, dashboard.Tests.Published, dashboard.Offers.Active).ShouldBe((7, 0, 0, 0, 0));
        (dashboard.Site.Exists, dashboard.Site.Published, dashboard.Site.VisitsInPeriod).ShouldBe((false, false, 0));
        dashboard.Site.Daily.Count.ShouldBe(7);
        dashboard.Site.Daily[^1].Day.ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow));
        dashboard.Site.Daily.ShouldAllBe(d => d.Count == 0);
        dashboard.Leads.ByStatus.Select(s => s.Key).ShouldBe(["New", "Contacted", "Converted", "Closed"]);
        (dashboard.Onboarding.Completed, dashboard.Onboarding.Total).ShouldBe((0, 5));
        dashboard.Funnel.ShouldBe(new DashboardFunnel(0, 0, 0, 0));
    }

    [Fact]
    public async Task The_numbers_match_what_happened_in_the_period()
    {
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var math = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;
        (await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile", new { firstName = "Ана", lastName = "Петровска", subjectIds = new[] { math } })).Dispose();
        await OfferAsync(teacher, math, isActive: true, showOnPublicSite: true);
        await OfferAsync(teacher, math, isActive: true, showOnPublicSite: false);
        await OfferAsync(teacher, math, isActive: false, showOnPublicSite: false);
        var slug = $"d-{Guid.NewGuid().ToString("N")[..12]}";
        var site = await SaveSiteAsync(teacher, slug);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/site/publish", new { version = site.Version })).Dispose();

        // Leads: two messages and a lesson request now; one moved to Contacted, one to Converted; one archived.
        var a = await LeadAsync(teacher, slug, "Message", "a@example.test");
        var b = await LeadAsync(teacher, slug, "Message", "b@example.test");
        var c = await LeadAsync(teacher, slug, "LessonRequest", "c@example.test");
        await StatusAsync(teacher, b, "Contacted");
        await StatusAsync(teacher, c, "Converted");
        var archived = await LeadAsync(teacher, slug, "Message", "d@example.test");
        var lead = await teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{teacher.WorkspaceId}/leads/{archived}");
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/leads/{archived}/archive", new { version = lead.Version })).Dispose();

        // A test with two attempts (one shared through a diagnostic request).
        var test = await PublishedTestAsync(teacher, math);
        using var visitor = fixture.Factory.CreateClient();
        var first = await AttemptAsync(visitor, slug, test);
        await AttemptAsync(visitor, slug, test);
        await DiagnosticLeadAsync(slug, first, share: true);

        // Page views: three today; and 40 days ago a lead and some views that only the 90-day period includes.
        for (var i = 0; i < 3; i++)
        {
            await fixture.WithServiceAsync<IPublicSites, bool>(async s => { await s.RecordViewAsync(slug, Ct); return true; });
        }

        var old = DateTime.UtcNow.AddDays(-40);
        await using (var db = fixture.CreateDbContext())
        {
            var siteId = await db.Database.SqlQuery<Guid>($"SELECT Id AS [Value] FROM Sites WHERE WorkspaceId = {teacher.WorkspaceId}").SingleAsync(Ct);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO SitePageViews (SiteId, WorkspaceId, Day, Views) VALUES ({siteId}, {teacher.WorkspaceId}, {DateOnly.FromDateTime(old)}, 10)", Ct);
            await db.Database.ExecuteSqlAsync($"UPDATE Leads SET CreatedAtUtc = {old} WHERE Id = {a}", Ct);
        }

        var month = await DashboardAsync(teacher, 30);
        month.Leads.New.ShouldBe(2); // a (old, still New) + the diagnostic request; d is archived
        month.Leads.InPeriod.ShouldBe(4); // b, c, d (archived, but it arrived) and the diagnostic request
        month.Leads.ByStatus.ShouldBe([new("New", 2), new("Contacted", 1), new("Converted", 1), new("Closed", 0)]);
        month.Leads.ByTypeInPeriod.ShouldBe([new("Message", 2), new("LessonRequest", 1), new("Diagnostic", 1)]);
        month.Tests.ShouldBe(new DashboardTests(1, 2, 1));
        (month.Site.Exists, month.Site.Published, month.Site.VisitsInPeriod).ShouldBe((true, true, 3));
        month.Site.Daily[^1].Count.ShouldBe(3);
        month.Offers.ShouldBe(new DashboardOffers(2, 1));
        var checklist = await teacher.Client.GetJsonAsync<ClassHub.Core.Modules.Teachers.Contracts.OnboardingResponse>($"/api/w/{teacher.WorkspaceId}/onboarding");
        (month.Onboarding.Completed, month.Onboarding.Total).ShouldBe((checklist.Completed, checklist.Total));
        month.Onboarding.Completed.ShouldBe(4); // everything but the full profile (no bio or teaching mode here)
        month.Funnel.ShouldBe(new DashboardFunnel(3, 4, 2, 1));

        var quarter = await DashboardAsync(teacher, 90);
        (quarter.Site.VisitsInPeriod, quarter.Leads.InPeriod, quarter.Funnel.Visits, quarter.Site.Daily.Count).ShouldBe((13, 5, 13, 90));
    }

    [Fact]
    public async Task Only_7_30_or_90_days_and_only_the_own_workspace()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        using (var invalid = await teacher.Client.GetAsync($"/api/w/{teacher.WorkspaceId}/dashboard?days=10"))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/dashboard");
        (await DashboardAsync(teacher, 30)).Leads.InPeriod.ShouldBe(0);
    }

    private static Task<DashboardResponse> DashboardAsync(TeacherSession teacher, int days) =>
        teacher.Client.GetJsonAsync<DashboardResponse>($"/api/w/{teacher.WorkspaceId}/dashboard?days={days}");

    private static async Task OfferAsync(TeacherSession teacher, Guid subject, bool isActive, bool showOnPublicSite)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/offers", new
        {
            kind = "SingleLesson", title = "Час", subjectId = subject, durationMinutes = 60, priceMinor = 100_000L, mode = "Online", isActive, showOnPublicSite,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<SiteResponse> SaveSiteAsync(TeacherSession teacher, string slug)
    {
        using var response = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/site",
            new { slug, template = "classic", palette = "ocean", language = "mk", draft = new { sections = SiteService.DefaultContent("mk").Sections }, version = (int?)null });
        return (await response.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
    }

    private Task<string> TokenAsync(string slug) =>
        fixture.WithServiceAsync<LeadFormTokens, string>(tokens => Task.FromResult(tokens.Issue(slug, DateTimeOffset.UtcNow.AddSeconds(-10))));

    private async Task<Guid> LeadAsync(TeacherSession teacher, string slug, string type, string email)
    {
        using var visitor = fixture.Factory.CreateClient();
        using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{slug}/leads", new
        {
            type, name = "Посетител", email, message = "Здраво", consent = true, ageConfirmed = true, consentVersion = LeadConsent.Version, formToken = await TokenAsync(slug), website = "",
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<Guid>($"SELECT Id AS [Value] FROM Leads WHERE WorkspaceId = {teacher.WorkspaceId} AND Email = {email}").SingleAsync(Ct);
    }

    private async Task DiagnosticLeadAsync(string slug, Guid attemptId, bool share)
    {
        using var visitor = fixture.Factory.CreateClient();
        using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{slug}/leads", new
        {
            type = "Diagnostic", name = "Ученик", email = "student@example.test", consent = true, ageConfirmed = true, consentVersion = LeadConsent.Version,
            formToken = await TokenAsync(slug), website = "", attemptId, shareResult = share,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task StatusAsync(TeacherSession teacher, Guid leadId, string status)
    {
        var lead = await teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{teacher.WorkspaceId}/leads/{leadId}");
        if (status == "Converted" && lead.Status == "New")
        {
            (await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/leads/{leadId}/status", new { status = "Contacted", version = lead.Version })).Dispose();
            lead = await teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{teacher.WorkspaceId}/leads/{leadId}");
        }

        using var response = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/leads/{leadId}/status", new { status, version = lead.Version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<DiagnosticTestResponse> PublishedTestAsync(TeacherSession teacher, Guid subject)
    {
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests", new
        {
            title = "Тест",
            subjectId = subject,
            difficulty = "Beginner",
            questions = new[] { new { text = "1 + 1?", points = 1, options = new[] { new { text = "2", isCorrect = true }, new { text = "3", isCorrect = false } } } },
        });
        var test = (await created.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
        using var published = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests/{test.Id}/publish", new { version = test.Version });
        return (await published.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static async Task<Guid> AttemptAsync(HttpClient visitor, string slug, DiagnosticTestResponse test)
    {
        using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{slug}/tests/{test.Slug}/attempts", new { version = test.Version, answers = Array.Empty<object>() }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PublicAttemptResult>(Ct))!.AttemptId;
    }
}
