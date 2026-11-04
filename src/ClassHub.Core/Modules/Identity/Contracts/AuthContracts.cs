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

/// <summary>Generic acknowledgement that never reveals whether an account exists.</summary>
public sealed record AcceptedResponse(string Message);
