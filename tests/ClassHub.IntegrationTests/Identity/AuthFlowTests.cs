using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Modules.Identity;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClassHub.IntegrationTests.Identity;

/// <summary>R0-P4: registration, email confirmation, sign-in/out, password reset, lockout and suspension.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthFlowTests(ClassHubApiFixture fixture)
{
    private const string Password = "Correct-Horse-Battery-7";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Teacher_registers_confirms_email_and_signs_in()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        var since = DateTime.UtcNow.AddSeconds(-1);

        using var register = await client.PostAsync("/api/auth/register", new { email, password = Password, locale = "en" });
        register.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var mail = await fixture.WaitForEmailAsync(email, "account.confirm-email", since);
        mail.Subject.ShouldBe("Confirm your email for ClassHub");
        mail.TextBody.ShouldContain("http://classhub.test/verify-email#");

        using var confirm = await client.PostAsync("/api/auth/confirm-email", new
        {
            userId = Guid.Parse(mail.FragmentValue("userId")),
            token = mail.FragmentValue("token"),
        });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var login = await client.PostAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await login.Content.ReadFromJsonAsync<MeResponse>(Ct);
        me!.Email.ShouldBe(email);
        me.EmailConfirmed.ShouldBeTrue();
        me.Locale.ShouldBe("en");
        me.TimeZone.ShouldBe("Europe/Skopje");

        (await client.GetJsonAsync<MeResponse>("/api/auth/me")).Id.ShouldBe(me.Id);
    }

    [Fact]
    public async Task Confirmation_links_cannot_be_reused_or_forged_after_confirmation()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        var since = DateTime.UtcNow.AddSeconds(-1);
        using (var _ = await client.PostAsync("/api/auth/register", new { email, password = Password })) { }
        var mail = await fixture.WaitForEmailAsync(email, "account.confirm-email", since);
        var userId = Guid.Parse(mail.FragmentValue("userId"));
        var token = mail.FragmentValue("token");

        using (var first = await client.PostAsync("/api/auth/confirm-email", new { userId, token }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var reused = await client.PostAsync("/api/auth/confirm-email", new { userId, token });
        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reused.ErrorCodeAsync()).ShouldBe(ErrorCodes.EmailAlreadyConfirmed);

        using var forged = await client.PostAsync("/api/auth/confirm-email", new { userId, token = "forged" });
        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await forged.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task Registration_is_atomic_when_the_confirmation_email_cannot_be_queued()
    {
        using var failingFactory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailOutbox>();
                services.AddScoped<IEmailOutbox, FailingOutbox>();
            }));
        using var http = failingFactory.CreateClient();
        using var tokenResponse = await http.GetAsync(new Uri("/api/auth/antiforgery", UriKind.Relative), Ct);
        var xsrf = tokenResponse.Headers.GetValues("X-XSRF-TOKEN").Single();
        var email = ApiTestExtensions.UniqueEmail();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/register", UriKind.Relative))
        {
            Content = JsonContent.Create(new { email, password = Password }),
        };
        request.Headers.Add("X-XSRF-TOKEN", xsrf);
        using var response = await http.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        await using var db = fixture.CreateDbContext();
        (await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct)).ShouldBeFalse();
    }

    private sealed class FailingOutbox : IEmailOutbox
    {
        public void Enqueue(EmailMessage message) => throw new InvalidOperationException("Outbox unavailable (test).");
    }

    [Fact]
    public async Task Macedonian_is_the_default_email_language()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        var since = DateTime.UtcNow.AddSeconds(-1);

        using var _ = await client.PostAsync("/api/auth/register", new { email, password = Password });

        (await fixture.WaitForEmailAsync(email, "account.confirm-email", since)).Subject.ShouldBe("Потврдете ја вашата е-пошта за ClassHub");
    }

    [Fact]
    public async Task Registering_an_existing_email_answers_the_same_and_creates_no_second_account()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);
        var since = DateTime.UtcNow.AddSeconds(-1);

        using var again = await client.PostAsync("/api/auth/register", new { email, password = "Another-Password-99" });

        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await again.ReadJsonAsync()).GetProperty("message").GetString().ShouldBe(
            (await (await client.PostAsync("/api/auth/register", new { email = ApiTestExtensions.UniqueEmail(), password = Password })).ReadJsonAsync())
                .GetProperty("message").GetString());
        await fixture.WaitForEmailAsync(email, "account.already-registered", since);
        await using var db = fixture.CreateDbContext();
        (await db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_registrations_with_the_same_email_create_exactly_one_account()
    {
        var email = ApiTestExtensions.UniqueEmail();
        var clients = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ApiClient.CreateAsync(fixture)));
        try
        {
            var responses = await Task.WhenAll(clients.Select(c => c.PostAsync("/api/auth/register", new { email, password = Password })));
            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }
        finally
        {
            foreach (var c in clients)
            {
                c.Dispose();
            }
        }

        await using var db = fixture.CreateDbContext();
        (await db.Users.CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct)).ShouldBe(1);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("1234567890")]
    [InlineData("aaaaaaaaaaaa")]
    public async Task Weak_passwords_are_rejected_for_new_and_existing_emails_alike(string weak)
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var existing = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, existing);

        foreach (var email in new[] { ApiTestExtensions.UniqueEmail(), existing })
        {
            using var response = await client.PostAsync("/api/auth/register", new { email, password = weak });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.ReadJsonAsync();
            problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
            problem.GetProperty("errors").TryGetProperty("password", out _).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Password_containing_the_email_name_is_rejected()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = $"marija{Guid.NewGuid():N}"[..14] + "@example.test";

        using var response = await client.PostAsync("/api/auth/register", new { email, password = email.Split('@')[0] + "2026" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Invalid_emails_are_rejected(string email)
    {
        using var client = await ApiClient.CreateAsync(fixture);

        using var response = await client.PostAsync("/api/auth/register", new { email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("email", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Unsafe_auth_requests_require_the_antiforgery_header()
    {
        using var client = await ApiClient.CreateAsync(fixture);

        using var response = await client.PostAsync("/api/auth/login", new { email = "x@example.test", password = Password }, withAntiforgery: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.AntiforgeryInvalid);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_401()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);

        using var wrong = await client.PostAsync("/api/auth/login", new { email, password = "Wrong-Password-123" });
        using var unknown = await client.PostAsync("/api/auth/login", new { email = ApiTestExtensions.UniqueEmail(), password = Password });

        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var a = await wrong.ReadJsonAsync();
        var b = await unknown.ReadJsonAsync();
        a.GetProperty("code").GetString().ShouldBe(ErrorCodes.InvalidCredentials);
        b.GetProperty("code").GetString().ShouldBe(ErrorCodes.InvalidCredentials);
        a.GetProperty("title").GetString().ShouldBe(b.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account_even_for_the_right_password()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);

        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.PostAsync("/api/auth/login", new { email, password = "Wrong-Password-123" });
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var locked = await client.PostAsync("/api/auth/login", new { email, password = Password });
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await locked.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Session_cookie_is_http_only_lax_and_scoped_to_the_site()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);

        using var login = await client.PostAsync("/api/auth/login", new { email, password = Password, rememberMe = true });

        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".ClassHub.Auth=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("expires=", Case.Insensitive); // persistent because rememberMe
    }

    [Fact]
    public async Task Without_remember_me_the_cookie_is_a_session_cookie()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);

        using var login = await client.PostAsync("/api/auth/login", new { email, password = Password });

        login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".ClassHub.Auth=", StringComparison.Ordinal))
            .ShouldNotContain("expires=", Case.Insensitive);
    }

    [Fact]
    public async Task Me_requires_a_session_and_logout_ends_it()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();

        using (var anonymous = await client.GetAsync("/api/auth/me"))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await RegisterAsync(client, email);
        await LoginAsync(client, email, Password);

        // The token issued at sign-in belongs to the signed-in user and is accepted for logout.
        using (var logout = await client.PostAsync("/api/auth/logout"))
        {
            logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var after = await client.GetAsync("/api/auth/me");
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_callers_get_404_for_unknown_api_routes()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);
        await LoginAsync(client, email, Password);

        using var response = await client.GetAsync("/api/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Password_reset_changes_the_password_ends_sessions_and_the_link_works_once()
    {
        using var session = await ApiClient.CreateAsync(fixture);
        using var other = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(session, email);
        await LoginAsync(session, email, Password);
        var since = DateTime.UtcNow.AddSeconds(-1);

        using (var forgot = await other.PostAsync("/api/auth/forgot-password", new { email }))
        {
            forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var mail = await fixture.WaitForEmailAsync(email, "account.reset-password", since);
        var token = mail.FragmentValue("token");
        mail.FragmentValue("email").ShouldBe(email);
        const string NewPassword = "Brand-New-Secret-42";

        using (var reset = await other.PostAsync("/api/auth/reset-password", new { email, token, newPassword = NewPassword }))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var oldSession = await session.GetAsync("/api/auth/me"))
        {
            oldSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var oldPassword = await other.PostAsync("/api/auth/login", new { email, password = Password }))
        {
            oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await LoginAsync(other, email, NewPassword);

        await session.RefreshAntiforgeryAsync();
        using var reuse = await session.PostAsync("/api/auth/reset-password", new { email, token, newPassword = "Yet-Another-Secret-43" });
        reuse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reuse.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task Reset_with_a_forged_token_answers_the_same_for_known_and_unknown_emails()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var known = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, known);

        foreach (var email in new[] { known, ApiTestExtensions.UniqueEmail() })
        {
            // A weak password would trip account-dependent password rules if they ran before the token check.
            using var response = await client.PostAsync("/api/auth/reset-password", new { email, token = "forged", newPassword = "password123" });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
        }
    }

    [Fact]
    public async Task Parallel_dispatchers_deliver_every_email_exactly_once()
    {
        var email = ApiTestExtensions.UniqueEmail("outbox");
        var since = DateTime.UtcNow.AddSeconds(-1);
        await using (var db = fixture.CreateDbContext())
        {
            for (var i = 0; i < 25; i++)
            {
                db.EmailOutbox.Add(new EmailOutboxMessage
                {
                    ToAddress = email, Subject = $"m{i}", HtmlBody = "<p>x</p>", TextBody = "x", Template = "test.parallel",
                    CreatedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow,
                });
            }

            await db.SaveChangesAsync(Ct);
        }

        // Four extra dispatchers race the hosted one.
        var services = fixture.Factory.Services;
        var dispatchers = Enumerable.Range(0, 4)
            .Select(_ => ActivatorUtilities.CreateInstance<EmailOutboxDispatcher>(services))
            .ToList();
        await Task.WhenAll(dispatchers.Select(d => d.DispatchBatchAsync(Ct)));

        var deadline = DateTime.UtcNow.AddSeconds(15);
        int delivered;
        do
        {
            await Task.Delay(200, Ct);
            await using var db = fixture.CreateDbContext();
            delivered = await db.DevMailbox.CountAsync(m => m.ToAddress == email && m.ReceivedAtUtc >= since, Ct);
        }
        while (delivered < 25 && DateTime.UtcNow < deadline);

        await using var check = fixture.CreateDbContext();
        (await check.DevMailbox.CountAsync(m => m.ToAddress == email, Ct)).ShouldBe(25);
        (await check.DevMailbox.Where(m => m.ToAddress == email).Select(m => m.Subject).Distinct().CountAsync(Ct)).ShouldBe(25);
        (await check.EmailOutbox.CountAsync(m => m.ToAddress == email && m.SentAtUtc == null, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_answers_202_and_sends_nothing()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        var since = DateTime.UtcNow.AddSeconds(-1);

        using var response = await client.PostAsync("/api/auth/forgot-password", new { email });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Task.Delay(500, Ct);
        (await fixture.CountEmailsAsync(email, since)).ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_reset_and_confirmation_tokens_are_rejected()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);
        await using var db = fixture.CreateDbContext();
        var userId = (await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct)).Id;

        using var badReset = await client.PostAsync("/api/auth/reset-password", new { email, token = "forged", newPassword = "Brand-New-Secret-42" });
        using var badConfirm = await client.PostAsync("/api/auth/confirm-email", new { userId, token = "forged" });
        using var unknownUser = await client.PostAsync("/api/auth/confirm-email", new { userId = Guid.NewGuid(), token = "forged" });

        foreach (var response in new[] { badReset, badConfirm, unknownUser })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidToken);
        }
    }

    [Fact]
    public async Task Suspended_accounts_lose_their_session_and_cannot_sign_in()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);
        var me = await LoginAsync(client, email, Password);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserAccountStatus>().SuspendAsync(me.Id, Ct);
        }

        using (var session = await client.GetAsync("/api/auth/me"))
        {
            session.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Like the SPA: after losing the session, fetch an antiforgery token for the anonymous identity.
        await client.RefreshAntiforgeryAsync();
        using var login = await client.PostAsync("/api/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await login.ErrorCodeAsync()).ShouldBe(ErrorCodes.AccountSuspended);
    }

    [Fact]
    public async Task Passwords_are_stored_only_as_identity_hashes()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        await RegisterAsync(client, email);

        await using var db = fixture.CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), Ct);

        user.PasswordHash.ShouldNotBeNullOrEmpty();
        user.PasswordHash.ShouldNotContain(Password);
        Convert.FromBase64String(user.PasswordHash)[0].ShouldBe((byte)0x01); // Identity V3 format (PBKDF2)
        user.SecurityStamp.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Delivered_emails_do_not_keep_their_bodies_in_the_outbox()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        var since = DateTime.UtcNow.AddSeconds(-1);
        await RegisterAsync(client, email);
        await fixture.WaitForEmailAsync(email, "account.confirm-email", since);

        await using var db = fixture.CreateDbContext();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        EmailOutboxMessage? row;
        do
        {
            row = await db.EmailOutbox.AsNoTracking().SingleAsync(m => m.ToAddress == email, Ct);
            if (row.SentAtUtc is null)
            {
                await Task.Delay(100, Ct);
            }
        }
        while (row.SentAtUtc is null && DateTime.UtcNow < deadline);

        row.SentAtUtc.ShouldNotBeNull();
        row.HtmlBody.ShouldBeEmpty();
        row.TextBody.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dev_mailbox_endpoint_lists_messages_in_the_testing_environment()
    {
        using var client = await ApiClient.CreateAsync(fixture);
        var email = ApiTestExtensions.UniqueEmail();
        var since = DateTime.UtcNow.AddSeconds(-1);
        await RegisterAsync(client, email);
        await fixture.WaitForEmailAsync(email, "account.confirm-email", since);

        var items = await client.GetJsonAsync<List<DevMailboxItem>>($"/api/dev/mailbox?to={Uri.EscapeDataString(email)}");

        items.ShouldHaveSingleItem().Template.ShouldBe("account.confirm-email");
    }

    private static async Task RegisterAsync(ApiClient client, string email)
    {
        using var response = await client.PostAsync("/api/auth/register", new { email, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private static async Task<MeResponse> LoginAsync(ApiClient client, string email, string password)
    {
        using var response = await client.PostAsync("/api/auth/login", new { email, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(Ct))!;
    }
}
