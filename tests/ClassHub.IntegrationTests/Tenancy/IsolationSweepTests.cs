using System.Net;
using System.Text.RegularExpressions;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Tenancy;

/// <summary>
/// R1-P12 isolation sweep (student portal added in R2-P13): every workspace, admin and student portal endpoint that exists, discovered from the endpoint table (so new
/// endpoints are covered automatically), is denied to anonymous callers, to another teacher (404, nothing revealed), and
/// to teachers on platform routes (403). Complements the per-module isolation tests that check the data stays unchanged.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class IsolationSweepTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\{(?<name>[^}:]+)(:[^}]*)?\}")]
    private static partial Regex RouteParameter();

    /// <summary>A route with the request body format it declares (routing answers 415 to another format before authorization).</summary>
    private sealed record Route(string Method, string Pattern, bool Multipart);

    private List<Route> Routes(string prefix) =>
        [
            .. fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => (e.RoutePattern.RawText ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))
                .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => new Route(m, e.RoutePattern.RawText!,
                    e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true)))
                .Distinct()
                .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal),
        ];

    /// <summary>Fills the route: the workspace given, a random id for every other id, and a valid value for the known ones.</summary>
    private static string Fill(string pattern, Guid workspaceId) =>
        RouteParameter().Replace(pattern, m => m.Groups["name"].Value switch
        {
            "workspaceId" => workspaceId.ToString(),
            "kind" => "subjects",
            "code" => "free",
            _ => Guid.NewGuid().ToString(),
        });

    [Fact]
    public async Task Every_workspace_endpoint_is_invisible_to_another_teacher_and_closed_to_anonymous_callers()
    {
        using var owner = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var intruder = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var anonymous = await ApiClient.CreateAsync(fixture);
        var routes = Routes("/api/w/{workspaceId");
        routes.Count.ShouldBeGreaterThan(30, "the sweep must see the R1 workspace endpoints");

        var leaks = new List<string>();
        foreach (var route in routes)
        {
            var path = Fill(route.Pattern, owner.WorkspaceId);
            using (var response = await SendAsync(intruder.Client, route, path))
            {
                if (response.StatusCode != HttpStatusCode.NotFound || await response.ErrorCodeAsync() != ErrorCodes.NotFound)
                {
                    leaks.Add($"teacher {route.Method} {route.Pattern} → {(int)response.StatusCode}");
                }
            }

            using (var response = await SendAsync(anonymous, route, path))
            {
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    leaks.Add($"anonymous {route.Method} {route.Pattern} → {(int)response.StatusCode}");
                }
            }
        }

        leaks.ShouldBeEmpty();
        // The owner still reaches their own workspace (the sweep did not break the session or the data).
        (await owner.Client.GetAsync($"/api/w/{owner.WorkspaceId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_platform_admin_endpoint_is_closed_to_teachers_and_anonymous_callers()
    {
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var anonymous = await ApiClient.CreateAsync(fixture);
        var routes = Routes("/api/admin");
        routes.Count.ShouldBeGreaterThan(15, "the sweep must see the R1 admin endpoints");

        var leaks = new List<string>();
        foreach (var route in routes)
        {
            var path = Fill(route.Pattern, teacher.WorkspaceId);
            using (var response = await SendAsync(teacher.Client, route, path))
            {
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    leaks.Add($"teacher {route.Method} {route.Pattern} → {(int)response.StatusCode}");
                }
            }

            using (var response = await SendAsync(anonymous, route, path))
            {
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    leaks.Add($"anonymous {route.Method} {route.Pattern} → {(int)response.StatusCode}");
                }
            }
        }

        leaks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_student_portal_endpoint_is_closed_to_other_students_teachers_and_anonymous_callers()
    {
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var student = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var otherTeacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var otherStudent = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var anonymous = await ApiClient.CreateAsync(fixture);
        await LinkAsync(teacher, student, "Ана");
        await LinkAsync(otherTeacher, otherStudent, "Борис"); // a student, but of another teacher
        var routes = Routes("/api/student/w/{workspaceId");
        routes.Count.ShouldBeGreaterThanOrEqualTo(8, "the sweep must see the R2 student portal endpoints");

        var leaks = new List<string>();
        foreach (var route in routes)
        {
            var path = Fill(route.Pattern, teacher.WorkspaceId);
            foreach (var (who, client) in new[] { ("other student", otherStudent.Client), ("teacher", teacher.Client) })
            {
                using var response = await SendAsync(client, route, path);
                if (response.StatusCode != HttpStatusCode.NotFound || await response.ErrorCodeAsync() != ErrorCodes.NotFound)
                {
                    leaks.Add($"{who} {route.Method} {route.Pattern} → {(int)response.StatusCode}");
                }
            }

            using (var response = await SendAsync(anonymous, route, path))
            {
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    leaks.Add($"anonymous {route.Method} {route.Pattern} → {(int)response.StatusCode}");
                }
            }
        }

        leaks.ShouldBeEmpty();
        // The student still reaches their own portal.
        (await student.Client.GetAsync($"/api/student/w/{teacher.WorkspaceId}/lessons")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Links <paramref name="learner"/>'s account as a student of <paramref name="teacher"/> (invitation accepted).</summary>
    private async Task LinkAsync(TeacherSession teacher, TeacherSession learner, string firstName)
    {
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName, email = learner.Email });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var studentId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
        var since = DateTime.UtcNow.AddMilliseconds(-5);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{studentId}/invitation", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await fixture.WaitForEmailAsync(learner.Email, "student.invitation", since)).FragmentValue("token");
        using var accept = await learner.Client.PostAsync("/api/student-invitations/accept", new { token });
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// Unsafe requests carry an empty body in the declared format (JSON, or a small multipart form): authorization must
    /// reject them before any binding or validation.
    /// </summary>
    private static async Task<HttpResponseMessage> SendAsync(ApiClient client, Route route, string path)
    {
        if (route.Multipart)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "photo.png" } };
            return await client.SendContentAsync(new HttpMethod(route.Method), path, form);
        }

        return await client.SendAsync(new HttpMethod(route.Method), path, HttpMethods.IsGet(route.Method) || HttpMethods.IsHead(route.Method) ? null : new { }, withAntiforgery: true);
    }
}
