using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Modules.Admin.Contracts;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Subscriptions.Jobs;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Subscriptions;

/// <summary>R1-P10: Free on registration, limits, manual Pro activation and extension, expiry without data loss.</summary>
[Collection(ApiCollection.Name)]
public sealed class PlanTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_teacher_is_on_free_with_the_provisional_limits()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        var plan = await teacher.Client.GetJsonAsync<TeacherPlanResponse>($"/api/w/{teacher.WorkspaceId}/plan");

        plan.Code.ShouldBe(PlanCodes.Free);
        plan.PaidUntilUtc.ShouldBeNull();
        plan.Limits.Select(l => (l.Key, l.Limit)).ShouldBe([
            (PlanLimits.ActiveStudents, 10), (PlanLimits.PublishedTests, 3), (PlanLimits.Activities, 10), (PlanLimits.StorageMb, 500), (PlanLimits.AnalyticsLevel, 1)]);
        plan.Limits.Single(l => l.Key == PlanLimits.PublishedTests).Used.ShouldBe(0);

        using var intruder = await fixture.CreateTeacherAsync();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/plan");
    }

    [Fact]
    public async Task Free_blocks_the_fourth_published_test_and_pro_raises_the_limit()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        var subject = await MathAsync(teacher);
        for (var i = 0; i < 3; i++)
        {
            (await PublishAsync(teacher, await CreateTestAsync(teacher, subject))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var fourth = await CreateTestAsync(teacher, subject);
        using (var blocked = await PublishAsync(teacher, fourth))
        {
            blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await blocked.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);
        }

        // Drafts are not limited; only publishing is.
        (await teacher.Client.GetJsonAsync<DiagnosticTestResponse>($"/api/w/{teacher.WorkspaceId}/tests/{fourth.Id}")).Status.ShouldBe("Draft");

        var activated = await ActivateAsync(admin, teacher.WorkspaceId, months: 1);
        activated.PlanCode.ShouldBe(PlanCodes.Pro);
        (await PublishAsync(teacher, fourth)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var plan = await teacher.Client.GetJsonAsync<TeacherPlanResponse>($"/api/w/{teacher.WorkspaceId}/plan");
        plan.Code.ShouldBe(PlanCodes.Pro);
        plan.Limits.Single(l => l.Key == PlanLimits.PublishedTests).ShouldBe(new PlanLimitUsage(PlanLimits.PublishedTests, 50, 4));
    }

    [Fact]
    public async Task Parallel_publications_near_the_limit_cannot_exceed_it()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var subject = await MathAsync(teacher);
        (await PublishAsync(teacher, await CreateTestAsync(teacher, subject))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PublishAsync(teacher, await CreateTestAsync(teacher, subject))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var candidates = new List<DiagnosticTestResponse>();
        for (var i = 0; i < 4; i++)
        {
            candidates.Add(await CreateTestAsync(teacher, subject));
        }

        var responses = await Task.WhenAll(candidates.Select(test => PublishAsync(teacher, test)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(3);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        (await teacher.Client.GetJsonAsync<TeacherPlanResponse>($"/api/w/{teacher.WorkspaceId}/plan"))
            .Limits.Single(l => l.Key == PlanLimits.PublishedTests).Used.ShouldBe(3);
    }

    [Fact]
    public async Task Activation_records_the_payment_and_extension_adds_to_the_end()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();

        var first = await ActivateAsync(admin, teacher.WorkspaceId, months: 1, amountMinor: 120_000);
        var firstEnd = first.PaidUntilUtc!.Value;
        firstEnd.ShouldBeInRange(DateTime.UtcNow.AddMonths(1).AddMinutes(-2), DateTime.UtcNow.AddMonths(1).AddMinutes(2));
        var activation = first.History.ShouldHaveSingleItem();
        activation.Kind.ShouldBe("Activated");
        activation.ShouldSatisfyAllConditions(
            () => activation.AmountMinor.ShouldBe(120_000),
            () => activation.Currency.ShouldBe("MKD"),
            () => activation.PaymentMethod.ShouldBe("bank-transfer"),
            () => activation.PaymentReference.ShouldBe("INV-2026-001"),
            () => activation.ActorUserId.ShouldBe(admin.Me.Id));

        var extended = await ActivateAsync(admin, teacher.WorkspaceId, months: 2, amountMinor: 240_000);
        extended.PaidUntilUtc.ShouldBe(firstEnd.AddMonths(2));
        extended.History.Select(h => h.Kind).ShouldBe(["Extended", "Activated"]);

        await using var db = fixture.CreateDbContext();
        var target = teacher.WorkspaceId.ToString();
        (await db.Set<AuditLogEntry>().Where(e => e.TargetId == target && e.Action.StartsWith("subscription.pro.")).Select(e => e.Action).ToListAsync(Ct))
            .ShouldBe(["subscription.pro.activated", "subscription.pro.extended"], ignoreOrder: true);

        var overview = await admin.Client.GetJsonAsync<AdminOverviewResponse>("/api/admin/overview");
        overview.ProWorkspaces.ShouldBeGreaterThanOrEqualTo(1);
        overview.RevenueLast30Days.Single(r => r.Currency == "MKD").AmountMinor.ShouldBeGreaterThanOrEqualTo(360_000);
    }

    [Theory]
    [InlineData(0, 1000L, "MKD", "bank-transfer", "months")]
    [InlineData(37, 1000L, "MKD", "bank-transfer", "months")]
    [InlineData(1, 0L, "MKD", "bank-transfer", "amountMinor")]
    [InlineData(1, 1000L, "MKD", "complimentary", "amountMinor")]
    [InlineData(1, 1000L, "XXX", "bank-transfer", "currency")]
    [InlineData(1, 1000L, "MKD", "barter", "paymentMethod")]
    public async Task Activation_requests_are_validated(int months, long amountMinor, string currency, string method, string field)
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();

        using var response = await admin.Client.PostAsync($"/api/admin/workspaces/{teacher.WorkspaceId}/subscription/pro",
            new { months, amountMinor, currency, paymentMethod = method, paidOn = Today() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        (await admin.Client.GetJsonAsync<AdminSubscriptionResponse>($"/api/admin/workspaces/{teacher.WorkspaceId}/subscription")).History.ShouldBeEmpty();
    }

    [Fact]
    public async Task Expiry_moves_back_to_free_without_losing_data_and_blocks_new_publications()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        var subject = await MathAsync(teacher);
        await ActivateAsync(admin, teacher.WorkspaceId, months: 1);
        var published = new List<DiagnosticTestResponse>();
        for (var i = 0; i < 4; i++)
        {
            var test = await CreateTestAsync(teacher, subject);
            (await PublishAsync(teacher, test)).StatusCode.ShouldBe(HttpStatusCode.OK);
            published.Add(test);
        }

        await using (var db = fixture.CreateDbContext())
        {
            await EndPaidPeriodAsync(db, teacher.WorkspaceId);
        }

        // The limits follow the end date at once, even before the job runs.
        (await teacher.Client.GetJsonAsync<TeacherPlanResponse>($"/api/w/{teacher.WorkspaceId}/plan")).Code.ShouldBe(PlanCodes.Free);

        var job = fixture.Factory.Services.GetRequiredService<SubscriptionExpiryJob>();
        (await job.RunOnceAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        (await job.RunOnceAsync(Ct)).ShouldBe(0);

        var subscription = await admin.Client.GetJsonAsync<AdminSubscriptionResponse>($"/api/admin/workspaces/{teacher.WorkspaceId}/subscription");
        subscription.PlanCode.ShouldBe(PlanCodes.Free);
        subscription.History.Select(h => h.Kind).ShouldBe(["Expired", "Activated"]);
        subscription.History[0].ActorUserId.ShouldBeNull();

        // D-29b: the four published tests stay published (over the Free limit); a fifth is blocked.
        var tests = await teacher.Client.GetJsonAsync<List<DiagnosticTestListItem>>($"/api/w/{teacher.WorkspaceId}/tests");
        tests.Count(t => t.Status == "Published").ShouldBe(4);
        using var blocked = await PublishAsync(teacher, await CreateTestAsync(teacher, subject));
        blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await blocked.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);

        await using var check = fixture.CreateDbContext();
        var target = teacher.WorkspaceId.ToString();
        (await check.Set<AuditLogEntry>().CountAsync(e => e.TargetId == target && e.Action == "subscription.expired" && e.ActorUserId == null, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Reactivating_before_the_job_runs_still_records_the_expiry()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        await ActivateAsync(admin, teacher.WorkspaceId, months: 1);
        await using (var db = fixture.CreateDbContext())
        {
            await EndPaidPeriodAsync(db, teacher.WorkspaceId);
        }

        var restarted = await ActivateAsync(admin, teacher.WorkspaceId, months: 1);

        restarted.History.Select(h => h.Kind).ShouldBe(["Activated", "Expired", "Activated"]);
        restarted.PaidUntilUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(27));
        // The job finds nothing left to do for this workspace and records no second expiry.
        await fixture.Factory.Services.GetRequiredService<SubscriptionExpiryJob>().RunOnceAsync(Ct);
        (await admin.Client.GetJsonAsync<AdminSubscriptionResponse>($"/api/admin/workspaces/{teacher.WorkspaceId}/subscription"))
            .History.Count(h => h.Kind == "Expired").ShouldBe(1);
        await using var check = fixture.CreateDbContext();
        var target = teacher.WorkspaceId.ToString();
        (await check.Set<AuditLogEntry>().CountAsync(e => e.TargetId == target && e.Action == "subscription.expired", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Teachers_cannot_change_their_own_plan()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        using var activate = await teacher.Client.PostAsync($"/api/admin/workspaces/{teacher.WorkspaceId}/subscription/pro",
            new { months = 12, amountMinor = 0, currency = "MKD", paymentMethod = "complimentary", paidOn = Today() });
        using var limits = await teacher.Client.PutAsync("/api/admin/plans/free", new { limits = Array.Empty<object>(), version = 0 });

        activate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        limits.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.Client.GetJsonAsync<TeacherPlanResponse>($"/api/w/{teacher.WorkspaceId}/plan")).Code.ShouldBe(PlanCodes.Free);
    }

    [Fact]
    public async Task Only_the_owner_changes_plan_limits_and_it_is_audited()
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        var plans = await admin.Client.GetJsonAsync<List<AdminPlanResponse>>("/api/admin/plans");
        var pro = plans.Single(p => p.Code == PlanCodes.Pro);
        var body = new { limits = pro.Limits.Select(l => new { key = l.Key, value = l.Key == PlanLimits.PublishedTests ? l.Value + 1 : l.Value }).ToArray(), version = pro.Version };

        (await admin.Client.PutAsync("/api/admin/plans/pro", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var missing = await owner.Client.PutAsync("/api/admin/plans/pro", new { limits = body.limits.Take(2).ToArray(), version = pro.Version }))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        try
        {
            using (var updated = await owner.Client.PutAsync("/api/admin/plans/pro", body))
            {
                updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
                var plan = (await updated.Content.ReadFromJsonAsync<AdminPlanResponse>(Ct))!;
                plan.Limits.Single(l => l.Key == PlanLimits.PublishedTests).Value.ShouldBe(pro.Limits.Single(l => l.Key == PlanLimits.PublishedTests).Value + 1);
                plan.Version.ShouldBe(pro.Version + 1);
            }

            using (var stale = await owner.Client.PutAsync("/api/admin/plans/pro", body))
            {
                stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
            }

            await using var db = fixture.CreateDbContext();
            (await db.Set<AuditLogEntry>().AnyAsync(e => e.Action == "plan.limits.updated" && e.TargetId == "pro" && e.ActorUserId == owner.Me.Id, Ct)).ShouldBeTrue();
        }
        finally
        {
            // Restore the seeded values for the other tests.
            var current = (await owner.Client.GetJsonAsync<List<AdminPlanResponse>>("/api/admin/plans")).Single(p => p.Code == PlanCodes.Pro);
            using var restore = await owner.Client.PutAsync("/api/admin/plans/pro", new { limits = pro.Limits.Select(l => new { key = l.Key, value = l.Value }).ToArray(), version = current.Version });
            restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("""{"version":0}""")]
    [InlineData("""{"version":0,"limits":null}""")]
    [InlineData("""{"version":0,"limits":[null]}""")]
    [InlineData("""{"version":0,"limits":[{"key":"published-tests","value":-1}]}""")]
    public async Task Malformed_plan_limit_requests_are_validation_errors(string body)
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await owner.Client.SendContentAsync(HttpMethod.Put, "/api/admin/plans/free", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
        (await response.ReadJsonAsync()).GetProperty("errors").EnumerateObject().ShouldNotBeEmpty();
    }

    /// <summary>Moves the workspace's paid period (and its activation record) two months back, ended a minute ago.</summary>
    private static async Task EndPaidPeriodAsync(Core.Infrastructure.Persistence.ClassHubDbContext db, Guid workspaceId)
    {
        await db.Database.ExecuteSqlAsync($"UPDATE WorkspaceSubscriptions SET StartsAtUtc = DATEADD(month, -2, SYSUTCDATETIME()), EndsAtUtc = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE WorkspaceId = {workspaceId}", Ct);
        await db.Database.ExecuteSqlAsync($"UPDATE SubscriptionEvents SET OccurredAtUtc = DATEADD(month, -2, SYSUTCDATETIME()), PeriodStartUtc = DATEADD(month, -2, SYSUTCDATETIME()), PeriodEndUtc = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE WorkspaceId = {workspaceId}", Ct);
    }

    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<AdminSubscriptionResponse> ActivateAsync(StaffSession admin, Guid workspaceId, int months, long amountMinor = 60_000)
    {
        using var response = await admin.Client.PostAsync($"/api/admin/workspaces/{workspaceId}/subscription/pro", new
        {
            months,
            amountMinor,
            currency = "MKD",
            paymentMethod = "bank-transfer",
            paymentReference = "INV-2026-001",
            paidOn = Today(),
            note = "Test activation",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AdminSubscriptionResponse>(Ct))!;
    }

    private static async Task<Guid> MathAsync(TeacherSession teacher) =>
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;

    private static async Task<DiagnosticTestResponse> CreateTestAsync(TeacherSession teacher, Guid subject)
    {
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests", new
        {
            title = $"Тест {Guid.NewGuid():N}"[..20],
            subjectId = subject,
            difficulty = "Beginner",
            questions = new[] { new { text = "1 + 1?", points = 1, options = new[] { new { text = "2", isCorrect = true }, new { text = "3", isCorrect = false } } } },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await created.Content.ReadFromJsonAsync<DiagnosticTestResponse>(Ct))!;
    }

    private static Task<HttpResponseMessage> PublishAsync(TeacherSession teacher, DiagnosticTestResponse test) =>
        teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/tests/{test.Id}/publish", new { version = test.Version });
}
