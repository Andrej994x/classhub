using System.Security.Claims;

namespace ClassHub.Core.Modules.Identity.Contracts;

/// <summary>Changes an account's status; used by platform administration (R1-P9).</summary>
public interface IUserAccountStatus
{
    /// <summary>
    /// Suspends the account (audited with the reason): it can no longer sign in, existing sessions end at the next
    /// security-stamp validation (the stamp is rotated), and its public site is hidden at once.
    /// </summary>
    /// <exception cref="ClassHub.Core.SharedKernel.Errors.ConflictException">The account is already suspended.</exception>
    Task SuspendAsync(Guid userId, string reason, CancellationToken cancellationToken);

    /// <summary>Reactivates a suspended account (audited with the reason).</summary>
    /// <exception cref="ClassHub.Core.SharedKernel.Errors.ConflictException">The account is not suspended.</exception>
    Task ReactivateAsync(Guid userId, string reason, CancellationToken cancellationToken);
}

/// <summary>Step-up check for critical operations: the signed-in user confirms with a fresh authenticator code.</summary>
public interface IMfaStepUp
{
    /// <summary>Throws (400 <c>MFA_CODE_INVALID</c>) unless <paramref name="code"/> is a valid code of the user's authenticator.</summary>
    Task VerifyAsync(ClaimsPrincipal principal, string? code, CancellationToken cancellationToken);
}
