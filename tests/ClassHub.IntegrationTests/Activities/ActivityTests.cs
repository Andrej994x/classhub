using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClassHub.Core.Modules.Activities.Contracts;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Activities;

/// <summary>
/// R3-P2: the Activity Library. Save, find, open, change (versions), duplicate and delete own activities; documents are
/// JSON objects in a supported board format and within the size limit; the plan limit holds under parallel saves;
/// another workspace sees nothing.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ActivityTests(ClassHubApiFixture fixture)
{
    private static readonly JsonElement Scene = JsonDocument.Parse("""{"elements":[{"type":"rectangle","x":10,"y":20,"label":"Триаголник"}],"appState":{"grid":true}}""").RootElement;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/activities{rest}";

    [Fact]
    public async Task A_teacher_saves_finds_opens_changes_duplicates_and_deletes_an_activity()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;

        var saved = await ReadAsync<ActivityResponse>(await teacher.Client.PostAsync(Path(teacher), Body("Агли во триаголник", math, Scene)));
        (saved.ToolType, saved.SchemaVersion, saved.Version, saved.SubjectId).ShouldBe(("drawing-board", 1, 1, math));
        saved.Document.GetRawText().ShouldBe(Scene.GetRawText()); // the document round-trips unchanged
        await ReadAsync<ActivityResponse>(await teacher.Client.PostAsync(Path(teacher), Body("Кругови", null, Scene, tool: "geometry-board")));

        var found = await teacher.Client.GetJsonAsync<ActivityListResponse>(Path(teacher, "?q=триаголник"));
        found.Items.ShouldHaveSingleItem().Id.ShouldBe(saved.Id);
        (await teacher.Client.GetJsonAsync<ActivityListResponse>(Path(teacher, $"?subjectId={math}"))).Total.ShouldBe(1);
        (await teacher.Client.GetJsonAsync<ActivityListResponse>(Path(teacher, "?toolType=geometry-board"))).Items.ShouldHaveSingleItem().Title.ShouldBe("Кругови");

        // Change it with the loaded version; a stale version is a conflict; the board never changes.
        var changedScene = JsonDocument.Parse("""{"elements":[],"appState":{"grid":false}}""").RootElement;
        var changed = await ReadAsync<ActivityResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{saved.Id}"), Body("Агли — повторување", math, changedScene, version: 1)));
        (changed.Version, changed.Title).ShouldBe((2, "Агли — повторување"));
        (await teacher.Client.GetJsonAsync<ActivityResponse>(Path(teacher, $"/{saved.Id}"))).Document.GetRawText().ShouldBe(changedScene.GetRawText());
        using (var stale = await teacher.Client.PutAsync(Path(teacher, $"/{saved.Id}"), Body("Старо", math, Scene, version: 1)))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        }

        (await teacher.Client.PutAsync(Path(teacher, $"/{saved.Id}"), Body("Друга табла", math, Scene, version: 2, tool: "geometry-board"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Duplicate: a new activity with the same document.
        var copy = await ReadAsync<ActivityResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{saved.Id}/duplicate"), new { }));
        (copy.Title, copy.Version, copy.Document.GetRawText()).ShouldBe(("Агли — повторување (копија)", 1, changedScene.GetRawText()));

        // Delete (soft): gone from the library and from opening.
        (await teacher.Client.PostAsync(Path(teacher, $"/{saved.Id}/delete"), new { version = 1 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await teacher.Client.PostAsync(Path(teacher, $"/{saved.Id}/delete"), new { version = 2 })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await teacher.Client.GetAsync(Path(teacher, $"/{saved.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacher.Client.GetJsonAsync<ActivityListResponse>(Path(teacher))).Items.Select(i => i.Id).ShouldBe([copy.Id, (await teacher.Client.GetJsonAsync<ActivityListResponse>(Path(teacher, "?toolType=geometry-board"))).Items[0].Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Documents_must_be_json_objects_in_a_supported_format_within_the_size_limit()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        async Task ExpectInvalidAsync(TeacherSession who, object body, string field)
        {
            using var response = await who.Client.PostAsync(Path(who), body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(Ct));
            (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        await ExpectInvalidAsync(teacher, Body("Низа", null, JsonDocument.Parse("[1,2,3]").RootElement), "document");
        await ExpectInvalidAsync(teacher, Body("Број", null, JsonDocument.Parse("42").RootElement), "document");
        await ExpectInvalidAsync(teacher, new { title = "Без документ", toolType = "drawing-board", schemaVersion = 1 }, "document");
        await ExpectInvalidAsync(teacher, Body("Идна верзија", null, Scene, schemaVersion: 9), "schemaVersion");
        await ExpectInvalidAsync(teacher, Body("Шах", null, Scene, tool: "chess-board"), "toolType");
        await ExpectInvalidAsync(teacher, Body("", null, Scene), "title");
        await ExpectInvalidAsync(teacher, Body("Непознат предмет", Guid.NewGuid(), Scene), "subjectId");

        // The size limit (lowered here to 100 bytes): a larger document is refused, a smaller one saved.
        using var small = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Activities:MaxDocumentBytes", "100"));
        using var limited = await ApiClient.CreateAsync(small);
        using (var login = await limited.PostAsync("/api/auth/login", new { email = teacher.Email, password = teacher.Password }))
        {
            login.IsSuccessStatusCode.ShouldBeTrue();
        }

        var big = JsonDocument.Parse($$"""{"text":"{{new string('a', 200)}}"}""").RootElement;
        using (var refused = await limited.PostAsync(Path(teacher), Body("Големо", null, big)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.ReadJsonAsync()).GetProperty("errors").TryGetProperty("document", out _).ShouldBeTrue();
        }

        (await limited.PostAsync(Path(teacher), Body("Мало", null, JsonDocument.Parse("""{"text":"ok"}""").RootElement))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_free_plan_limit_holds_under_parallel_saves_and_deleting_frees_a_place()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 13).Select(i => teacher.Client.PostAsync(Path(teacher), Body($"Активност {i}", null, Scene))));
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var refused = responses.Where(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ToList();
        (created, refused.Count).ShouldBe((10, 3)); // Free: 10 activities (D-29)
        foreach (var response in refused)
        {
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);
        }

        var first = (await teacher.Client.GetJsonAsync<ActivityListResponse>(Path(teacher))).Items[0];
        (await teacher.Client.PostAsync(Path(teacher, $"/{first.Id}/duplicate"), new { })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await teacher.Client.PostAsync(Path(teacher, $"/{first.Id}/delete"), new { version = first.Version })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await teacher.Client.PostAsync(Path(teacher), Body("На ослободеното место", null, Scene))).StatusCode.ShouldBe(HttpStatusCode.Created);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Activities_are_invisible_to_other_workspaces()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var saved = await ReadAsync<ActivityResponse>(await owner.Client.PostAsync(Path(owner), Body("Мое", null, Scene)));

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(owner, $"/{saved.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, Path(owner, $"/{saved.Id}/duplicate"), new { });
        // Under their own workspace route the id does not exist either.
        (await intruder.Client.GetAsync(Path(intruder, $"/{saved.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PutAsync(Path(intruder, $"/{saved.Id}"), Body("Туѓо", null, Scene, version: 1))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PostAsync(Path(intruder, $"/{saved.Id}/delete"), new { version = 1 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.GetJsonAsync<ActivityListResponse>(Path(intruder))).Total.ShouldBe(0);
        (await owner.Client.GetJsonAsync<ActivityResponse>(Path(owner, $"/{saved.Id}"))).Version.ShouldBe(1);
    }

    private static object Body(string title, Guid? subjectId, JsonElement document, int? version = null, string tool = "drawing-board", int schemaVersion = 1) =>
        new { title, subjectId, toolType = tool, schemaVersion, document, version };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }
}
