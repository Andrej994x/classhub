using System.Security.Claims;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Identity.Application;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace ClassHub.UnitTests.Identity;

public sealed class MfaUnitTests
{
    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData(" 123-456 ", "123456")]
    [InlineData(null, "")]
    public void Authenticator_codes_ignore_spaces_and_hyphens(string? input, string expected) =>
        MfaService.NormalizeCode(input).ShouldBe(expected);

    [Theory]
    [InlineData("ABCDE-12345", "ABCDE-12345")]
    [InlineData(" abcde-12345 ", "ABCDE-12345")]
    [InlineData("abcde12345", "ABCDE-12345")]
    [InlineData("abc de 12345", "ABCDE-12345")]
    public void Recovery_codes_keep_identitys_format(string input, string expected) =>
        MfaService.NormalizeRecoveryCode(input).ShouldBe(expected);

    [Fact]
    public void The_key_is_shown_in_lowercase_groups_of_four() =>
        MfaService.FormatKey("ABCDEFGHIJKLMNOPQR").ShouldBe("abcd efgh ijkl mnop qr");

    [Theory]
    [InlineData(PlatformRoles.Owner, true)]
    [InlineData(PlatformRoles.Admin, true)]
    [InlineData(null, false)]
    public void Staff_sessions_are_never_persistent_and_shorter(string? role, bool limited)
    {
        var identity = new ClaimsIdentity("test", ClaimTypes.Name, ClaimTypes.Role);
        if (role is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var issued = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        var properties = new AuthenticationProperties { IsPersistent = true, IssuedUtc = issued, ExpiresUtc = issued.AddDays(30) };
        var scheme = new AuthenticationScheme("cookie", null, typeof(CookieAuthenticationHandler));
        var context = new CookieSigningInContext(new DefaultHttpContext(), scheme, new CookieAuthenticationOptions(), new ClaimsPrincipal(identity), properties, new CookieOptions());

        PlatformAuthorization.LimitStaffSession(context, TimeSpan.FromHours(8));

        properties.IsPersistent.ShouldBe(!limited);
        properties.ExpiresUtc.ShouldBe(limited ? issued.AddHours(8) : issued.AddDays(30));
    }
}
