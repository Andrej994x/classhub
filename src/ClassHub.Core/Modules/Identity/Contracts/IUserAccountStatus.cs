namespace ClassHub.Core.Modules.Identity.Contracts;

/// <summary>Changes an account's status; used by platform administration (R1-P9).</summary>
public interface IUserAccountStatus
{
    /// <summary>
    /// Suspends the account: it can no longer sign in, and existing sessions end at the next security-stamp
    /// validation (the stamp is rotated).
    /// </summary>
    Task SuspendAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Reactivates a suspended account.</summary>
    Task ReactivateAsync(Guid userId, CancellationToken cancellationToken);
}
