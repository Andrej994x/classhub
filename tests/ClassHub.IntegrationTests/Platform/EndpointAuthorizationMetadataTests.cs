using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>
/// Deny by default: every real endpoint must declare <c>AllowAnonymous</c> or a named ClassHub policy.
/// Relying on the fallback policy alone (or on an unnamed <c>RequireAuthorization()</c>) fails this test.
/// </summary>
public sealed class EndpointAuthorizationMetadataTests
{
    [Fact]
    public void Every_endpoint_declares_anonymous_access_or_a_named_policy()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false);
        _ = factory.Services; // builds the host

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        endpoints.ShouldNotBeEmpty();

        var undeclared = endpoints
            .Where(e =>
                e.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => !string.IsNullOrWhiteSpace(a.Policy)))
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .ToList();

        undeclared.ShouldBeEmpty("Endpoints without AllowAnonymous or a named policy: " + string.Join("; ", undeclared));
    }

    [Fact]
    public void Known_platform_endpoints_are_mapped()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false);
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        routes.ShouldContain("/health");
        routes.ShouldContain("/health/ready");
        routes.ShouldContain("/api/auth/antiforgery");
    }
}
