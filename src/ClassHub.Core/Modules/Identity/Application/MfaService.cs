using System.Security.Claims;
using System.Text;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ClassHub.Core.Modules.Identity.Application;

/// <summary>
/// The authenticator app (TOTP, RFC 6238) and recovery codes on top of ASP.NET Core Identity (R1-P9). Required for
/// platform roles: their endpoints need a session signed in with the authenticator (<c>amr=mfa</c>).
/// </summary>
internal sealed partial class MfaService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    IAuditLog audit,
    ClassHubDbContext db,
    ILogger<MfaService> logger) : IMfaStepUp
{
    public const int RecoveryCodeCount = 10;
    public const string Issuer = "ClassHub";

    public async Task<MfaStatusResponse> GetStatusAsync(ClaimsPrincipal principal)
    {
        var user = await CurrentUserAsync(principal);
        return new MfaStatusResponse(
            user.TwoFactorEnabled,
            IsMultiFactorSession(principal),
            await IsPlatformStaffAsync(user),
            user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0);
    }

    /// <summary>
    /// Creates a new authenticator key. While the authenticator is on, only a session signed in with it may replace
    /// the key (the recovery codes stay valid until the new key is confirmed).
    /// </summary>
    public async Task<AuthenticatorSetupResponse> SetupAsync(HttpContext http)
    {
        var user = await CurrentUserAsync(http.User);
        if (user.TwoFactorEnabled && !IsMultiFactorSession(http.User))
        {
            throw MfaRequired();
        }

        // Identity rotates the security stamp: other sessions end, this one is renewed (keeping its amr claim).
        ThrowIfFailed(await users.ResetAuthenticatorKeyAsync(user));
        await signIn.RefreshSignInAsync(user);

        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("No authenticator key was stored.");
        var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
        return new AuthenticatorSetupResponse(FormatKey(key), uri);
    }

    /// <summary>
    /// Confirms the new key with a code from the app: turns the authenticator on, issues new recovery codes (shown
    /// once) and upgrades this session to <c>amr=mfa</c>, since the code proves the second factor.
    /// </summary>
    public async Task<RecoveryCodesResponse> EnableAsync(HttpContext http, MfaCodeRequest request)
    {
        var user = await CurrentUserAsync(http.User);
        if (user.TwoFactorEnabled && !IsMultiFactorSession(http.User))
        {
            throw MfaRequired();
        }

        if (await users.GetAuthenticatorKeyAsync(user) is null || !await IsValidCodeAsync(user, request.Code))
        {
            throw CodeInvalid();
        }

        IEnumerable<string> codes;
        await using (var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted))
        {
            ThrowIfFailed(await users.SetTwoFactorEnabledAsync(user, true)); // rotates the security stamp
            codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
            audit.Record("account.mfa.enabled", "User", user.Id.ToString());
            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);
        }

        var current = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        await signIn.SignInWithClaimsAsync(user, current.Properties, [new Claim(AuthenticationMethods.ClaimType, AuthenticationMethods.MultiFactor)]);
        http.User = await signIn.CreateUserPrincipalAsync(user);
        http.IssueAntiforgeryTokens();

        LogEnabled(logger, user.Id);
        return new RecoveryCodesResponse([.. codes]);
    }

    /// <summary>New recovery codes (the old ones stop working); needs a session with the authenticator and a fresh code.</summary>
    public async Task<RecoveryCodesResponse> RegenerateRecoveryCodesAsync(ClaimsPrincipal principal, MfaCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(principal);
        if (!user.TwoFactorEnabled || !IsMultiFactorSession(principal))
        {
            throw MfaRequired();
        }

        await VerifyStepUpAsync(user, request.Code);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        audit.Record("account.mfa.recovery-codes.regenerated", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RecoveryCodesResponse([.. codes]);
    }

    public async Task VerifyAsync(ClaimsPrincipal principal, string? code, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(principal);
        if (!user.TwoFactorEnabled || !IsMultiFactorSession(principal))
        {
            throw MfaRequired();
        }

        await VerifyStepUpAsync(user, code);
    }

    /// <summary>A wrong step-up code counts as a failed sign-in attempt (Identity lockout), on top of the rate limit.</summary>
    private async Task VerifyStepUpAsync(AppUser user, string? code)
    {
        if (!await IsValidCodeAsync(user, code))
        {
            await users.AccessFailedAsync(user);
            LogStepUpFailed(logger, user.Id);
            throw CodeInvalid();
        }
    }

    private async Task<bool> IsValidCodeAsync(AppUser user, string? code)
    {
        var normalized = NormalizeCode(code);
        return normalized.Length == 6 && normalized.All(char.IsAsciiDigit) &&
            await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, normalized);
    }

    private async Task<bool> IsPlatformStaffAsync(AppUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return roles.Contains(PlatformRoles.Owner) || roles.Contains(PlatformRoles.Admin);
    }

    private async Task<AppUser> CurrentUserAsync(ClaimsPrincipal principal) =>
        await users.GetUserAsync(principal) ?? throw new AuthenticationFailedException("You are not signed in.", ErrorCodes.Unauthorized);

    internal static bool IsMultiFactorSession(ClaimsPrincipal principal) =>
        principal.HasClaim(AuthenticationMethods.ClaimType, AuthenticationMethods.MultiFactor);

    /// <summary>Codes as people type them: spaces and hyphens are ignored.</summary>
    internal static string NormalizeCode(string? code) =>
        string.Concat((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-'));

    /// <summary>
    /// Recovery codes as Identity issues them (<c>XXXXX-XXXXX</c>, uppercase): spaces are ignored, the case too, and the
    /// hyphen may be left out.
    /// </summary>
    internal static string NormalizeRecoveryCode(string? code)
    {
        var text = string.Concat((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return text.Length == 10 && !text.Contains('-', StringComparison.Ordinal) ? $"{text[..5]}-{text[5..]}" : text;
    }

    /// <summary>Groups of four lowercase characters, easier to type into an app than one long string.</summary>
    internal static string FormatKey(string key)
    {
        var text = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                text.Append(' ');
            }

            text.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return text.ToString().ToLowerInvariant();
    }

    internal static ForbiddenOperationException MfaRequired() =>
        new("Sign in with your authenticator app first.", ErrorCodes.MfaRequired);

    internal static BadRequestException CodeInvalid() =>
        new("The code is not valid. Enter the current code from your authenticator app.", ErrorCodes.MfaCodeInvalid);

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} turned on the authenticator")]
    private static partial void LogEnabled(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Step-up code rejected for user {UserId}")]
    private static partial void LogStepUpFailed(ILogger logger, Guid userId);
}
