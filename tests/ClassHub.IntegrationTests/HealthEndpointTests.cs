using System.Net;
using System.Net.Http.Json;
using ClassHub.Api;
using ClassHub.IntegrationTests.Infrastructure;

namespace ClassHub.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests(ClassHubApiFixture fixture)
{
    [Fact]
    public async Task Health_returns_ok_without_authentication()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);
        body.ShouldNotBeNull().Status.ShouldBe("ok");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_checks_report_healthy_without_details(string path)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }
}
