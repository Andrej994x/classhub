using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Identity;

namespace ClassHub.Core.Modules.Identity.Application;

internal sealed class UserAccountStatusService(UserManager<AppUser> users) : IUserAccountStatus
{
    public async Task SuspendAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
        user.Status = UserStatus.Suspended;
        await users.UpdateAsync(user);
        // Ends every session: cookies carry the old stamp and fail validation.
        await users.UpdateSecurityStampAsync(user);
    }

    public async Task ReactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
        user.Status = UserStatus.Active;
        await users.UpdateAsync(user);
    }
}
