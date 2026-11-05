using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ClassHub.Core.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Infrastructure;

/// <summary>A signed-in Platform Owner or Admin; <see cref="AuthenticatorKey"/> is null until the authenticator is enrolled.</summary>
public sealed record StaffSession(ApiClient Client, MeResponse Me, Guid WorkspaceId, string Email, string Password, string? AuthenticatorKey) : IDisposable
{
    public string Code() => Totp.Code(AuthenticatorKey ?? throw new InvalidOperationException("No authenticator enrolled."));

    public void Dispose() => Client.Dispose();
}

/// <summary>RFC 6238 codes (SHA-1, 30-second steps, 6 digits), what an authenticator app shows for a key.</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Code(string key, DateTimeOffset? at = null)
    {
        var secret = Base32Decode(key);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350 // RFC 6238 authenticators use HMAC-SHA1
        var hash = HMACSHA1.HashData(secret, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A well-formed code that is not valid now (from ten minutes ahead, outside the accepted window).</summary>
    public static string WrongCode(string key)
    {
        var now = Code(key);
        var later = Code(key, DateTimeOffset.UtcNow.AddMinutes(10));
        return later != now ? later : Code(key, DateTimeOffset.UtcNow.AddMinutes(20));
    }

    private static byte[] Base32Decode(string key)
    {
        var text = key.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"Not a base32 character: {c}");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        return [.. bytes];
    }
}

public static class PlatformStaffKit
{
    /// <summary>
    /// A teacher account with a platform role (granted like the CLI: role plus stamp rotation), signed in again and,
    /// unless <paramref name="enrollAuthenticator"/> is false, enrolled in the authenticator (the session then has amr=mfa).
    /// </summary>
    public static async Task<StaffSession> CreatePlatformStaffAsync(this ClassHubApiFixture fixture, string role, bool enrollAuthenticator = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var teacher = await fixture.CreateTeacherAsync();
        await fixture.GrantRoleAsync(teacher.Me.Id, role);

        // The rotated stamp ended the session: sign in again (no authenticator yet, so password only).
        await teacher.Client.RefreshAntiforgeryAsync();
        MeResponse me;
        using (var login = await teacher.Client.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password }))
        {
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
            me = (await login.Content.ReadFromJsonAsync<MeResponse>(TestContext.Current.CancellationToken))!;
        }

        string? key = null;
        if (enrollAuthenticator)
        {
            key = await EnrollAuthenticatorAsync(teacher.Client);
        }

        return new StaffSession(teacher.Client, me, teacher.WorkspaceId, teacher.Email, teacher.Password, key);
    }

    /// <summary>Sets up and confirms the authenticator for the signed-in client; returns the key.</summary>
    public static async Task<string> EnrollAuthenticatorAsync(ApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        AuthenticatorSetupResponse setup;
        using (var response = await client.PostAsync("/api/auth/mfa/authenticator"))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            setup = (await response.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>(TestContext.Current.CancellationToken))!;
        }

        using (var enable = await client.PostAsync("/api/auth/mfa/authenticator/enable", new { code = Totp.Code(setup.SharedKey) }))
        {
            enable.StatusCode.ShouldBe(HttpStatusCode.OK, await enable.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        return setup.SharedKey;
    }

    public static async Task GrantRoleAsync(this ClassHubApiFixture fixture, Guid userId, string role)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        (await users.AddToRoleAsync(user!, role)).Succeeded.ShouldBeTrue();
        (await users.UpdateSecurityStampAsync(user!)).Succeeded.ShouldBeTrue();
    }

    /// <summary>Signs in with password and authenticator code; returns the second step's response.</summary>
    public static async Task<HttpResponseMessage> SignInWithAuthenticatorAsync(ApiClient client, string email, string password, string code, bool rememberMe = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        await client.RefreshAntiforgeryAsync();
        using (var first = await client.PostAsync("/api/auth/login", new { email, password, rememberMe }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        return await client.PostAsync("/api/auth/login/two-factor", new { code, rememberMe });
    }
}
