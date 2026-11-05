using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;

namespace ClassHub.IntegrationTests.Infrastructure;

/// <summary>A signed-in teacher with their workspace.</summary>
public sealed record TeacherSession(ApiClient Client, MeResponse Me, Guid WorkspaceId, string Email, string Password) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

/// <summary>
/// Shared helpers for tenant isolation tests: every phase that adds tenant-owned resources uses
/// <see cref="AssertCrossTenantDeniedAsync"/> to prove another teacher gets 404 and changes nothing.
/// </summary>
public static class TenantIsolationKit
{
    public const string Password = "Correct-Horse-Battery-7";

    /// <summary>Registers a new teacher, signs in and returns the session with the provisioned workspace.</summary>
    public static async Task<TeacherSession> CreateTeacherAsync(this ClassHubApiFixture fixture, string locale = "mk", bool confirmEmail = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();

        using (var register = await client.PostAsync("/api/auth/register", new { email, password = Password, locale }))
        {
            register.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var login = await client.PostAsync("/api/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (await login.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken))!;

        var workspaces = await client.GetJsonAsync<List<MyWorkspaceResponse>>("/api/me/workspaces");
        var session = new TeacherSession(client, me, workspaces.ShouldHaveSingleItem().Id, email, Password);
        if (confirmEmail)
        {
            await fixture.SetEmailConfirmedAsync(session, true);
        }

        return session;
    }

    /// <summary>Arranges the account's email confirmation (D-10b: publishing and public leads need a confirmed email).</summary>
    public static async Task SetEmailConfirmedAsync(this ClassHubApiFixture fixture, TeacherSession teacher, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(teacher);
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"UPDATE AspNetUsers SET EmailConfirmed = {confirmed} WHERE Id = {teacher.Me.Id}", TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that <paramref name="intruder"/> cannot see or change another workspace's resource: the API
    /// answers 404 with code NOT_FOUND (never 403, so existence is not revealed).
    /// </summary>
    public static async Task AssertCrossTenantDeniedAsync(ApiClient intruder, HttpMethod method, string path, object? body = null)
    {
        ArgumentNullException.ThrowIfNull(intruder);
        using var response = await intruder.SendAsync(method, path, body, withAntiforgery: true);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {path} must be invisible to another tenant");
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.NotFound);
    }
}
