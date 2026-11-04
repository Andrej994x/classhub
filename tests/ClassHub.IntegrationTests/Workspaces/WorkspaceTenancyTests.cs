using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.Core.SharedKernel.Tenancy;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClassHub.IntegrationTests.Workspaces;

/// <summary>R0-P5: every teacher gets an isolated workspace; other tenants and admins cannot reach it.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkspaceTenancyTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registration_provisions_exactly_one_owned_workspace()
    {
        using var teacher = await fixture.CreateTeacherAsync(locale: "en");

        var mine = await teacher.Client.GetJsonAsync<List<MyWorkspaceResponse>>("/api/me/workspaces");
        var workspace = await teacher.Client.GetJsonAsync<WorkspaceResponse>($"/api/w/{teacher.WorkspaceId}");

        mine.ShouldHaveSingleItem().Role.ShouldBe("Owner");
        workspace.Name.ShouldBe("My workspace");
        workspace.Currency.ShouldBe("MKD");
        workspace.TimeZone.ShouldBe("Europe/Skopje");
        workspace.Locale.ShouldBe("en");
        workspace.Role.ShouldBe("Owner");

        await using var db = fixture.CreateDbContext();
        (await db.Set<WorkspaceMember>().IgnoreQueryFilters()
            .CountAsync(m => m.UserId == teacher.Me.Id, Ct)).ShouldBe(1);
        (await db.AuditLog.AnyAsync(a => a.Action == "workspace.created" && a.WorkspaceId == teacher.WorkspaceId, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Macedonian_teachers_get_a_macedonian_workspace_name()
    {
        using var teacher = await fixture.CreateTeacherAsync(locale: "mk");

        (await teacher.Client.GetJsonAsync<WorkspaceResponse>($"/api/w/{teacher.WorkspaceId}")).Name.ShouldBe("Мој работен простор");
    }

    [Fact]
    public async Task Owner_updates_settings_and_the_change_is_audited()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        using var response = await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}",
            new { name = "Математика со Ана", timeZone = "Europe/Berlin", currency = "eur", locale = "en" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(Ct);
        updated!.Name.ShouldBe("Математика со Ана");
        updated.Currency.ShouldBe("EUR");
        updated.TimeZone.ShouldBe("Europe/Berlin");

        await using var db = fixture.CreateDbContext();
        var entry = await db.AuditLog.SingleAsync(a => a.Action == "workspace.settings.updated" && a.WorkspaceId == teacher.WorkspaceId, Ct);
        entry.ActorUserId.ShouldBe(teacher.Me.Id);
        entry.DetailsJson.ShouldNotBeNull().ShouldContain("Europe/Berlin");
    }

    [Theory]
    [InlineData("timeZone", "UTC", "UTC")]
    [InlineData("timeZone", "America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [InlineData("currency", "usd", "USD")]
    public async Task Valid_settings_without_the_usual_shape_are_accepted(string field, string value, string stored)
    {
        using var teacher = await fixture.CreateTeacherAsync();

        using var response = await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new Dictionary<string, string> { [field] = value });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty(field).GetString().ShouldBe(stored);
    }

    [Theory]
    [InlineData("timeZone", "Mars/Olympus")]
    [InlineData("timeZone", "UTC+1")]
    [InlineData("currency", "EURO")]
    [InlineData("currency", "ABC")]
    [InlineData("timeZone", "Central European Standard Time")]
    [InlineData("locale", "de")]
    [InlineData("name", "")]
    public async Task Invalid_settings_are_rejected(string field, string value)
    {
        using var teacher = await fixture.CreateTeacherAsync();

        using var response = await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new Dictionary<string, string> { [field] = value });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Another_teacher_cannot_read_or_change_the_workspace()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var path = $"/api/w/{owner.WorkspaceId}";

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Patch, path, new { name = "Hijacked" });

        (await owner.Client.GetJsonAsync<WorkspaceResponse>(path)).Name.ShouldNotBe("Hijacked");
        (await intruder.Client.GetJsonAsync<List<MyWorkspaceResponse>>("/api/me/workspaces"))
            .ShouldHaveSingleItem().Id.ShouldBe(intruder.WorkspaceId);
    }

    [Fact]
    public async Task Platform_admins_without_membership_cannot_open_workspaces()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var admin = await fixture.CreateTeacherAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var adminUser = await users.FindByIdAsync(admin.Me.Id.ToString());
            (await users.AddToRoleAsync(adminUser!, PlatformRoles.Admin)).Succeeded.ShouldBeTrue();
        }

        // Sign in again so the session carries the role.
        await admin.Client.RefreshAntiforgeryAsync();
        using (var relogin = await admin.Client.PostAsync("/api/auth/login", new { email = admin.Email, password = admin.Password }))
        {
            (await relogin.Content.ReadFromJsonAsync<MeResponse>(Ct))!.PlatformRoles.ShouldContain(PlatformRoles.Admin);
        }

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(admin.Client, HttpMethod.Get, $"/api/w/{owner.WorkspaceId}");
    }

    [Fact]
    public async Task Unknown_or_malformed_workspace_ids_are_not_found()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(teacher.Client, HttpMethod.Get, $"/api/w/{Guid.NewGuid()}");
        using var malformed = await teacher.Client.GetAsync("/api/w/not-a-guid");
        malformed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_for_workspace_endpoints()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var anonymous = await ApiClient.CreateAsync(fixture);

        using var response = await anonymous.GetAsync($"/api/w/{teacher.WorkspaceId}");
        using var list = await anonymous.GetAsync("/api/me/workspaces");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        list.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tenant_owned_rows_are_invisible_without_a_tenant_context()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        // A context without a tenant (for example code that runs outside a workspace request) sees nothing.
        await using var db = fixture.CreateDbContext();
        (await db.Set<WorkspaceMember>().AnyAsync(m => m.UserId == teacher.Me.Id, Ct)).ShouldBeFalse();
        (await db.Set<WorkspaceMember>().IgnoreQueryFilters([TenancyFilters.Tenant]).AnyAsync(m => m.UserId == teacher.Me.Id, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Registration_rolls_back_the_account_when_provisioning_fails()
    {
        using var failingFactory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWorkspaceProvisioning>();
                services.AddScoped<IWorkspaceProvisioning, FailingProvisioning>();
            }));
        using var http = failingFactory.CreateClient();
        using var token = await http.GetAsync(new Uri("/api/auth/antiforgery", UriKind.Relative), Ct);
        var email = ApiTestExtensions.UniqueEmail();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/register", UriKind.Relative))
        {
            Content = JsonContent.Create(new { email, password = TenantIsolationKit.Password }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token.Headers.GetValues("X-XSRF-TOKEN").Single());

        using var response = await http.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        await using var db = fixture.CreateDbContext();
        (await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct)).ShouldBeFalse();
        (await db.EmailOutbox.AnyAsync(m => m.ToAddress == email, Ct)).ShouldBeFalse();
    }

    private sealed class FailingProvisioning : IWorkspaceProvisioning
    {
        public Task<Guid> ProvisionForNewTeacherAsync(Guid userId, string locale, string timeZone, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provisioning unavailable (test).");
    }

    [Fact]
    public async Task Audit_entries_never_contain_secrets_or_emails()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        await using var db = fixture.CreateDbContext();

        var entries = await db.AuditLog.AsNoTracking().Where(a => a.WorkspaceId == teacher.WorkspaceId).ToListAsync(Ct);

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(e => e.DetailsJson == null || !e.DetailsJson.Contains(teacher.Email));
        entries.ShouldAllBe(e => e.DetailsJson == null || !e.DetailsJson.Contains(TenantIsolationKit.Password));
    }
}
