using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>
/// API host for platform tests that never touch a database (no Docker needed): the connection string
/// points at a closed local port and no test calls a database-backed endpoint.
/// </summary>
public sealed class PlatformApiFactory(
    bool includeProbeModule = true,
    IReadOnlyDictionary<string, string?>? settings = null,
    System.Net.IPAddress? remoteIpAddress = null) : WebApplicationFactory<Program>
{
    /// <summary>Unreachable on purpose; only used so the host can be built.</summary>
    public const string UnusedConnectionString =
        "Server=localhost,1;Database=ClassHubTest_nodatabase;User Id=nobody;Password=none;Connect Timeout=1;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}", UnusedConnectionString);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (includeProbeModule)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IModule, ProbeModule>();
                services.AddScoped<IValidator<ProbeRequest>, ProbeRequestValidator>();
            });
        }

        if (remoteIpAddress is not null)
        {
            // TestServer requests have no peer address; simulate the TCP peer the way Kestrel would.
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(remoteIpAddress)));
        }
    }

    private sealed class RemoteIpStartupFilter(System.Net.IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed record ProbeRequest(string? Name, int Age, IReadOnlyList<ProbeItem>? Items);

public sealed record ProbeItem(string? DisplayName);

internal sealed class ProbeRequestValidator : AbstractValidator<ProbeRequest>
{
    public ProbeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Age).InclusiveBetween(0, 150);
        RuleForEach(x => x.Items).ChildRules(item => item.RuleFor(i => i.DisplayName).NotEmpty());
    }
}

/// <summary>Test-only endpoints exercising the platform pipeline (mapped under /api/_probe).</summary>
internal sealed class ProbeModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var probe = endpoints.MapGroup("/_probe");

        probe.MapGet("/not-found", () => { throw new NotFoundException(); }).AllowAnonymous();
        probe.MapGet("/rule", () => { throw new BusinessRuleException("The plan limit was reached.", "PLAN_LIMIT_REACHED"); }).AllowAnonymous();
        probe.MapGet("/crash", () => { throw new InvalidOperationException("secret internal detail 4711"); }).AllowAnonymous();
        probe.MapPost("/validate", (ProbeRequest request) => TypedResults.Ok(request)).WithValidation<ProbeRequest>().AllowAnonymous();
        probe.MapGet("/protected", () => TypedResults.Ok()).RequireAuthorization(ClassHubPolicies.Authenticated);
        probe.MapGet("/no-metadata", () => TypedResults.Ok());
        probe.MapGet("/limited", () => TypedResults.Ok()).RequireRateLimiting(RateLimitPolicies.Auth).AllowAnonymous();
        probe.MapPost("/antiforgery", () => TypedResults.NoContent()).RequireAntiforgeryHeader().AllowAnonymous();
        probe.MapGet("/antiforgery", () => TypedResults.NoContent()).RequireAntiforgeryHeader().AllowAnonymous();
    }
}
