using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClassHub.Core.Modules.Activities.Contracts;
using ClassHub.Core.Modules.Usage.Contracts;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;

namespace ClassHub.IntegrationTests.Usage;

/// <summary>
/// R3-P6: usage counters per workspace and day (tools opened, activities created and reused). The workspace sees its
/// own numbers; staff see platform totals only; parallel uses never lose a count; nobody is recorded.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UsageTests(ClassHubApiFixture fixture)
{
    private static readonly object Drawing = new
    {
        title = "Агли",
        toolType = "drawing-board",
        schemaVersion = 1,
        document = JsonDocument.Parse("""{"elements":[{"type":"rectangle"}]}""").RootElement,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opens_creations_and_reuses_are_counted_for_the_workspace_only()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var empty = await Usage(teacher); // a new workspace starts empty
        (empty.Days, empty.From, empty.To, empty.Tools.Count, empty.Boards.Count).ShouldBe((30, Today().AddDays(-29), Today(), 0, 0));

        foreach (var code in new[] { "geogebra", "geogebra", "drawing-board" })
        {
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/toolbox/{code}/open", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var activity = await ReadAsync<ActivityResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/activities", Drawing));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/activities/{activity.Id}/duplicate", new { })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/activities/{activity.Id}/open", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var usage = await Usage(teacher);
        usage.Tools.ShouldBe([new ToolUsage("geogebra", 2), new ToolUsage("drawing-board", 1)]);
        usage.Boards.ShouldBe([new BoardUsage("drawing-board", 1, 2)]); // created once; duplicated and reopened

        // Only tools teachers can open are counted; the activity must be the workspace's own.
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/toolbox/no-such-tool/open", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.PostAsync($"/api/w/{other.WorkspaceId}/activities/{activity.Id}/open", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacher.Client.GetAsync($"/api/w/{teacher.WorkspaceId}/usage?days=14")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Another workspace sees none of it and cannot read this one's numbers.
        var theirs = await Usage(other);
        (theirs.Tools.Count, theirs.Boards.Count).ShouldBe((0, 0));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(other.Client, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/usage");
    }

    [Fact]
    public async Task Parallel_opens_are_all_counted()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/toolbox/phet/open", new { })));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.NoContent);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        (await Usage(teacher)).Tools.ShouldBe([new ToolUsage("phet", 12)]);
    }

    [Fact]
    public async Task Staff_see_platform_totals_only_and_teachers_cannot()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/toolbox/molview/open", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await admin.Client.GetAsync("/api/admin/usage?days=7");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain(teacher.WorkspaceId.ToString(), Case.Insensitive); // totals, never a workspace
        var platform = JsonSerializer.Deserialize<PlatformUsageResponse>(body, JsonSerializerOptions.Web)!;
        platform.Workspaces.ShouldBeGreaterThanOrEqualTo(1);
        platform.Tools.Single(t => t.Code == "molview").Opens.ShouldBeGreaterThanOrEqualTo(1);
        platform.Daily.Count.ShouldBe(7);
        platform.Daily[^1].Day.ShouldBe(Today());
        platform.Daily[^1].ToolOpens.ShouldBeGreaterThanOrEqualTo(1);

        (await teacher.Client.GetAsync("/api/admin/usage")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static Task<WorkspaceUsageResponse> Usage(TeacherSession teacher) =>
        teacher.Client.GetJsonAsync<WorkspaceUsageResponse>($"/api/w/{teacher.WorkspaceId}/usage");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }
}
