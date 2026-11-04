using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClassHub.Core.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Infrastructure;

/// <summary>
/// A browser-like client for the in-memory API: keeps cookies (auth + antiforgery) and sends the
/// <c>X-XSRF-TOKEN</c> header on unsafe requests, refreshing the token whenever the API re-issues it.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    private ApiClient(HttpClient http) => _http = http;

    public string? AntiforgeryToken { get; private set; }

    public HttpClient Http => _http;

    public static Task<ApiClient> CreateAsync(ClassHubApiFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return CreateAsync(fixture.Factory);
    }

    /// <summary>A client for a derived factory (for example with replaced services).</summary>
    public static async Task<ApiClient> CreateAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = new ApiClient(factory.CreateClient(new() { AllowAutoRedirect = false }));
        await client.RefreshAntiforgeryAsync();
        return client;
    }

    public async Task RefreshAntiforgeryAsync()
    {
        using var response = await _http.GetAsync(new Uri("/api/auth/antiforgery", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Capture(response);
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, bool withAntiforgery = true) =>
        SendAsync(HttpMethod.Post, path, body, withAntiforgery);

    public Task<HttpResponseMessage> PutAsync(string path, object? body = null) => SendAsync(HttpMethod.Put, path, body, true);

    public Task<HttpResponseMessage> PatchAsync(string path, object? body = null) => SendAsync(HttpMethod.Patch, path, body, true);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null, true);

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null, false);

    public async Task<T> GetJsonAsync<T>(string path)
    {
        using var response = await GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool withAntiforgery)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (withAntiforgery && AntiforgeryToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", AntiforgeryToken);
        }

        var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        Capture(response);
        return response;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Like the SPA: keep the latest request token the API returned (sign-in, sign-out, token endpoint).</summary>
    private void Capture(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-XSRF-TOKEN", out var values))
        {
            AntiforgeryToken = values.Last();
        }
    }
}

public static class ApiTestExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement.Clone();
    }

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync()).TryGetProperty("code", out var code) ? code.GetString() : null;

    /// <summary>Waits until an email with the template was delivered to the dev mailbox after <paramref name="sinceUtc"/>.</summary>
    public static async Task<DevMailboxMessage> WaitForEmailAsync(
        this ClassHubApiFixture fixture, string to, string template, DateTime sinceUtc, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        var address = to.Trim().ToLowerInvariant();
        while (true)
        {
            await using (var db = fixture.CreateDbContext())
            {
                var message = await db.DevMailbox.AsNoTracking()
                    .Where(m => m.ToAddress == address && m.Template == template && m.ReceivedAtUtc >= sinceUtc)
                    .OrderByDescending(m => m.ReceivedAtUtc)
                    .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
                if (message is not null)
                {
                    return message;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No '{template}' email for {address} arrived in time.");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    public static async Task<int> CountEmailsAsync(this ClassHubApiFixture fixture, string to, DateTime sinceUtc)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var address = to.Trim().ToLowerInvariant();
        await using var db = fixture.CreateDbContext();
        var delivered = await db.DevMailbox.CountAsync(m => m.ToAddress == address && m.ReceivedAtUtc >= sinceUtc, TestContext.Current.CancellationToken);
        var pending = await db.EmailOutbox.CountAsync(m => m.ToAddress == address && m.CreatedAtUtc >= sinceUtc && m.SentAtUtc == null, TestContext.Current.CancellationToken);
        return delivered + pending;
    }

    /// <summary>Reads a value from the URL fragment of the link in an email (<c>#a=1&amp;b=2</c>).</summary>
    public static string FragmentValue(this DevMailboxMessage message, string name)
    {
        ArgumentNullException.ThrowIfNull(message);
        var match = Regex.Match(message.TextBody, $@"[#&]{Regex.Escape(name)}=([^&\s]+)");
        match.Success.ShouldBeTrue($"Email '{message.Template}' has no '{name}' in its link.");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    public static string UniqueEmail(string prefix = "teacher") => $"{prefix}.{Guid.NewGuid():N}@example.test";
}
