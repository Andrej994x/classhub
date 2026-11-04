using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Api.Platform;

/// <summary>
/// Local command-line operations that must never be exposed over HTTP. Usage:
/// <c>dotnet run --project src/ClassHub.Api -- grant-platform-owner &lt;email&gt; [--force-add]</c>.
/// </summary>
public static class PlatformCommands
{
    public const string GrantPlatformOwner = "grant-platform-owner";

    /// <summary>Application lock (sp_getapplock) that serializes Platform Owner bootstraps.</summary>
    public const string OwnerBootstrapLock = "classhub:platform-owner-bootstrap";

    /// <summary>Runs a command if <paramref name="args"/> names one; returns false to start the web host.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args[0] != GrantPlatformOwner)
        {
            return false;
        }

        if (args.Length < 2)
        {
            await Console.Error.WriteLineAsync($"Usage: {GrantPlatformOwner} <email> [--force-add]");
            Environment.ExitCode = 64;
            return true;
        }

        await using var scope = app.Services.CreateAsyncScope();
        Environment.ExitCode = await GrantPlatformOwnerAsync(
            scope.ServiceProvider, args[1], args.Contains("--force-add", StringComparer.Ordinal), Console.Out);
        return true;
    }

    /// <summary>
    /// Grants the Platform Owner role to an existing account. Refuses when another owner already exists,
    /// unless <paramref name="forceAdd"/> is set. Audited (actor: CLI).
    /// </summary>
    /// <returns>0 on success, 1 if the account does not exist, 2 if an owner exists and force was not given.</returns>
    public static async Task<int> GrantPlatformOwnerAsync(IServiceProvider services, string email, bool forceAdd, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var users = services.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            await output.WriteLineAsync("No account with that email exists. Register it first.");
            return 1;
        }

        // One transaction, serialized across processes: the "is there an owner yet?" check, the grant, the
        // session revocation and the audit entry. Without the lock two concurrent bootstraps of different
        // accounts could both see no owner and both succeed without --force-add. The Identity store saves on
        // its own, so without the transaction a later failure would leave an unaudited owner.
        var db = services.GetRequiredService<ClassHubDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DECLARE @result int; EXEC @result = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; " +
            "IF @result < 0 THROW 50001, 'Could not acquire the platform owner lock.', 1;",
            OwnerBootstrapLock);

        if (await users.IsInRoleAsync(user, PlatformRoles.Owner))
        {
            await output.WriteLineAsync("The account is already a Platform Owner.");
            return 0;
        }

        var owners = await users.GetUsersInRoleAsync(PlatformRoles.Owner);
        if (owners.Count > 0 && !forceAdd)
        {
            await output.WriteLineAsync("A Platform Owner already exists. Use --force-add to add another one.");
            return 2;
        }

        ThrowIfFailed(await users.AddToRoleAsync(user, PlatformRoles.Owner));

        // Role changes end existing sessions, so the new role applies at the next sign-in.
        ThrowIfFailed(await users.UpdateSecurityStampAsync(user));

        services.GetRequiredService<IAuditLog>().Record("platform.role.granted", "User", user.Id.ToString(), after: new { role = PlatformRoles.Owner, via = "cli" });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await output.WriteLineAsync("Platform Owner role granted.");
        return 0;
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
