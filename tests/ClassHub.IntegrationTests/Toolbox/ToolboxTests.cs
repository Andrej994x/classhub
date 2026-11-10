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
        all.Tools.Single(t => t.Code == "drawing-board").BuiltInKey.ShouldBe("drawing-board"); // shipped in R3-P3
        all.Tools.Select(t => t.Code).ShouldNotContain("geometry-board"); // a board stays hidden until it ships
        all.Tools.TakeWhile(t => t.IsFeatured).Count().ShouldBeGreaterThanOrEqualTo(3); // featured first

        var geogebra = (await SearchAsync(teacher, "?q=geogebra")).Tools.ShouldHaveSingleItem();
        (geogebra.Kind, geogebra.Category, geogebra.Url).ShouldBe(("External", "Geometry", "https://www.geogebra.org/classic"));
        geogebra.SubjectIds.ShouldContain(Subject("math"));
        geogebra.LicenseNoteMk!.ShouldStartWith("Бесплатно"); // license notes in both UI languages
        geogebra.LicenseNoteEn!.ShouldStartWith("Free");
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

        // A tool for any subject (none listed) is found under every subject filter, next to the subject's own tools.
        var general = await ReadAsync<AdminToolResponse>(await admin.Client.PostAsync("/api/admin/toolbox", Body(NewCode(), word + "Општо", "https://www.geogebra.org/", [])));
        var underPhysics = (await SearchAsync(teacher, $"?q={word}&subjectId={physics}")).Tools.Select(t => t.Id).ToList();
        underPhysics.ShouldBe([created.Tool.Id, general.Tool.Id], ignoreOrder: true);
        await ReadAsync<AdminToolResponse>(await admin.Client.PutAsync($"/api/admin/toolbox/{general.Tool.Id}", Body(null, word + "Општо", "https://www.geogebra.org/", [], version: 1, active: false)));

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

        // A change of the description or license alone is visible in the audit, with the value before it.
        using (var described = await admin.Client.PutAsync($"/api/admin/toolbox/{created.Tool.Id}", new
        {
            nameMk = word, nameEn = word, descriptionMk = "Нов опис", descriptionEn = "New description", kind = "External", category = "Simulations",
            url = "https://phet.colorado.edu/", subjectIds = Array.Empty<Guid>(), licenseNoteMk = "Нова лиценца", licenseNoteEn = "New license", isActive = false, version = 3,
        }))
        {
            described.StatusCode.ShouldBe(HttpStatusCode.OK, await described.Content.ReadAsStringAsync(Ct));
        }

        var details = await db.Set<AuditLogEntry>().Where(e => e.TargetId == code && e.Action == "toolbox.tool.updated").OrderByDescending(e => e.OccurredAtUtc).Select(e => e.DetailsJson).FirstAsync(Ct);
        details.ShouldNotBeNull();
        var text = System.Text.RegularExpressions.Regex.Unescape(details); // JSON escapes non-ASCII text
        text.ShouldContain("Симулации за сили."); // before
        text.ShouldContain("Нов опис"); // after
        text.ShouldContain("CC BY 4.0 (бесплатно)");
        text.ShouldContain("Нова лиценца");
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
        // Kinds and categories by name only: "2" is not a kind (it would skip the board checks).
        await ExpectInvalidAsync(new { code = NewCode(), nameMk = word, nameEn = word, descriptionMk = "о", descriptionEn = "d", kind = "2", category = "Drawing" }, "kind");
        await ExpectInvalidAsync(new { code = NewCode(), nameMk = word, nameEn = word, descriptionMk = "о", descriptionEn = "d", kind = "External", category = "5", url = "https://www.geogebra.org/" }, "category");

        // A board that has not shipped cannot be activated (its page does not exist yet).
        var board = (await admin.Client.GetJsonAsync<List<AdminToolResponse>>("/api/admin/toolbox")).Single(t => t.Tool.Code == "geometry-board");
        using (var activate = await admin.Client.PutAsync($"/api/admin/toolbox/{board.Tool.Id}", new
        {
            nameMk = board.Tool.NameMk, nameEn = board.Tool.NameEn, descriptionMk = board.Tool.DescriptionMk, descriptionEn = board.Tool.DescriptionEn,
            kind = "BuiltIn", category = board.Tool.Category, builtInKey = "geometry-board", subjectIds = board.Tool.SubjectIds, isActive = true, version = board.Version,
        }))
        {
            activate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await activate.ReadJsonAsync()).GetProperty("errors").TryGetProperty("isActive", out _).ShouldBeTrue();
        }

        // Each board has one catalog entry (the seeded one).
        (await admin.Client.PostAsync("/api/admin/toolbox", new { code = NewCode(), nameMk = word, nameEn = word, descriptionMk = "о", descriptionEn = "d", kind = "BuiltIn", category = "Drawing", builtInKey = "drawing-board", isActive = false }))
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
        licenseNoteMk = "CC BY 4.0 (бесплатно)",
        licenseNoteEn = "CC BY 4.0 (free)",
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
