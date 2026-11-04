using ClassHub.Api.Platform;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>The local CLI command that bootstraps a Platform Owner (never exposed over HTTP).</summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformOwnerCommandTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Grant_platform_owner_rules()
    {
        // Runs as one test: "an owner already exists" depends on the shared database state.
        using var first = await fixture.CreateTeacherAsync();
        using var second = await fixture.CreateTeacherAsync();

        (await RunAsync(ApiTestExtensions.UniqueEmail(), force: false)).ShouldBe(1);

        var existingOwners = await OwnersAsync();
        var firstCode = await RunAsync(first.Email, force: existingOwners > 0);
        firstCode.ShouldBe(0);
        (await RunAsync(first.Email, force: false)).ShouldBe(0); // idempotent

        (await RunAsync(second.Email, force: false)).ShouldBe(2); // an owner exists
        (await IsOwnerAsync(second.Me.Id)).ShouldBeFalse();
        (await RunAsync(second.Email, force: true)).ShouldBe(0);
        (await IsOwnerAsync(second.Me.Id)).ShouldBeTrue();

        await using var db = fixture.CreateDbContext();
        (await db.AuditLog.CountAsync(a => a.Action == "platform.role.granted" && a.TargetId == second.Me.Id.ToString(), Ct)).ShouldBe(1);

        // The role change ended the existing session (security stamp); a new sign-in carries the role.
        using (var stale = await second.Client.GetAsync("/api/auth/me"))
        {
            stale.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task A_failure_before_the_audit_entry_is_saved_grants_nothing()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var stampBefore = await SecurityStampAsync(teacher.Me.Id);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var services = new FailingAuditServices(scope.ServiceProvider);
            await Should.ThrowAsync<InvalidOperationException>(() => PlatformCommands.GrantPlatformOwnerAsync(services, teacher.Email, forceAdd: true, TextWriter.Null));
        }

        (await IsOwnerAsync(teacher.Me.Id)).ShouldBeFalse();
        (await SecurityStampAsync(teacher.Me.Id)).ShouldBe(stampBefore);
        await using var db = fixture.CreateDbContext();
        (await db.AuditLog.CountAsync(a => a.Action == "platform.role.granted" && a.TargetId == teacher.Me.Id.ToString(), Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_bootstraps_are_serialized_so_only_the_first_owner_is_added_without_force()
    {
        // Tests in this collection run one at a time, so the owner state can be set up here.
        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AspNetUserRoles WHERE RoleId = {PlatformRoles.OwnerRoleId}", Ct);
        }

        using var winner = await fixture.CreateTeacherAsync();
        using var loser = await fixture.CreateTeacherAsync();

        // Another bootstrap holds the lock and is about to grant the role to `winner`.
        await using var other = fixture.CreateDbContext();
        await using var otherTransaction = await other.Database.BeginTransactionAsync(Ct);
        await other.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sp_getapplock @Resource = {PlatformCommands.OwnerBootstrapLock}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'", Ct);

        var contender = RunAsync(loser.Email, force: false);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        contender.IsCompleted.ShouldBeFalse(); // waits for the lock instead of checking stale state

        await other.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES ({winner.Me.Id}, {PlatformRoles.OwnerRoleId})", Ct);
        await otherTransaction.CommitAsync(Ct);

        (await contender).ShouldBe(2); // an owner exists now: refused without --force-add
        (await IsOwnerAsync(loser.Me.Id)).ShouldBeFalse();
    }

    [Fact]
    public void The_command_is_not_reachable_over_http()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false);
        var routes = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty);

        routes.ShouldNotContain(r => r.Contains("platform-owner", StringComparison.OrdinalIgnoreCase) || r.Contains("grant", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> RunAsync(string email, bool force)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await PlatformCommands.GrantPlatformOwnerAsync(scope.ServiceProvider, email, force, TextWriter.Null);
    }

    private async Task<int> OwnersAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetUsersInRoleAsync(PlatformRoles.Owner)).Count;
    }

    private async Task<string?> SecurityStampAsync(Guid userId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync(Ct);
    }

    /// <summary>The real scope, except that recording the audit entry fails (after the role was added).</summary>
    private sealed class FailingAuditServices(IServiceProvider inner) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IAuditLog) ? new ThrowingAuditLog() : inner.GetService(serviceType);
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public void Record(string action, string targetType, string targetId, Guid? workspaceId = null, object? before = null, object? after = null, Guid? actorUserId = null) =>
            throw new InvalidOperationException("audit store unavailable");
    }

    private async Task<bool> IsOwnerAsync(Guid userId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await users.IsInRoleAsync((await users.FindByIdAsync(userId.ToString()))!, PlatformRoles.Owner);
    }
}
