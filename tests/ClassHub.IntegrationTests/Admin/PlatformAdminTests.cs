using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClassHub.Core.Modules.Admin.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;

namespace ClassHub.IntegrationTests.Admin;

/// <summary>R1-P9: the admin API needs a platform role with MFA; owner vs admin rights; suspension; audit.</summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformAdminTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_endpoints_need_a_platform_role()
    {
        using var anonymous = await ApiClient.CreateAsync(fixture);
        using var teacher = await fixture.CreateTeacherAsync();

        (await anonymous.GetAsync("/api/admin/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var forbidden = await teacher.Client.GetAsync("/api/admin/users");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.ErrorCodeAsync()).ShouldBe(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Platform_staff_need_a_session_signed_in_with_the_authenticator()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin, enrollAuthenticator: false);

        using var response = await admin.Client.GetAsync("/api/admin/overview");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.MfaRequired);
        (await admin.Client.GetJsonAsync<MfaStatusResponse>("/api/auth/mfa")).ShouldBe(new MfaStatusResponse(false, false, true, 0));

        // Enrolling proves the second factor: the same session gets in.
        await PlatformStaffKit.EnrollAuthenticatorAsync(admin.Client);
        (await admin.Client.GetAsync("/api/admin/overview")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_enrolled_owner_signs_in_with_the_code_and_keeps_access_across_session_validations()
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        await LogoutAsync(owner.Client);

        using (var signIn = await PlatformStaffKit.SignInWithAuthenticatorAsync(owner.Client, owner.Email, owner.Password, owner.Code(), rememberMe: true))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
            // Platform staff never get a persistent cookie, even with "remember me".
            signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".ClassHub.Auth=", StringComparison.Ordinal))
                .ShouldNotContain("expires=", Case.Insensitive);
        }

        // The security stamp is validated on every request in tests; the amr claim must survive each refresh.
        for (var i = 0; i < 3; i++)
        {
            (await owner.Client.GetAsync("/api/admin/overview")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_overview_counts_users_teachers_registrations_and_published_sites()
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        var before = await owner.Client.GetJsonAsync<AdminOverviewResponse>("/api/admin/overview");

        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        await PublishSiteAsync(teacher);
        using var suspended = await fixture.CreateTeacherAsync();
        await SuspendAsync(owner, suspended.Me.Id);

        var after = await owner.Client.GetJsonAsync<AdminOverviewResponse>("/api/admin/overview");
        after.Users.ShouldBe(before.Users + 2);
        after.SuspendedUsers.ShouldBe(before.SuspendedUsers + 1);
        after.Teachers.ShouldBe(before.Teachers + 2);
        after.ActiveTeachers.ShouldBe(before.ActiveTeachers + 1); // signed in, not suspended
        after.NewUsersLast7Days.ShouldBe(before.NewUsersLast7Days + 2);
        after.NewUsersLast30Days.ShouldBe(before.NewUsersLast30Days + 2);
        after.PublishedSites.ShouldBe(before.PublishedSites + 1);
        after.PlatformStaff.ShouldBe(before.PlatformStaff);
        after.PlatformStaff.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Search_and_detail_show_account_metadata_only()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var slug = await PublishSiteAsync(teacher);

        var page = await admin.Client.GetJsonAsync<AdminUserPage>($"/api/admin/users?q={Uri.EscapeDataString(teacher.Email[..20])}");
        var found = page.Items.ShouldHaveSingleItem();
        page.Total.ShouldBe(1);
        found.Id.ShouldBe(teacher.Me.Id);
        found.Status.ShouldBe("Active");
        found.PlatformRoles.ShouldBeEmpty();
        found.Workspace.ShouldNotBeNull();
        found.Workspace.Id.ShouldBe(teacher.WorkspaceId);
        found.Workspace.SiteSlug.ShouldBe(slug);
        found.Workspace.SitePublished.ShouldBeTrue();

        (await admin.Client.GetJsonAsync<AdminUserPage>($"/api/admin/users?q={teacher.Me.Id}")).Items.ShouldHaveSingleItem().Id.ShouldBe(teacher.Me.Id);
        (await admin.Client.GetJsonAsync<AdminUserPage>("/api/admin/users?staff=true")).Items.ShouldContain(u => u.Id == admin.Me.Id);
        (await admin.Client.GetJsonAsync<AdminUserPage>("/api/admin/users?staff=true")).Items.ShouldNotContain(u => u.Id == teacher.Me.Id);
        (await admin.Client.GetAsync("/api/admin/users?status=Deleted")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.GetAsync("/api/admin/users?page=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var detail = await admin.Client.GetAsync($"/api/admin/users/{teacher.Me.Id}");
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await detail.ReadJsonAsync();
        json.EnumerateObject().Select(p => p.Name).ShouldBe(["user", "lockedOutUntilUtc", "history"], ignoreOrder: true);
        json.GetProperty("user").EnumerateObject().Select(p => p.Name).ShouldBe(
            ["id", "email", "emailConfirmed", "status", "platformRoles", "mfaEnabled", "createdAtUtc", "lastLoginAtUtc", "workspace"], ignoreOrder: true);
        (await admin.Client.GetAsync($"/api/admin/users/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Suspension_ends_sessions_blocks_sign_in_hides_the_site_and_is_audited()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var slug = await PublishSiteAsync(teacher);
        using var visitor = fixture.Factory.CreateClient();
        (await visitor.GetAsync($"/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var missing = await admin.Client.PostAsync($"/api/admin/users/{teacher.Me.Id}/suspend", new { reason = " " }))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await missing.ReadJsonAsync()).GetProperty("errors").TryGetProperty("reason", out _).ShouldBeTrue();
        }

        var detail = await SuspendAsync(admin, teacher.Me.Id, "Spam on the public page");
        detail.User.Status.ShouldBe("Suspended");
        var entry = detail.History.ShouldHaveSingleItem();
        entry.Action.ShouldBe("user.suspended");
        entry.ActorUserId.ShouldBe(admin.Me.Id);
        entry.ActorEmail.ShouldBe(admin.Email);
        entry.DetailsJson.ShouldNotBeNull();
        entry.DetailsJson.ShouldContain("Spam on the public page");

        (await teacher.Client.GetAsync("/api/me/workspaces")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await teacher.Client.RefreshAntiforgeryAsync();
        using (var login = await teacher.Client.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password }))
        {
            login.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await login.ErrorCodeAsync()).ShouldBe(ErrorCodes.AccountSuspended);
        }

        (await visitor.GetAsync($"/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using (var twice = await admin.Client.PostAsync($"/api/admin/users/{teacher.Me.Id}/suspend", new { reason = "Again" }))
        {
            twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await twice.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        using (var reactivate = await admin.Client.PostAsync($"/api/admin/users/{teacher.Me.Id}/reactivate", new { reason = "Content removed" }))
        {
            reactivate.StatusCode.ShouldBe(HttpStatusCode.OK);
            var after = (await reactivate.Content.ReadFromJsonAsync<AdminUserDetail>(Ct))!;
            after.User.Status.ShouldBe("Active");
            after.History.Select(h => h.Action).ShouldBe(["user.reactivated", "user.suspended"]);
        }

        (await visitor.GetAsync($"/{slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var again = await teacher.Client.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Admins_manage_teachers_but_not_themselves_other_admins_or_owners()
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var otherAdmin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);

        await AssertTargetForbiddenAsync(admin, admin.Me.Id);
        await AssertTargetForbiddenAsync(admin, otherAdmin.Me.Id);
        await AssertTargetForbiddenAsync(admin, owner.Me.Id);
        await AssertTargetForbiddenAsync(owner, owner.Me.Id);

        // The owner may suspend an admin; the admin's session ends and the admin console closes.
        (await SuspendAsync(owner, otherAdmin.Me.Id)).User.Status.ShouldBe("Suspended");
        (await otherAdmin.Client.GetAsync("/api/admin/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_the_owner_grants_and_revokes_platform_admin_with_a_fresh_code()
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        var grant = $"/api/admin/users/{teacher.Me.Id}/admin-role/grant";

        using (var byAdmin = await admin.Client.PostAsync(grant, new { code = admin.Code() }))
        {
            byAdmin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await byAdmin.ErrorCodeAsync()).ShouldBe(ErrorCodes.Forbidden);
        }

        (await owner.Client.PostAsync(grant, new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var wrong = await owner.Client.PostAsync(grant, new { code = Totp.WrongCode(owner.AuthenticatorKey!) }))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrong.ErrorCodeAsync()).ShouldBe(ErrorCodes.MfaCodeInvalid);
        }

        using (var granted = await owner.Client.PostAsync(grant, new { code = owner.Code() }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.OK);
            var detail = (await granted.Content.ReadFromJsonAsync<AdminUserDetail>(Ct))!;
            detail.User.PlatformRoles.ShouldBe([PlatformRoles.Admin]);
            var entry = detail.History.ShouldHaveSingleItem();
            entry.Action.ShouldBe("platform.role.granted");
            entry.ActorUserId.ShouldBe(owner.Me.Id);
        }

        // The role change ended the teacher's session; the new role applies at the next sign-in.
        (await teacher.Client.GetAsync("/api/me/workspaces")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await owner.Client.PostAsync(grant, new { code = owner.Code() })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using (var revoked = await owner.Client.PostAsync($"/api/admin/users/{teacher.Me.Id}/admin-role/revoke", new { code = owner.Code() }))
        {
            revoked.StatusCode.ShouldBe(HttpStatusCode.OK);
            var detail = (await revoked.Content.ReadFromJsonAsync<AdminUserDetail>(Ct))!;
            detail.User.PlatformRoles.ShouldBeEmpty();
            detail.History.Select(h => h.Action).ShouldBe(["platform.role.revoked", "platform.role.granted"]);
        }

        using (var self = await owner.Client.PostAsync($"/api/admin/users/{owner.Me.Id}/admin-role/grant", new { code = owner.Code() }))
        {
            self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await self.ErrorCodeAsync()).ShouldBe(ErrorCodes.AdminTargetForbidden);
        }
    }

    [Fact]
    public async Task Platform_staff_with_mfa_still_cannot_open_a_teachers_workspace()
    {
        using var owner = await fixture.CreatePlatformStaffAsync(PlatformRoles.Owner);
        using var teacher = await fixture.CreateTeacherAsync();

        foreach (var path in new[] { $"/api/w/{teacher.WorkspaceId}", $"/api/w/{teacher.WorkspaceId}/leads", $"/api/w/{teacher.WorkspaceId}/profile", $"/api/w/{teacher.WorkspaceId}/dashboard" })
        {
            await TenantIsolationKit.AssertCrossTenantDeniedAsync(owner.Client, HttpMethod.Get, path);
        }
    }

    [Fact]
    public async Task The_audit_viewer_filters_and_hides_workspace_details()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        using (var rename = await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new { name = "Private workspace name" }))
        {
            rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await SuspendAsync(admin, teacher.Me.Id, "Audit filter check");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var byUser = await admin.Client.GetJsonAsync<AdminAuditPage>($"/api/admin/audit?userId={teacher.Me.Id}&action=user.&from={today}&to={today}");
        var suspension = byUser.Items.ShouldHaveSingleItem();
        suspension.Action.ShouldBe("user.suspended");
        suspension.DetailsJson!.ShouldContain("Audit filter check");

        var byWorkspace = await admin.Client.GetJsonAsync<AdminAuditPage>($"/api/admin/audit?workspaceId={teacher.WorkspaceId}");
        byWorkspace.Items.Select(i => i.Action).ShouldBe(["workspace.settings.updated", "workspace.created"]);
        byWorkspace.Items.ShouldAllBe(i => i.DetailsJson == null);
        JsonSerializer.Serialize(byWorkspace).ShouldNotContain("Private workspace name");

        (await admin.Client.GetJsonAsync<AdminAuditPage>("/api/admin/audit?from=2000-01-01&to=2000-01-02")).Total.ShouldBe(0);
        (await admin.Client.GetAsync("/api/admin/audit?from=2026-02-02&to=2026-02-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task AssertTargetForbiddenAsync(StaffSession actor, Guid target)
    {
        using var response = await actor.Client.PostAsync($"/api/admin/users/{target}/suspend", new { reason = "Not allowed" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.AdminTargetForbidden);
    }

    private static async Task<AdminUserDetail> SuspendAsync(StaffSession actor, Guid userId, string reason = "Test suspension")
    {
        using var response = await actor.Client.PostAsync($"/api/admin/users/{userId}/suspend", new { reason });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<AdminUserDetail>(Ct))!;
    }

    private static async Task<string> PublishSiteAsync(TeacherSession teacher)
    {
        var slug = $"adm-{Guid.NewGuid():N}"[..20];
        var path = $"/api/w/{teacher.WorkspaceId}/site";
        int version;
        using (var save = await teacher.Client.PutAsync(path, new { slug, template = "classic", palette = "ocean", language = "mk", draft = new { sections = SiteService.DefaultContent("mk").Sections } }))
        {
            save.StatusCode.ShouldBe(HttpStatusCode.OK, await save.Content.ReadAsStringAsync(Ct));
            version = (await save.ReadJsonAsync()).GetProperty("version").GetInt32();
        }

        using (var publish = await teacher.Client.PostAsync($"{path}/publish", new { version }))
        {
            publish.StatusCode.ShouldBe(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync(Ct));
        }

        return slug;
    }

    private static async Task LogoutAsync(ApiClient client)
    {
        using var response = await client.PostAsync("/api/auth/logout");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
