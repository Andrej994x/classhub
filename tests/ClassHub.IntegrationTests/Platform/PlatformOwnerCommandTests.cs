using ClassHub.Api.Platform;
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

    private async Task<bool> IsOwnerAsync(Guid userId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await users.IsInRoleAsync((await users.FindByIdAsync(userId.ToString()))!, PlatformRoles.Owner);
    }
}
