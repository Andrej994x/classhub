namespace ClassHub.Core.Modules.Identity.Contracts;

public sealed record RegisterRequest(string? Email, string? Password, string? Locale);

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe);

public sealed record ConfirmEmailRequest(Guid UserId, string? Token);

public sealed record EmailRequest(string? Email);

public sealed record ResetPasswordRequest(string? Email, string? Token, string? NewPassword);

/// <summary>The signed-in user, as the web app sees it.</summary>
public sealed record MeResponse(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    string Locale,
    string TimeZone,
    IReadOnlyList<string> PlatformRoles);

/// <summary>Account preferences; omitted fields stay unchanged.</summary>
public sealed record UpdateMeRequest(string? Locale, string? TimeZone);

/// <summary>Generic acknowledgement that never reveals whether an account exists.</summary>
public sealed record AcceptedResponse(string Message);

/// <summary>
/// <c>POST /api/auth/login</c> answered 202: the password was right and the account uses the authenticator. The
/// sign-in finishes with <c>POST /api/auth/login/two-factor</c> within five minutes.
/// </summary>
public sealed record TwoFactorRequiredResponse(bool RequiresTwoFactor);

/// <summary>The second sign-in step: a 6-digit authenticator code or one of the recovery codes.</summary>
public sealed record TwoFactorLoginRequest(string? Code, string? RecoveryCode, bool RememberMe);

/// <summary>
/// The account's two-factor state. <c>Required</c>: the account has a platform role, whose endpoints need a
/// session signed in with the authenticator (<c>SessionVerified</c>).
/// </summary>
public sealed record MfaStatusResponse(bool Enabled, bool SessionVerified, bool Required, int RecoveryCodesLeft);

/// <summary>A new authenticator key: shown once as text and as an <c>otpauth://</c> link (QR code).</summary>
public sealed record AuthenticatorSetupResponse(string SharedKey, string AuthenticatorUri);

/// <summary>An authenticator code (enrollment, step-up for critical operations).</summary>
public sealed record MfaCodeRequest(string? Code);

/// <summary>New single-use recovery codes; shown only once.</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
