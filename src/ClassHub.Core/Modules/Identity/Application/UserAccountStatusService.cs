using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Identity;

namespace ClassHub.Core.Modules.Identity.Application;

/// <summary>
/// Suspension and reactivation. The status, the security stamp and the audit entry commit together; the public pages
/// are evicted only after the commit, so no request can cache the old state again in between.
/// </summary>
internal sealed class UserAccountStatusService(UserManager<AppUser> users, ClassHubDbContext db, IAuditLog audit, IPublicSiteCache publicSites)
    : IUserAccountStatus
{
    public Task SuspendAsync(Guid userId, string reason, CancellationToken cancellationToken) =>
        ChangeAsync(userId, UserStatus.Active, UserStatus.Suspended, "user.suspended", reason, cancellationToken);

    public Task ReactivateAsync(Guid userId, string reason, CancellationToken cancellationToken) =>
        ChangeAsync(userId, UserStatus.Suspended, UserStatus.Active, "user.reactivated", reason, cancellationToken);

    private async Task ChangeAsync(Guid userId, UserStatus from, UserStatus to, string action, string reason, CancellationToken cancellationToken)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
            if (user.Status != from)
            {
                throw new ConflictException($"The account is already {to.ToString().ToLowerInvariant()}.", ErrorCodes.InvalidStatusTransition);
            }

            user.Status = to;
            // Saves the status and rotates the stamp: every session ends at its next validation. Identity checks the
            // concurrency stamp, so a parallel change of the same account fails instead of being overwritten.
            var result = await users.UpdateSecurityStampAsync(user);
            if (!result.Succeeded)
            {
                throw result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
                    ? new ConflictException("The account was changed at the same time. Reload and try again.", ErrorCodes.ConcurrencyConflict)
                    : new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            audit.Record(action, "User", user.Id.ToString(), before: new { status = from.ToString() }, after: new { status = to.ToString(), reason });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // A suspended owner's public site is hidden at once (and shown again on reactivation), not when the cache expires.
        await publicSites.EvictAllAsync(cancellationToken);
    }
}
