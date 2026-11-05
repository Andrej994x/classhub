using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Identity;

/// <summary>R1-P9: the authenticator app (TOTP), recovery codes and the two-step sign-in.</summary>
[Collection(ApiCollection.Name)]
public sealed class MfaTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_enrolls_the_authenticator_and_then_signs_in_in_two_steps()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        (await teacher.Client.GetJsonAsync<MfaStatusResponse>("/api/auth/mfa")).ShouldBe(new MfaStatusResponse(false, false, false, 0));

        AuthenticatorSetupResponse setup;
        using (var response = await teacher.Client.PostAsync("/api/auth/mfa/authenticator"))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            setup = (await response.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>(Ct))!;
        }

        setup.AuthenticatorUri.ShouldStartWith("otpauth://totp/ClassHub%3A");
        setup.AuthenticatorUri.ShouldContain("&issuer=ClassHub&digits=6");
        setup.AuthenticatorUri.ShouldContain($"secret={setup.SharedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant()}");

        using (var wrong = await teacher.Client.PostAsync("/api/auth/mfa/authenticator/enable", new { code = Totp.WrongCode(setup.SharedKey) }))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrong.ErrorCodeAsync()).ShouldBe(ErrorCodes.MfaCodeInvalid);
        }

        (await teacher.Client.GetJsonAsync<MfaStatusResponse>("/api/auth/mfa")).Enabled.ShouldBeFalse();

        RecoveryCodesResponse codes;
        using (var enable = await teacher.Client.PostAsync("/api/auth/mfa/authenticator/enable", new { code = Totp.Code(setup.SharedKey) }))
        {
            enable.StatusCode.ShouldBe(HttpStatusCode.OK);
            codes = (await enable.Content.ReadFromJsonAsync<RecoveryCodesResponse>(Ct))!;
        }

        codes.RecoveryCodes.Count.ShouldBe(10);
        codes.RecoveryCodes.Distinct().Count().ShouldBe(10);
        // The code proved the second factor: this session continues, now with amr=mfa.
        (await teacher.Client.GetJsonAsync<MfaStatusResponse>("/api/auth/mfa")).ShouldBe(new MfaStatusResponse(true, true, false, 10));

        await LogoutAsync(teacher.Client);
        await teacher.Client.RefreshAntiforgeryAsync();
        using (var password = await teacher.Client.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password }))
        {
            password.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            (await password.Content.ReadFromJsonAsync<TwoFactorRequiredResponse>(Ct))!.RequiresTwoFactor.ShouldBeTrue();
            password.Headers.TryGetValues("Set-Cookie", out var cookies);
            (cookies ?? []).ShouldNotContain(c => c.StartsWith(".ClassHub.Auth=", StringComparison.Ordinal) && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        }

        // Only the short-lived two-factor cookie exists: no session yet.
        (await teacher.Client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using (var wrong = await teacher.Client.PostAsync("/api/auth/login/two-factor", new { code = Totp.WrongCode(setup.SharedKey) }))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await wrong.ErrorCodeAsync()).ShouldBe(ErrorCodes.MfaCodeInvalid);
        }

        using (var second = await teacher.Client.PostAsync("/api/auth/login/two-factor", new { code = Totp.Code(setup.SharedKey) }))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await second.Content.ReadFromJsonAsync<MeResponse>(Ct))!.Id.ShouldBe(teacher.Me.Id);
        }

        (await teacher.Client.GetJsonAsync<MfaStatusResponse>("/api/auth/mfa")).SessionVerified.ShouldBeTrue();
        // The antiforgery token issued for the new identity works for unsafe requests.
        (await teacher.Client.PatchAsync("/api/auth/me", new { locale = "en" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_is_audited()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var key = await PlatformStaffKit.EnrollAuthenticatorAsync(teacher.Client);
        RecoveryCodesResponse codes;
        using (var regenerate = await teacher.Client.PostAsync("/api/auth/mfa/recovery-codes", new { code = Totp.Code(key) }))
        {
            regenerate.StatusCode.ShouldBe(HttpStatusCode.OK);
            codes = (await regenerate.Content.ReadFromJsonAsync<RecoveryCodesResponse>(Ct))!;
        }

        var recovery = codes.RecoveryCodes[0];
        await LogoutAsync(teacher.Client);
        await StartAsync(teacher);
        using (var signIn = await teacher.Client.PostAsync("/api/auth/login/two-factor", new { recoveryCode = recovery }))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await teacher.Client.GetJsonAsync<MfaStatusResponse>("/api/auth/mfa")).ShouldBe(new MfaStatusResponse(true, true, false, 9));

        await LogoutAsync(teacher.Client);
        await StartAsync(teacher);
        using (var reused = await teacher.Client.PostAsync("/api/auth/login/two-factor", new { recoveryCode = recovery }))
        {
            reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await reused.ErrorCodeAsync()).ShouldBe(ErrorCodes.MfaCodeInvalid);
        }

        await using var db = fixture.CreateDbContext();
        var key2 = teacher.Me.Id.ToString();
        (await db.Set<AuditLogEntry>().CountAsync(e => e.TargetId == key2 && e.Action == "account.mfa.recovery-code-used", Ct)).ShouldBe(1);
        (await db.Set<AuditLogEntry>().CountAsync(e => e.TargetId == key2 && e.Action == "account.mfa.enabled", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Regenerating_recovery_codes_needs_a_fresh_code_and_replaces_the_old_ones()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var key = await PlatformStaffKit.EnrollAuthenticatorAsync(teacher.Client);

        using (var missing = await teacher.Client.PostAsync("/api/auth/mfa/recovery-codes", new { }))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await missing.ErrorCodeAsync()).ShouldBe(ErrorCodes.ValidationFailed);
        }

        using (var wrong = await teacher.Client.PostAsync("/api/auth/mfa/recovery-codes", new { code = Totp.WrongCode(key) }))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrong.ErrorCodeAsync()).ShouldBe(ErrorCodes.MfaCodeInvalid);
        }

        var first = await RegenerateAsync(teacher.Client, key);
        var second = await RegenerateAsync(teacher.Client, key);
        second.ShouldNotContain(first[0]);

        await LogoutAsync(teacher.Client);
        await StartAsync(teacher);
        (await teacher.Client.PostAsync("/api/auth/login/two-factor", new { recoveryCode = first[0] })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await teacher.Client.PostAsync("/api/auth/login/two-factor", new { recoveryCode = second[0] })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_authenticator_codes_lock_the_account()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var key = await PlatformStaffKit.EnrollAuthenticatorAsync(teacher.Client);
        await LogoutAsync(teacher.Client);
        await StartAsync(teacher);

        for (var i = 0; i < 5; i++)
        {
            (await teacher.Client.PostAsync("/api/auth/login/two-factor", new { code = Totp.WrongCode(key) })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Locked out: even the right code (and the password step) answer like a wrong one.
        (await teacher.Client.PostAsync("/api/auth/login/two-factor", new { code = Totp.Code(key) })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await teacher.Client.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_second_step_needs_the_password_step_first()
    {
        using var client = await ApiClient.CreateAsync(fixture);

        using var response = await client.PostAsync("/api/auth/login/two-factor", new { code = "123456" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.SignInExpired);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"code":"123456","recoveryCode":"abcde-12345"}""")]
    public async Task The_second_step_takes_exactly_one_kind_of_code(string body)
    {
        using var client = await ApiClient.CreateAsync(fixture);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendContentAsync(HttpMethod.Post, "/api/auth/login/two-factor", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("code", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Authenticator_keys_and_recovery_codes_are_encrypted_at_rest()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var key = await PlatformStaffKit.EnrollAuthenticatorAsync(teacher.Client);
        var codes = await RegenerateAsync(teacher.Client, key);

        await using var db = fixture.CreateDbContext();
        Task<string> TokenAsync(string name) => db.Database
            .SqlQuery<string>($"SELECT Value AS [Value] FROM AspNetUserTokens WHERE UserId = {teacher.Me.Id} AND LoginProvider = '[AspNetUserStore]' AND Name = {name}")
            .SingleAsync(Ct);
        var rawKey = key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var authenticator = await TokenAsync("AuthenticatorKey");
        authenticator.ShouldNotContain(rawKey, Case.Insensitive);
        var recovery = await TokenAsync("RecoveryCodes");
        codes.ShouldAllBe(code => !recovery.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_suspended_account_cannot_finish_the_second_step()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var key = await PlatformStaffKit.EnrollAuthenticatorAsync(teacher.Client);
        await LogoutAsync(teacher.Client);
        await StartAsync(teacher);
        await fixture.WithServiceAsync<IUserAccountStatus, bool>(async s => { await s.SuspendAsync(teacher.Me.Id, "Test suspension", Ct); return true; });

        using var response = await teacher.Client.PostAsync("/api/auth/login/two-factor", new { code = Totp.Code(key) });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.AccountSuspended);
        (await teacher.Client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<IReadOnlyList<string>> RegenerateAsync(ApiClient client, string key)
    {
        using var response = await client.PostAsync("/api/auth/mfa/recovery-codes", new { code = Totp.Code(key) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>(Ct))!.RecoveryCodes;
    }

    private static async Task StartAsync(TeacherSession teacher)
    {
        await teacher.Client.RefreshAntiforgeryAsync();
        using var response = await teacher.Client.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private static async Task LogoutAsync(ApiClient client)
    {
        using var response = await client.PostAsync("/api/auth/logout");
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
