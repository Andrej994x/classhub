using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.Extensions.Options;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>R0-P3: errors, validation, authorization fallback, rate limits, antiforgery and headers.</summary>
public sealed class PlatformPipelineTests : IClassFixture<PlatformPipelineTests.Host>
{
    private readonly Host _host;

    public PlatformPipelineTests(Host host) => _host = host;

    public sealed class Host() : IDisposable
    {
        public PlatformApiFactory Factory { get; } = new(settings: new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitLimit"] = "3",
            ["RateLimiting:AuthWindowSeconds"] = "3600",
        });

        public void Dispose() => Factory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Expected_errors_become_problem_details_with_code_and_trace_id()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/_probe/not-found", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.NotFound);
        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Business_rule_errors_keep_their_specific_code()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/_probe/rule", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("PLAN_LIMIT_REACHED");
        problem.GetProperty("title").GetString().ShouldBe("The plan limit was reached.");
    }

    [Fact]
    public async Task Unexpected_errors_are_generic_500_without_internal_details()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/_probe/crash", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain("4711");
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain("   at ");
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().ShouldBe(ErrorCodes.InternalError);
    }

    [Fact]
    public async Task Unknown_api_routes_are_denied_to_anonymous_callers_with_a_problem()
    {
        // Deny by default: the fallback policy also covers requests that match no endpoint, so an
        // anonymous caller cannot probe which routes exist (authenticated callers get 404; see R0-P4).
        using var client = _host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Invalid_input_returns_validation_problem_with_camel_case_fields()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/_probe/validate", UriKind.Relative),
            new { name = "", age = -1, items = new[] { new { displayName = "" } } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue();
        errors.TryGetProperty("age", out _).ShouldBeTrue();
        errors.TryGetProperty("items[0].displayName", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Valid_input_reaches_the_handler()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/_probe/validate", UriKind.Relative), new { name = "Ана", age = 30 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Malformed_json_is_a_400_problem()
    {
        using var client = _host.Factory.CreateClient();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/_probe/validate", UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe(ErrorCodes.BadRequest);
    }

    [Theory]
    [InlineData("/api/_probe/protected")]
    [InlineData("/api/_probe/no-metadata")]
    public async Task Anonymous_requests_to_protected_or_undeclared_endpoints_get_401_problem(string path)
    {
        using var client = _host.Factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Rate_limited_endpoints_return_429_problem_with_retry_after()
    {
        using var client = _host.Factory.CreateClient();
        var uri = new Uri("/api/_probe/limited", UriKind.Relative);

        for (var i = 0; i < 3; i++)
        {
            using var ok = await client.GetAsync(uri, Ct);
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var limited = await client.GetAsync(uri, Ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await ReadJsonAsync(limited)).GetProperty("code").GetString().ShouldBe(ErrorCodes.RateLimited);
    }

    [Fact]
    public async Task Unsafe_request_without_antiforgery_token_is_rejected()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/api/_probe/antiforgery", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe(ErrorCodes.AntiforgeryInvalid);
    }

    [Fact]
    public async Task Unsafe_request_with_antiforgery_cookie_and_header_is_accepted()
    {
        using var client = _host.Factory.CreateClient();
        var token = await GetRequestTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/_probe/antiforgery", UriKind.Relative));
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Antiforgery_header_with_a_forged_token_is_rejected()
    {
        using var client = _host.Factory.CreateClient();
        _ = await GetRequestTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/_probe/antiforgery", UriKind.Relative));
        request.Headers.Add("X-XSRF-TOKEN", "forged-token");
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Safe_methods_do_not_need_an_antiforgery_token()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/_probe/antiforgery", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Antiforgery_endpoint_issues_a_readable_request_token_cookie_and_an_http_only_cookie_token()
    {
        using var client = _host.Factory.CreateClient(new() { HandleCookies = false });

        using var response = await client.GetAsync(new Uri("/api/auth/antiforgery", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var requestToken = cookies.Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        requestToken.ShouldContain("samesite=strict", Case.Insensitive);
        requestToken.ShouldNotContain("httponly", Case.Insensitive);
        var cookieToken = cookies.Single(c => c.StartsWith(".ClassHub.Antiforgery=", StringComparison.Ordinal));
        cookieToken.ShouldContain("httponly", Case.Insensitive);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_a_deny_all_csp()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), Ct);

        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header(response, "Content-Security-Policy").ShouldStartWith("default-src 'none'");
        Header(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
    }

    [Fact]
    public async Task Page_responses_get_the_same_origin_csp()
    {
        using var client = _host.Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/some-page", UriKind.Relative), Ct);

        Header(response, "Content-Security-Policy").ShouldContain("default-src 'self'");
        Header(response, "Content-Security-Policy").ShouldContain("object-src 'none'");
        Header(response, "X-Frame-Options").ShouldBe("SAMEORIGIN");
    }

    [Fact]
    public void Invalid_rate_limit_configuration_stops_the_host_at_startup()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false, settings: new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitLimit"] = "0",
        });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }

    private static async Task<string> GetRequestTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri("/api/auth/antiforgery", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(setCookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues)
        : string.Empty;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
}
