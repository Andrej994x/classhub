using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Leads.Application;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.DiagnosticTests;

/// <summary>R1-P7: public test taking with server-side scoring, the diagnostic lead and the teacher's results view.</summary>
[Collection(ApiCollection.Name)]
public sealed class PublicTestTests(ClassHubApiFixture fixture)
{
    private const string BrowserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Published(TeacherSession Teacher, string SiteSlug, DiagnosticTestResponse Test) : IDisposable
    {
        public void Dispose() => Teacher.Dispose();

        public string Path => $"/api/public/sites/{SiteSlug}/tests/{Test.Slug}";
    }

    [Fact]
    public async Task A_published_test_is_public_without_its_correct_answers()
    {
        using var site = await PublishedTestAsync();
        using var visitor = Visitor();

        using var response = await visitor.GetAsync(site.Path, Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        raw.ShouldNotContain("isCorrect", Case.Insensitive);
        raw.ShouldNotContain("correct", Case.Insensitive);
        var test = (await response.Content.ReadFromJsonAsync<PublicTestResponse>(Ct))!;
        (test.Title, test.Subject, test.TotalPoints).ShouldBe(("Дропки", "Математика", 5));
        test.Questions.Select(q => q.SelectSeveral).ShouldBe([false, true]); // the second has two correct options
        test.Questions[1].Options.Select(o => o.Text).ShouldBe(["2", "3", "4"]);
    }

    [Fact]
    public async Task Drafts_inactive_and_archived_tests_and_unpublished_sites_are_not_public()
    {
        using var site = await PublishedTestAsync();
        var math = site.Test.SubjectId;
        var draft = await CreateTestAsync(site.Teacher, math, "Нацрт");
        using var visitor = Visitor();

        (await visitor.GetAsync($"/api/public/sites/{site.SiteSlug}/tests/{draft.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/{site.SiteSlug}/tests/{draft.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await visitor.GetAsync($"/api/public/sites/{site.SiteSlug}/tests/nema-takov", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var live = await site.Teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"{TestsPath(site.Teacher)}/{site.Test.Id}");
        await ChangeAsync(site.Teacher, site.Test.Id, "deactivate", live.Version);
        (await visitor.GetAsync(site.Path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var page = await visitor.GetAsync($"/{site.SiteSlug}/tests/{site.Test.Slug}", Ct))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.NotFound); // the cached page was evicted
            page.Headers.GetValues("X-Robots-Tag").ShouldBe(["noindex"]);
        }

        // An unpublished site hides its tests too.
        using var other = await PublishedTestAsync();
        var siteState = await other.Teacher.Client.GetJsonAsync<SiteResponse>($"/api/w/{other.Teacher.WorkspaceId}/site");
        (await other.Teacher.Client.PostAsync($"/api/w/{other.Teacher.WorkspaceId}/site/unpublish", new { version = siteState.Version })).Dispose();
        (await visitor.GetAsync(other.Path, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Answers_are_scored_on_the_server_and_the_attempt_stays_anonymous()
    {
        using var site = await PublishedTestAsync();
        using var visitor = Visitor();
        var q1 = site.Test.Questions[0];
        var q2 = site.Test.Questions[1];

        var perfect = await SubmitAsync(visitor, site, (q1.Id, [Correct(q1)]), (q2.Id, Correct(q2, all: true)));
        (perfect.Points, perfect.MaxPoints, perfect.QuestionsRight, perfect.TotalQuestions).ShouldBe((5, 5, 2, 2));

        // Only one of the two correct options: no points for question 2.
        var partial = await SubmitAsync(visitor, site, (q1.Id, [Correct(q1)]), (q2.Id, [Correct(q2)]));
        (partial.Points, partial.QuestionsRight).ShouldBe((2, 1));
        var empty = await SubmitAsync(visitor, site);
        (empty.Points, empty.QuestionsRight).ShouldBe((0, 0));
        perfect.AttemptId.ShouldNotBe(partial.AttemptId);
        perfect.AttemptId.Version.ShouldBe(4); // random, not the sequential default

        var test = await site.Teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"{TestsPath(site.Teacher)}/{site.Test.Id}");
        test.AttemptCount.ShouldBe(3);
        await using var db = fixture.CreateDbContext();
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TestAttempts'").ToListAsync(Ct);
        columns.Order(StringComparer.Ordinal).ShouldBe(
            ["AnswersJson", "CompletedAtUtc", "CorrectQuestions", "Id", "LeadId", "MaxPoints", "Points", "ResultSharedAtUtc", "TestId", "TotalQuestions", "WorkspaceId"]);
    }

    [Fact]
    public async Task Tampered_submissions_are_refused_and_never_stored()
    {
        using var site = await PublishedTestAsync();
        using var other = await PublishedTestAsync();
        using var visitor = Visitor();
        var q1 = site.Test.Questions[0];
        var q2 = site.Test.Questions[1];
        var foreign = other.Test.Questions[0];

        foreach (var (name, answers) in new (string, object[])[]
                 {
                     ("foreign question", [new { questionId = foreign.Id, optionIds = new[] { foreign.Options[0].Id } }]),
                     ("option of another question", [new { questionId = q1.Id, optionIds = new[] { q2.Options[0].Id } }]),
                     ("unknown option", [new { questionId = q1.Id, optionIds = new[] { Guid.NewGuid() } }]),
                     ("question twice", [new { questionId = q1.Id, optionIds = new[] { q1.Options[0].Id } }, new { questionId = q1.Id, optionIds = new[] { q1.Options[1].Id } }]),
                     ("option twice", [new { questionId = q2.Id, optionIds = new[] { q2.Options[0].Id, q2.Options[0].Id } }]),
                 })
        {
            using var response = await visitor.PostAsJsonAsync($"{site.Path}/attempts", new { answers }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
        }

        (await site.Teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"{TestsPath(site.Teacher)}/{site.Test.Id}")).AttemptCount.ShouldBe(0);
    }

    [Fact]
    public async Task Parallel_attempts_never_exceed_the_hourly_cap_of_a_test()
    {
        using var site = await PublishedTestAsync();
        await using var limited = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("DiagnosticTests:MaxAttemptsPerTestPerHour", "3"));

        var statuses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var client = limited.CreateClient();
            using var response = await client.PostAsJsonAsync($"{site.Path}/attempts", new { answers = Array.Empty<object>() }, Ct);
            return response.StatusCode;
        }));

        statuses.Count(s => s == HttpStatusCode.OK).ShouldBe(3);
        statuses.Count(s => s == HttpStatusCode.TooManyRequests).ShouldBe(5);
        (await site.Teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"{TestsPath(site.Teacher)}/{site.Test.Id}")).AttemptCount.ShouldBe(3);
    }

    [Fact]
    public async Task A_visitor_sends_a_request_with_the_result_and_the_teacher_sees_it_only_when_shared()
    {
        using var site = await PublishedTestAsync();
        using var visitor = Visitor();
        var q1 = site.Test.Questions[0];
        var shared = await SubmitAsync(visitor, site, (q1.Id, [Correct(q1)]));
        var private_ = await SubmitAsync(visitor, site);

        await SendContactAsync(site, shared.AttemptId, shareResult: true, name: "Марија");
        await SendContactAsync(site, private_.AttemptId, shareResult: false, name: "Бојан");

        var leads = await site.Teacher.Client.GetJsonAsync<LeadListResponse>($"/api/w/{site.Teacher.WorkspaceId}/leads?type=Diagnostic");
        leads.Total.ShouldBe(2);
        var withResult = await site.Teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{site.Teacher.WorkspaceId}/leads/{leads.Items.Single(l => l.Name == "Марија").Id}");
        withResult.DiagnosticResult.ShouldBe(new DiagnosticResultSummary(site.Test.Id, "Дропки", 2, 5));
        var withoutResult = await site.Teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{site.Teacher.WorkspaceId}/leads/{leads.Items.Single(l => l.Name == "Бојан").Id}");
        withoutResult.DiagnosticResult.ShouldBe(new DiagnosticResultSummary(site.Test.Id, "Дропки", null, null));

        var attempts = await site.Teacher.Client.GetJsonAsync<TestAttemptsResponse>($"{TestsPath(site.Teacher)}/{site.Test.Id}/attempts");
        (attempts.Total, attempts.SharedCount).ShouldBe((2, 1));
        var item = attempts.Shared.ShouldHaveSingleItem();
        (item.AttemptId, item.Points, item.MaxPoints, item.LeadId).ShouldBe((shared.AttemptId, 2, 5, (Guid?)withResult.Id));
    }

    [Fact]
    public async Task An_attempt_gets_at_most_one_request_and_only_from_its_own_site()
    {
        using var site = await PublishedTestAsync();
        using var other = await PublishedTestAsync();
        using var visitor = Visitor();
        var attempt = await SubmitAsync(visitor, site);
        await SendContactAsync(site, attempt.AttemptId, shareResult: false);

        using (var again = await PostContactAsync(site.SiteSlug, attempt.AttemptId, shareResult: true))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await again.ReadJsonAsync()).GetProperty("errors").TryGetProperty("attemptId", out _).ShouldBeTrue();
        }

        // Another teacher's site cannot take this attempt; a made-up id neither.
        using (var foreignSite = await PostContactAsync(other.SiteSlug, attempt.AttemptId, shareResult: false))
        {
            foreignSite.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var unknown = await PostContactAsync(site.SiteSlug, Guid.NewGuid(), shareResult: false))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // A diagnostic request needs its attempt; other requests cannot carry one or share a result.
        foreach (var body in new object[]
                 {
                     ContactBody(await TokenAsync(site.SiteSlug), type: "Diagnostic", attemptId: null),
                     ContactBody(await TokenAsync(site.SiteSlug), type: "Message", attemptId: attempt.AttemptId),
                     ContactBody(await TokenAsync(site.SiteSlug), type: "Message", attemptId: null, shareResult: true),
                 })
        {
            using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.SiteSlug}/leads", body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await site.Teacher.Client.GetJsonAsync<LeadListResponse>($"/api/w/{site.Teacher.WorkspaceId}/leads")).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Attempts_lock_destructive_edits_for_real()
    {
        using var site = await PublishedTestAsync();
        using var visitor = Visitor();
        await SubmitAsync(visitor, site);
        var test = await site.Teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"{TestsPath(site.Teacher)}/{site.Test.Id}");

        using var response = await site.Teacher.Client.PutAsync($"{TestsPath(site.Teacher)}/{site.Test.Id}", new
        {
            title = test.Title,
            subjectId = test.SubjectId,
            difficulty = test.Difficulty,
            questions = Array.Empty<object>(),
            version = test.Version,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // published: must stay publishable first …
        var inactive = await ChangeAsync(site.Teacher, site.Test.Id, "deactivate", test.Version);
        using var destructive = await site.Teacher.Client.PutAsync($"{TestsPath(site.Teacher)}/{site.Test.Id}", new
        {
            title = test.Title,
            subjectId = test.SubjectId,
            difficulty = test.Difficulty,
            questions = Array.Empty<object>(),
            version = inactive.Version,
        });
        (await destructive.ErrorCodeAsync()).ShouldBe(ErrorCodes.TestHasAttempts); // … and the attempt locks the structure
    }

    [Fact]
    public async Task The_test_page_is_indexable_and_linked_from_the_site_and_the_sitemap()
    {
        using var site = await PublishedTestAsync();
        using var visitor = Visitor();

        using var page = await visitor.GetAsync($"/{site.SiteSlug}/tests/{site.Test.Slug}", Ct);
        var html = await page.Content.ReadAsStringAsync(Ct);
        page.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        html.ShouldContain("<title>Дропки — Ана Петровска</title>");
        html.ShouldContain($"<link rel=\"canonical\" href=\"http://classhub.test/{site.SiteSlug}/tests/{site.Test.Slug}\" />");
        html.ShouldContain("data-island=\"diagnostic-test\"");
        html.ShouldNotContain("name=\"robots\"");
        html.ShouldNotContain("isCorrect", Case.Insensitive);

        (await visitor.GetStringAsync($"/{site.SiteSlug}", Ct)).ShouldContain($"href=\"/{site.SiteSlug}/tests/{site.Test.Slug}\"");
        (await visitor.GetStringAsync("/sitemap.xml", Ct)).ShouldContain($"<loc>http://classhub.test/{site.SiteSlug}/tests/{site.Test.Slug}</loc>");
    }

    [Fact]
    public async Task The_teacher_results_view_is_isolated()
    {
        using var site = await PublishedTestAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, $"{TestsPath(site.Teacher)}/{site.Test.Id}/attempts");
        (await intruder.Client.GetAsync($"{TestsPath(intruder)}/{site.Test.Id}/attempts")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private HttpClient Visitor()
    {
        var client = fixture.Factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserAgent);
        return client;
    }

    private static Guid Correct(TestQuestionResponse question) => question.Options.First(o => o.IsCorrect).Id;

    private static Guid[] Correct(TestQuestionResponse question, bool all) => [.. question.Options.Where(o => o.IsCorrect).Select(o => o.Id)];

    private static async Task<PublicAttemptResult> SubmitAsync(HttpClient visitor, Published site, params (Guid Question, Guid[] Options)[] answers)
    {
        using var response = await visitor.PostAsJsonAsync($"{site.Path}/attempts",
            new { answers = answers.Select(a => new { questionId = a.Question, optionIds = a.Options }).ToArray() }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PublicAttemptResult>(Ct))!;
    }

    private async Task SendContactAsync(Published site, Guid attemptId, bool shareResult, string name = "Марија")
    {
        using var response = await PostContactAsync(site.SiteSlug, attemptId, shareResult, name);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<HttpResponseMessage> PostContactAsync(string siteSlug, Guid attemptId, bool shareResult, string name = "Марија")
    {
        using var visitor = Visitor();
        return await visitor.PostAsJsonAsync($"/api/public/sites/{siteSlug}/leads",
            ContactBody(await TokenAsync(siteSlug), "Diagnostic", attemptId, shareResult, name), Ct);
    }

    private static object ContactBody(string token, string type, Guid? attemptId, bool shareResult = false, string name = "Марија") => new
    {
        type,
        name,
        email = "visitor@example.test",
        message = (string?)null,
        consent = true,
        ageConfirmed = true,
        consentVersion = LeadConsent.Version,
        formToken = token,
        website = "",
        attemptId,
        shareResult,
    };

    private Task<string> TokenAsync(string slug) =>
        fixture.WithServiceAsync<LeadFormTokens, string>(tokens => Task.FromResult(tokens.Issue(slug, DateTimeOffset.UtcNow.AddSeconds(-10))));

    private async Task<Published> PublishedTestAsync()
    {
        var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var catalog = await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog");
        var math = catalog.Subjects.Single(s => s.Code == "math").Id;
        (await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/profile", new { firstName = "Ана", lastName = "Петровска", subjectIds = new[] { math } })).Dispose();
        var slug = $"pt-{Guid.NewGuid().ToString("N")[..12]}";
        using (var saved = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/site",
                   new { slug, template = "classic", palette = "ocean", language = "mk", draft = new { sections = SiteService.DefaultContent("mk").Sections }, version = (int?)null }))
        {
            var site = (await saved.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/site/publish", new { version = site.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var test = await CreateTestAsync(teacher, math, "Дропки");
        var published = await ChangeAsync(teacher, test.Id, "publish", test.Version);
        return new Published(teacher, slug, published);
    }

    private static async Task<DiagnosticTestResponse> CreateTestAsync(TeacherSession teacher, Guid subject, string title)
    {
        using var response = await teacher.Client.PostAsync(TestsPath(teacher), new
        {
            title,
            subjectId = subject,
            difficulty = "Intermediate",
            questions = new object[]
            {
                new { text = "Колку е 1/2 + 1/4?", points = 2, options = new[] { new { text = "3/4", isCorrect = true }, new { text = "2/6", isCorrect = false } } },
                new { text = "Кои се прости броеви?", points = 3, options = new[] { new { text = "2", isCorrect = true }, new { text = "3", isCorrect = true }, new { text = "4", isCorrect = false } } },
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static async Task<DiagnosticTestResponse> ChangeAsync(TeacherSession teacher, Guid testId, string action, int version)
    {
        using var response = await teacher.Client.PostAsync($"{TestsPath(teacher)}/{testId}/{action}", new { version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static string TestsPath(TeacherSession teacher) => $"/api/w/{teacher.WorkspaceId}/tests";
}
