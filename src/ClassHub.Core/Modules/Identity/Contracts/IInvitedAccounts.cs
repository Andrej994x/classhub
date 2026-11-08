using Microsoft.AspNetCore.Http;

namespace ClassHub.Core.Modules.Identity.Contracts;

/// <summary>
/// Accounts created from an invitation (student accounts, R2-P3). The invitation link arrived at the address, so the
/// email counts as confirmed, and no teacher workspace is provisioned.
/// </summary>
public interface IInvitedAccounts
{
    /// <summary>
    /// Creates an active account inside the caller's transaction (the shared DbContext). Throws a validation error on
    /// <c>password</c>, or 409 <c>EMAIL_REGISTERED</c> when the address already has an account (sign in to accept).
    /// </summary>
    Task<Guid> CreateConfirmedAsync(string email, string password, string? locale, CancellationToken cancellationToken);

    /// <summary>Signs the account in (password method, session cookie) after the caller committed.</summary>
    Task<MeResponse> SignInAsync(HttpContext http, Guid userId);
}
