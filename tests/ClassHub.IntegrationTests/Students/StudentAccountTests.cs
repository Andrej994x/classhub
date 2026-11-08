using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Students;

/// <summary>R2-P3: invitations, account linking (D-16), the student:self policy and the minors safeguard (D-18).</summary>
[Collection(ApiCollection.Name)]
public sealed class StudentAccountTests(ClassHubApiFixture fixture)
{
    private const string Password = TenantIsolationKit.Password;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invited_student_creates_an_account_and_sees_only_their_own_record()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var email = ApiTestExtensions.UniqueEmail("student");
        var ana = await StudentAsync(teacher, "Ана", email);
        await StudentAsync(teacher, "Борис", ApiTestExtensions.UniqueEmail("student"));

        var token = await InviteForTokenAsync(teacher, ana.Id, email);
        var invited = await teacher.Client.GetJsonAsync<StudentResponse>($"/api/w/{teacher.WorkspaceId}/students/{ana.Id}");
        invited.Invitation.ShouldNotBeNull().Email.ShouldBe(email);
        invited.AccountLinked.ShouldBeFalse();

        using var visitor = await ApiClient.CreateAsync(fixture);
        var preview = await ReadAsync<InvitationPreviewResponse>(await visitor.PostAsync("/api/student-invitations/preview", new { token }));
        preview.StudentFirstName.ShouldBe("Ана");
        preview.Email.ShouldBe(email);

