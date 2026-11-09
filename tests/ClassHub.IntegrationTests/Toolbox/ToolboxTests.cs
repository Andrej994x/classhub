using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Toolbox.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Toolbox;

/// <summary>
/// R3-P1: teachers search the active tools (seeded and staff-added) by text, subject and category; only platform staff
/// add and change tools (official HTTPS links on approved sites only, audited, deactivated never deleted).
/// The catalog is shared by all tests, so each test uses its own codes and search words.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ToolboxTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Teachers_find_the_seeded_tools_by_search_subject_and_category()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var subjects = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects;
        Guid Subject(string code) => subjects.Single(s => s.Code == code).Id;

        var all = await SearchAsync(teacher, "");
        all.Categories.ShouldBe(["Geometry", "Graphing", "Simulations", "Chemistry", "Drawing", "Other"]);
        all.Tools.Select(t => t.Code).ShouldContain("geogebra");
        all.Tools.Select(t => t.Code).ShouldNotContain("drawing-board"); // built-in boards stay inactive until they ship
        all.Tools.TakeWhile(t => t.IsFeatured).Count().ShouldBeGreaterThanOrEqualTo(3); // featured first

        var geogebra = (await SearchAsync(teacher, "?q=geogebra")).Tools.ShouldHaveSingleItem();
        (geogebra.Kind, geogebra.Category, geogebra.Url).ShouldBe(("External", "Geometry", "https://www.geogebra.org/classic"));
        geogebra.SubjectIds.ShouldContain(Subject("math"));
        (await SearchAsync(teacher, "?q=молекули")).Tools.Select(t => t.Code).ShouldContain("molview"); // the Macedonian description

        var chemistry = (await SearchAsync(teacher, $"?subjectId={Subject("chemistry")}")).Tools.Select(t => t.Code).ToList();
        chemistry.ShouldContain("molview");
        chemistry.ShouldContain("phet");
        chemistry.ShouldNotContain("desmos");
        (await SearchAsync(teacher, "?category=Graphing")).Tools.ShouldAllBe(t => t.Category == "Graphing");

        (await teacher.Client.GetAsync($"/api/w/{teacher.WorkspaceId}/toolbox?category=Robots")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetAsync($"/api/w/{teacher.WorkspaceId}/toolbox?q={new string('x', 101)}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_add_change_and_deactivate_tools_and_teachers_see_only_active_ones()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using var teacher = await fixture.CreateTeacherAsync();
        var word = $"Колиба{Guid.NewGuid():N}"[..16];
        var code = $"t-{Guid.NewGuid():N}"[..14];
        var physics = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "physics").Id;

        var created = await ReadAsync<AdminToolResponse>(await admin.Client.PostAsync("/api/admin/toolbox", Body(code, word, "https://phet.colorado.edu/en/simulations/forces", [physics])));
        (created.IsActive, created.Version, created.Tool.SubjectIds.Single()).ShouldBe((true, 1, physics));
        var found = (await SearchAsync(teacher, $"?q={word}")).Tools.ShouldHaveSingleItem();
        (found.Code, found.Url, found.IsFeatured).ShouldBe((code, "https://phet.colorado.edu/en/simulations/forces", false));

        // Featured, then deactivated: teachers no longer see it; staff still do.
        var featured = await ReadAsync<AdminToolResponse>(await admin.Client.PutAsync($"/api/admin/toolbox/{created.Tool.Id}", Body(null, word, "https://phet.colorado.edu/", [physics], version: 1, featured: true)));
        featured.Tool.IsFeatured.ShouldBeTrue();
        using (var stale = await admin.Client.PutAsync($"/api/admin/toolbox/{created.Tool.Id}", Body(null, word, "https://phet.colorado.edu/", [physics], version: 1)))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        }

        await ReadAsync<AdminToolResponse>(await admin.Client.PutAsync($"/api/admin/toolbox/{created.Tool.Id}", Body(null, word, "https://phet.colorado.edu/", [], version: 2, active: false)));
        (await SearchAsync(teacher, $"?q={word}")).Tools.ShouldBeEmpty();
        var listed = (await admin.Client.GetJsonAsync<List<AdminToolResponse>>("/api/admin/toolbox")).Single(t => t.Tool.Id == created.Tool.Id);
        (listed.IsActive, listed.Version, listed.Tool.SubjectIds.Count).ShouldBe((false, 3, 0));

        // The code is permanent and unique; every change is audited.
        (await admin.Client.PutAsync($"/api/admin/toolbox/{created.Tool.Id}", Body("other-code", word, "https://phet.colorado.edu/", [], version: 3))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsync("/api/admin/toolbox", Body(code, word, "https://phet.colorado.edu/", []))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var db = fixture.CreateDbContext();
        (await db.Set<AuditLogEntry>().Where(e => e.TargetId == code && e.Action.StartsWith("toolbox.tool.")).Select(e => e.Action).ToListAsync(Ct))
            .ShouldBe(["toolbox.tool.created", "toolbox.tool.updated", "toolbox.tool.updated"], ignoreOrder: true);
    }

    [Fact]
    public async Task Links_must_be_official_https_links_on_approved_sites_and_boards_must_exist()
    {
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        var word = $"Линк{Guid.NewGuid():N}"[..14];
        async Task ExpectInvalidAsync(object body, string field)
        {
            using var response = await admin.Client.PostAsync("/api/admin/toolbox", body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        await ExpectInvalidAsync(Body(NewCode(), word, "http://www.geogebra.org/", []), "url");
        await ExpectInvalidAsync(Body(NewCode(), word, "https://geogebra.example.com/", []), "url");
        await ExpectInvalidAsync(Body(NewCode(), word, "javascript:alert(1)", []), "url");
        await ExpectInvalidAsync(new { code = NewCode(), nameMk = word, nameEn = word, descriptionMk = "о", descriptionEn = "d", kind = "BuiltIn", category = "Drawing", builtInKey = "chess-board" }, "builtInKey");
        await ExpectInvalidAsync(Body(NewCode(), word, "https://www.geogebra.org/", [Guid.NewGuid()]), "subjectIds");

        // Each board has one catalog entry (the seeded one).
        (await admin.Client.PostAsync("/api/admin/toolbox", new { code = NewCode(), nameMk = word, nameEn = word, descriptionMk = "о", descriptionEn = "d", kind = "BuiltIn", category = "Drawing", builtInKey = "drawing-board" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_platform_staff_manage_tools()
    {
        using var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        using var anonymous = await ApiClient.CreateAsync(fixture);
        var body = Body(NewCode(), "Туѓо", "https://www.geogebra.org/", []);

        (await teacher.Client.PostAsync("/api/admin/toolbox", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.Client.GetAsync("/api/admin/toolbox")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.PostAsync("/api/admin/toolbox", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(teacher.Client, HttpMethod.Get, $"/api/w/{Guid.NewGuid()}/toolbox");
    }

    private static string NewCode() => $"t-{Guid.NewGuid():N}"[..14];

    private static object Body(string? code, string name, string url, Guid[] subjects, int? version = null, bool? featured = null, bool? active = null) => new
    {
        code,
        nameMk = name,
        nameEn = name,
        descriptionMk = "Симулации за сили.",
        descriptionEn = "Force simulations.",
        kind = "External",
        category = "Simulations",
        url,
        subjectIds = subjects,
        licenseNote = "CC BY 4.0",
        isActive = active,
        isFeatured = featured,
        sortOrder = 900,
        version,
    };

    private static async Task<ToolboxResponse> SearchAsync(TeacherSession teacher, string query) =>
        await teacher.Client.GetJsonAsync<ToolboxResponse>($"/api/w/{teacher.WorkspaceId}/toolbox{query}");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }
}
