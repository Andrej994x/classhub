using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Identity.Application;

/// <summary>
/// Identity's EF store, with the authenticator key and the recovery codes encrypted with Data Protection before they
/// reach <c>AspNetUserTokens</c> (Identity would store both as plain text). Everything else is unchanged.
/// </summary>
internal sealed class ProtectedUserStore(ClassHubDbContext context, IDataProtectionProvider protection, IdentityErrorDescriber? describer = null)
    : UserStore<AppUser, AppRole, ClassHubDbContext, Guid>(context, describer)
{
    /// <summary>The login provider Identity uses for its own tokens (authenticator key, recovery codes).</summary>
    internal const string InternalLoginProvider = "[AspNetUserStore]";

    internal const string ProtectorPurpose = "ClassHub.Identity.TwoFactorTokens.v1";

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.Ordinal) { "AuthenticatorKey", "RecoveryCodes" };

    private readonly IDataProtector _protector = protection.CreateProtector(ProtectorPurpose);

    public override Task SetTokenAsync(AppUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        var stored = value is not null && IsProtected(loginProvider, name) ? _protector.Protect(value) : value;
        return base.SetTokenAsync(user, loginProvider, name, stored, cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(AppUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var stored = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return stored is not null && IsProtected(loginProvider, name) ? _protector.Unprotect(stored) : stored;
    }

    private static bool IsProtected(string loginProvider, string name) =>
        loginProvider == InternalLoginProvider && ProtectedNames.Contains(name);
}
