using ClassHub.Api.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>Development-only surface (dev mailbox) and the E2E database guard.</summary>
public sealed class EnvironmentSurfaceTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_dev_mailbox_is_not_mapped_outside_development_testing_and_e2e(string environment)
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false, environment: environment);

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToList();

        routes.ShouldNotBeEmpty();
        routes.ShouldNotContain(r => r.Contains("mailbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_dev_mailbox_is_mapped_in_testing()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false);
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .ShouldContain(e => e.RoutePattern.RawText == "/api/dev/mailbox");
    }

    [Fact]
    public void The_e2e_environment_refuses_to_start_on_a_non_disposable_database()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false, environment: E2EDatabase.EnvironmentName);

        var error = Should.Throw<InvalidOperationException>(() => factory.Services.GetRequiredService<EndpointDataSource>());
        error.Message.ShouldContain("Refusing to start the E2E environment");
    }

    [Fact]
    public void The_e2e_guard_accepts_only_the_disposable_container_database() =>
        Should.NotThrow(() => E2EDatabase.AssertDisposable(
            "Server=127.0.0.1,14333;Database=ClassHubE2E_3f2a;User Id=sa;Password=x;Encrypt=True;TrustServerCertificate=True"));

    [Theory]
    [InlineData(@"Server=.\SQLEXPRESS;Database=ClassHubDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=ClassHubE2E_x;Integrated Security=True")]
    [InlineData("Server=127.0.0.1,14333;Database=ClassHubDb;User Id=sa;Password=x")]
    [InlineData("Server=127.0.0.1,14333;Database=ClassHubE2E_x;Integrated Security=True")]
    [InlineData("Server=db.example.com,1433;Database=ClassHubE2E_x;User Id=sa;Password=x")]
    [InlineData("Server=127.0.0.1;Database=ClassHubE2E_x;User Id=sa;Password=x")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Database=ClassHubE2E_x;User Id=sa;Password=x")]
    public void The_e2e_guard_refuses_any_other_database(string connectionString) =>
        Should.Throw<InvalidOperationException>(() => E2EDatabase.AssertDisposable(connectionString));
}