        using (var accept = await visitor.PostAsync("/api/student-invitations/accept-with-account", new { token, password = Password, locale = "en" }))
        {
            accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync(Ct));
            (await accept.Content.ReadFromJsonAsync<StudentTeacherResponse>(Ct))!.StudentId.ShouldBe(ana.Id);
        }

        // Signed in as the new student account: a confirmed email, no teacher workspace.
        var me = await visitor.GetJsonAsync<MeResponse>("/api/auth/me");
        me.Email.ShouldBe(email);
        me.EmailConfirmed.ShouldBeTrue();
        (await visitor.GetJsonAsync<List<object>>("/api/me/workspaces")).ShouldBeEmpty();

        var teachers = await visitor.GetJsonAsync<List<StudentTeacherResponse>>("/api/student/teachers");
        teachers.ShouldHaveSingleItem().WorkspaceId.ShouldBe(teacher.WorkspaceId);
        var profile = await visitor.GetJsonAsync<StudentSelfResponse>($"/api/student/w/{teacher.WorkspaceId}/profile");
        profile.StudentId.ShouldBe(ana.Id);
        profile.FirstName.ShouldBe("Ана");

        // The student is not a member of the teacher's workspace: every teacher endpoint is invisible.
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(visitor, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/students");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(visitor, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/students/{ana.Id}");

        // The teacher sees that the account exists, never its credentials; the link works once.
        var linked = await teacher.Client.GetJsonAsync<StudentResponse>($"/api/w/{teacher.WorkspaceId}/students/{ana.Id}");
        linked.AccountLinked.ShouldBeTrue();
        linked.Invitation.ShouldBeNull();
        using var again = await ApiClient.CreateAsync(fixture);
        using var reuse = await again.PostAsync("/api/student-invitations/accept-with-account", new { token, password = Password });
        reuse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reuse.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task An_existing_account_links_to_students_of_several_teachers()
    {
        using var first = await fixture.CreateTeacherAsync();
        using var second = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync(); // any existing account, here one that teaches too
        var atFirst = await StudentAsync(first, "Марија", learner.Email);
        var atSecond = await StudentAsync(second, "Марија", learner.Email);

        await AcceptAsync(learner.Client, await InviteForTokenAsync(first, atFirst.Id, learner.Email));
        await AcceptAsync(learner.Client, await InviteForTokenAsync(second, atSecond.Id, learner.Email));

        var teachers = await learner.Client.GetJsonAsync<List<StudentTeacherResponse>>("/api/student/teachers");
        teachers.Select(t => t.WorkspaceId).ShouldBe([first.WorkspaceId, second.WorkspaceId], ignoreOrder: true);
        (await learner.Client.GetJsonAsync<StudentSelfResponse>($"/api/student/w/{second.WorkspaceId}/profile")).StudentId.ShouldBe(atSecond.Id);
        // Still the teacher of their own workspace.
        (await learner.Client.GetAsync($"/api/w/{learner.WorkspaceId}/students")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task One_account_cannot_be_two_students_of_the_same_teacher()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var one = await StudentAsync(teacher, "Прв", learner.Email);
        var other = await StudentAsync(teacher, "Втор", ApiTestExtensions.UniqueEmail("student"));
        await AcceptAsync(learner.Client, await InviteForTokenAsync(teacher, one.Id, learner.Email));

        var otherEmail = ApiTestExtensions.UniqueEmail("other");
        var otherToken = await InviteForTokenAsync(teacher, other.Id, otherEmail, otherEmail);
        using var accept = await learner.Client.PostAsync("/api/student-invitations/accept", new { token = otherToken });

        accept.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await accept.ErrorCodeAsync()).ShouldBe(ErrorCodes.AccountAlreadyLinked);
        (await teacher.Client.GetJsonAsync<StudentResponse>($"/api/w/{teacher.WorkspaceId}/students/{other.Id}")).AccountLinked.ShouldBeFalse();
    }

    [Fact]
    public async Task Students_without_a_record_in_the_workspace_get_not_found()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var record = await StudentAsync(teacher, "Ана", learner.Email);
        await AcceptAsync(learner.Client, await InviteForTokenAsync(teacher, record.Id, learner.Email));

        // Another teacher's workspace, and a teacher who is not a student anywhere.
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(learner.Client, HttpMethod.Get, $"/api/student/w/{other.WorkspaceId}/profile");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(other.Client, HttpMethod.Get, $"/api/student/w/{teacher.WorkspaceId}/profile");
        // Even the workspace's own teacher has no student record there.
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(teacher.Client, HttpMethod.Get, $"/api/student/w/{teacher.WorkspaceId}/profile");
        (await other.Client.GetJsonAsync<List<StudentTeacherResponse>>("/api/student/teachers")).ShouldBeEmpty();
        using var anonymous = await ApiClient.CreateAsync(fixture);
        (await anonymous.GetAsync($"/api/student/w/{teacher.WorkspaceId}/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Archiving_or_unlinking_ends_portal_access_and_voids_pending_links()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var linked = await StudentAsync(teacher, "Ана", learner.Email);
        await AcceptAsync(learner.Client, await InviteForTokenAsync(teacher, linked.Id, learner.Email));

        var unlinked = await teacher.Client.DeleteAsync($"/api/w/{teacher.WorkspaceId}/students/{linked.Id}/account");
        unlinked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await unlinked.Content.ReadFromJsonAsync<StudentResponse>(Ct))!.AccountLinked.ShouldBeFalse();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(learner.Client, HttpMethod.Get, $"/api/student/w/{teacher.WorkspaceId}/profile");

        // A pending link dies with the archive.
        var pendingEmail = ApiTestExtensions.UniqueEmail("student");
        var pending = await StudentAsync(teacher, "Борис", pendingEmail);
        var token = await InviteForTokenAsync(teacher, pending.Id, pendingEmail);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{pending.Id}/archive", new { version = pending.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var visitor = await ApiClient.CreateAsync(fixture);
        using var accept = await visitor.PostAsync("/api/student-invitations/accept-with-account", new { token, password = Password });
        (await accept.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task A_new_invitation_replaces_the_old_one_and_expired_links_fail()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var email = ApiTestExtensions.UniqueEmail("student");
        var student = await StudentAsync(teacher, "Ана", email);
        var old = await InviteForTokenAsync(teacher, student.Id, email);
        var current = await InviteForTokenAsync(teacher, student.Id, email);
        current.ShouldNotBe(old);

        using var visitor = await ApiClient.CreateAsync(fixture);
        (await (await visitor.PostAsync("/api/student-invitations/preview", new { token = old })).ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
        (await visitor.PostAsync("/api/student-invitations/preview", new { token = current })).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE StudentInvitations SET ExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE StudentId = {student.Id}", Ct);
        }

        (await (await visitor.PostAsync("/api/student-invitations/preview", new { token = current })).ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
        (await teacher.Client.GetJsonAsync<StudentResponse>($"/api/w/{teacher.WorkspaceId}/students/{student.Id}")).Invitation.ShouldBeNull();
    }

    [Fact]
    public async Task A_minor_needs_the_guardian_agreement_and_it_is_recorded()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var email = ApiTestExtensions.UniqueEmail("guardian");
        var student = await StudentAsync(teacher, "Мила", email, isMinor: true);
        var token = await InviteForTokenAsync(teacher, student.Id, email);
        using var visitor = await ApiClient.CreateAsync(fixture);

        using (var refused = await visitor.PostAsync("/api/student-invitations/accept-with-account", new { token, password = Password }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.ReadJsonAsync()).GetProperty("errors").TryGetProperty("guardianConsent", out _).ShouldBeTrue();
        }

        (await visitor.PostAsync("/api/student-invitations/accept-with-account", new { token, password = Password, guardianConsent = true })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM StudentInvitations WHERE StudentId = {student.Id} AND GuardianConsentAtUtc IS NOT NULL").SingleAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task An_existing_email_must_sign_in_to_accept()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var student = await StudentAsync(teacher, "Ана", learner.Email);
        var token = await InviteForTokenAsync(teacher, student.Id, learner.Email);
        using var visitor = await ApiClient.CreateAsync(fixture);

        using var accept = await visitor.PostAsync("/api/student-invitations/accept-with-account", new { token, password = Password });

        accept.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await accept.ErrorCodeAsync()).ShouldBe(ErrorCodes.EmailRegistered);
        (await teacher.Client.GetJsonAsync<StudentResponse>($"/api/w/{teacher.WorkspaceId}/students/{student.Id}")).AccountLinked.ShouldBeFalse();
    }

    [Fact]
    public async Task Two_accounts_accepting_one_invitation_at_once_link_exactly_one()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var email = ApiTestExtensions.UniqueEmail("student");
        var student = await StudentAsync(teacher, "Ана", email);
        var token = await InviteForTokenAsync(teacher, student.Id, email);
        var learners = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => fixture.CreateTeacherAsync()));

        var responses = await Task.WhenAll(learners.Select(l => l.Client.PostAsync("/api/student-invitations/accept", new { token })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        await using (var db = fixture.CreateDbContext())
        {
            (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Students WHERE Id = {student.Id} AND UserId IS NOT NULL").SingleAsync(Ct)).ShouldBe(1);
        }

        foreach (var item in responses.Cast<IDisposable>().Concat(learners))
        {
            item.Dispose();
        }
    }

    [Fact]
    public async Task Teachers_cannot_invite_another_teachers_student()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var student = await StudentAsync(owner, "Ана", ApiTestExtensions.UniqueEmail("student"));

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, $"/api/w/{owner.WorkspaceId}/students/{student.Id}/invitation", new { });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Delete, $"/api/w/{owner.WorkspaceId}/students/{student.Id}/account");
        (await intruder.Client.PostAsync($"/api/w/{intruder.WorkspaceId}/students/{student.Id}/invitation", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Inviting_needs_an_email_and_is_limited_per_day()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var noEmail = await StudentAsync(teacher, "Без", null);
        using (var missing = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{noEmail.Id}/invitation", new { }))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await missing.ReadJsonAsync()).GetProperty("errors").TryGetProperty("email", out _).ShouldBeTrue();
        }

        for (var i = 0; i < 5; i++)
        {
            await InviteAsync(teacher, noEmail.Id, ApiTestExtensions.UniqueEmail("try"));
        }

        using var limited = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{noEmail.Id}/invitation", new { email = ApiTestExtensions.UniqueEmail("try") });
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    /// <summary>Invites and returns the token from the email sent by this invitation (not an earlier one).</summary>
    private async Task<string> InviteForTokenAsync(TeacherSession teacher, Guid studentId, string address, string? email = null)
    {
        var since = DateTime.UtcNow.AddMilliseconds(-5);
        await InviteAsync(teacher, studentId, email);
        return (await fixture.WaitForEmailAsync(address, "student.invitation", since)).FragmentValue("token");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName, string? email, bool isMinor = false)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName, email, isMinor });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    private static async Task<StudentResponse> InviteAsync(TeacherSession teacher, Guid studentId, string? email = null) =>
        await ReadAsync<StudentResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{studentId}/invitation", new { email }));

    private static async Task AcceptAsync(ApiClient client, string token)
    {
        using var response = await client.PostAsync("/api/student-invitations/accept", new { token });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }
}
