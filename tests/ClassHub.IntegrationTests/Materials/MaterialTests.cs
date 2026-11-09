using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Materials.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Materials;

/// <summary>R2-P8: private materials, the upload allow-list, the storage quota, assignments and the student portal.</summary>
[Collection(ApiCollection.Name)]
public sealed class MaterialTests(ClassHubApiFixture fixture)
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << >>\n%%EOF\n");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/materials{rest}";

    private static string Portal(Guid workspaceId, string rest = "") => $"/api/student/w/{workspaceId}/materials{rest}";

    [Fact]
    public async Task A_teacher_uploads_a_material_and_downloads_it_as_a_safe_attachment()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        var material = await UploadAsync(teacher, "Равенки", "..\\..\\etc/evil<name>:.PDF", PdfBytes, "Вежби за дома");

        material.Title.ShouldBe("Равенки");
        material.Description.ShouldBe("Вежби за дома");
        material.FileName.ShouldBe("evil_name__.pdf"); // last path part, reserved and control characters replaced, own extension
        material.ContentType.ShouldBe("application/pdf");
        material.SizeBytes.ShouldBe(PdfBytes.Length);
        material.Assignments.ShouldBeEmpty();

        using var download = await teacher.Client.GetAsync(Path(teacher, $"/{material.Id}/file"));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(PdfBytes);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("evil_name__.pdf");
        download.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        download.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldStartWith("sandbox");
        download.Headers.CacheControl!.NoStore.ShouldBeTrue();

        var image = await UploadAsync(teacher, "Табела", "tabela.png", PngBytes);
        using var shown = await teacher.Client.GetAsync(Path(teacher, $"/{image.Id}/file"));
        shown.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline"); // only images are shown in the browser

        var list = await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher));
        list.Items.Select(i => i.Id).ShouldBe([image.Id, material.Id]);
        list.StorageUsedBytes.ShouldBe(PdfBytes.Length + PngBytes.Length);
    }

    [Fact]
    public async Task Files_outside_the_allow_list_or_with_a_mismatching_signature_are_rejected()
    {
        using var teacher = await fixture.CreateTeacherAsync();

        foreach (var (name, bytes) in new[]
        {
            ("program.exe", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }),
            ("program.pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }), // an executable renamed to .pdf
            ("page.html", Encoding.UTF8.GetBytes("<script>alert(1)</script>")),
            ("slika.png", PdfBytes), // a PDF named .png
            ("binary.txt", new byte[] { 0x41, 0x00, 0x42 }),
            ("noextension", PdfBytes),
        })
        {
            using var response = await PostUploadAsync(teacher, "Документ", name, bytes);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
            (await response.ErrorCodeAsync()).ShouldBe(ErrorCodes.FileNotAllowed, name);
        }

        using (var empty = await PostUploadAsync(teacher, "Празно", "empty.pdf", []))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var noTitle = await PostUploadAsync(teacher, "  ", "doc.pdf", PdfBytes))
        {
            noTitle.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher))).Total.ShouldBe(0);
        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(0);
    }

    [Fact]
    public async Task Uploads_beyond_the_plan_storage_are_refused_and_leave_nothing_behind()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        // The free plan stores 500 MB; this workspace already uses all but a few bytes of it.
        await using (var db = fixture.CreateDbContext())
        {
            db.Set<StoredFile>().Add(new StoredFile
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = teacher.WorkspaceId,
                Purpose = "material",
                ContentType = "application/pdf",
                SizeBytes = (500L * 1024 * 1024) - 10,
                Sha256 = new string('0', 64),
                StorageKey = $"{teacher.WorkspaceId:N}/materials/{Guid.NewGuid():N}.pdf",
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
        }

        using var blocked = await PostUploadAsync(teacher, "Преголемо", "doc.pdf", PdfBytes);

        blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await blocked.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);
        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher))).Total.ShouldBe(0);
        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(1); // only the seeded one
        Directory.EnumerateFiles(UploadsRoot(teacher.WorkspaceId), "*", SearchOption.AllDirectories).ShouldBeEmpty();

        // Another workspace has its own storage.
        using var other = await fixture.CreateTeacherAsync();
        (await UploadAsync(other, "Мое", "doc.pdf", PdfBytes)).SizeBytes.ShouldBe(PdfBytes.Length);
    }

    [Fact]
    public async Task Deleting_a_material_removes_its_file_and_frees_the_storage()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (student, _) = await StudentAsync(teacher, "Ана");
        var material = await UploadAsync(teacher, "Тест", "test.pdf", PdfBytes);
        material = await AssignAsync(teacher, material.Id, new { studentId = student });

        (await teacher.Client.PostAsync(Path(teacher, $"/{material.Id}/delete"), new { version = material.Version - 1 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var deleted = await teacher.Client.PostAsync(Path(teacher, $"/{material.Id}/delete"), new { version = material.Version }))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await teacher.Client.GetAsync(Path(teacher, $"/{material.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(0);
        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher))).StorageUsedBytes.ShouldBe(0);
        Directory.EnumerateFiles(UploadsRoot(teacher.WorkspaceId), "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Students_open_only_materials_assigned_to_them_or_their_groups()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync(); // the student's account
        var (ana, _) = await StudentAsync(teacher, "Ана", learner.Email);
        await LinkAccountAsync(teacher, ana, learner);
        var group = await GroupAsync(teacher, "Алгебра", ana);

        var direct = await UploadAsync(teacher, "Лично", "licno.pdf", PdfBytes);
        var viaGroup = await UploadAsync(teacher, "За групата", "grupa.png", PngBytes);
        var unassigned = await UploadAsync(teacher, "Само за мене", "moe.pdf", PdfBytes);
        await AssignAsync(teacher, direct.Id, new { studentId = ana });
        var groupAssigned = await AssignAsync(teacher, viaGroup.Id, new { groupId = group.Id });
        groupAssigned.Assignments.ShouldHaveSingleItem().Name.ShouldBe("Алгебра");

        var mine = await learner.Client.GetJsonAsync<List<StudentMaterialResponse>>(Portal(teacher.WorkspaceId));
        mine.Select(m => m.Id).ShouldBe([viaGroup.Id, direct.Id], ignoreOrder: true);
        using (var file = await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{direct.Id}/file")))
        {
            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await file.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(PdfBytes);
            file.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        }

        (await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{viaGroup.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Not assigned: not found, never forbidden.
        (await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{unassigned.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{Guid.NewGuid()}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Leaving the group ends access to the group's materials.
        (await teacher.Client.DeleteAsync($"/api/w/{teacher.WorkspaceId}/groups/{group.Id}/members/{ana}")).IsSuccessStatusCode.ShouldBeTrue();
        (await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{viaGroup.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await learner.Client.GetJsonAsync<List<StudentMaterialResponse>>(Portal(teacher.WorkspaceId))).ShouldHaveSingleItem().Id.ShouldBe(direct.Id);

        // Unassigned or archived: gone from the portal.
        var current = await teacher.Client.GetJsonAsync<MaterialResponse>(Path(teacher, $"/{direct.Id}"));
        var archived = await ReadAsync<MaterialResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{direct.Id}/archive"), new { version = current.Version }));
        archived.Archived.ShouldBeTrue();
        (await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{direct.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await learner.Client.GetJsonAsync<List<StudentMaterialResponse>>(Portal(teacher.WorkspaceId))).ShouldBeEmpty();
        var restored = await ReadAsync<MaterialResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{direct.Id}/restore"), new { version = archived.Version }));
        var unassignedAgain = await ReadAsync<MaterialResponse>(await teacher.Client.DeleteAsync(Path(teacher, $"/{direct.Id}/assignments/{restored.Assignments.Single().Id}")));
        unassignedAgain.Assignments.ShouldBeEmpty();
        (await learner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{direct.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Another student of the same teacher sees nothing of this.
        using var otherLearner = await fixture.CreateTeacherAsync();
        var (boris, _) = await StudentAsync(teacher, "Борис", otherLearner.Email);
        await LinkAccountAsync(teacher, boris, otherLearner);
        (await otherLearner.Client.GetJsonAsync<List<StudentMaterialResponse>>(Portal(teacher.WorkspaceId))).ShouldBeEmpty();
        (await otherLearner.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{viaGroup.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assignments_target_one_active_student_or_group_of_the_workspace_and_repeat_safely()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var (ana, anaVersion) = await StudentAsync(teacher, "Ана");
        var (foreign, _) = await StudentAsync(other, "Туѓ");
        var group = await GroupAsync(teacher, "Геометрија");
        var material = await UploadAsync(teacher, "Тест", "test.pdf", PdfBytes);

        (await teacher.Client.PostAsync(Path(teacher, $"/{material.Id}/assignments"), new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/{material.Id}/assignments"), new { studentId = ana, groupId = group.Id })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/{material.Id}/assignments"), new { studentId = foreign })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Twice, and in parallel: one assignment.
        await AssignAsync(teacher, material.Id, new { studentId = ana });
        var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => teacher.Client.PostAsync(Path(teacher, $"/{material.Id}/assignments"), new { studentId = ana })));
        parallel.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        foreach (var response in parallel)
        {
            response.Dispose();
        }

        var both = await AssignAsync(teacher, material.Id, new { groupId = group.Id });
        both.Assignments.Count.ShouldBe(2);
        both.Assignments.Single(a => a.StudentId == ana).Name.ShouldBe("Ана");
        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher))).Items.ShouldHaveSingleItem().AssignmentCount.ShouldBe(2);

        // Archived students are not given new materials.
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{ana}/archive", new { version = anaVersion })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await UploadAsync(teacher, "Втор", "vtor.pdf", PdfBytes);
        (await teacher.Client.PostAsync(Path(teacher, $"/{second.Id}/assignments"), new { studentId = ana })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Materials_are_invisible_to_other_workspaces_and_to_anonymous_visitors()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var material = await UploadAsync(teacher, "Приватно", "privatno.pdf", PdfBytes);

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher, $"/{material.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher, $"/{material.Id}/file"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, Path(teacher, $"/{material.Id}/archive"), new { version = material.Version });
        // Under one's own route, another workspace's material does not exist.
        (await intruder.Client.GetAsync(Path(intruder, $"/{material.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PutAsync(Path(intruder, $"/{material.Id}"), new { title = "x", version = material.Version })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The teacher has no student record in their own workspace: the portal stays closed.
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(teacher.Client, HttpMethod.Get, Portal(teacher.WorkspaceId));

        using var anonymous = await ApiClient.CreateAsync(fixture);
        (await anonymous.GetAsync(Path(teacher, $"/{material.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(Portal(teacher.WorkspaceId, $"/{material.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Without the antiforgery header an upload is refused like any other unsafe request.
        using var content = Form("Без токен", "doc.pdf", PdfBytes);
        using var forged = await teacher.Client.Http.PostAsync(new Uri(Path(teacher), UriKind.Relative), content, Ct);
        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher))).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Details_change_with_the_version_and_the_file_stays()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var material = await UploadAsync(teacher, "Старо", "doc.pdf", PdfBytes);

        var updated = await ReadAsync<MaterialResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{material.Id}"), new { title = " Ново ", description = "Опис", version = material.Version }));

        updated.Title.ShouldBe("Ново");
        updated.Description.ShouldBe("Опис");
        updated.FileName.ShouldBe("doc.pdf");
        updated.Version.ShouldBe(material.Version + 1);
        (await teacher.Client.PutAsync(Path(teacher, $"/{material.Id}"), new { title = "Пак", version = material.Version })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await teacher.Client.PutAsync(Path(teacher, $"/{material.Id}"), new { title = "", version = updated.Version })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PutAsync(Path(teacher, $"/{material.Id}"), new { title = "x", subjectId = Guid.NewGuid(), version = updated.Version })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher, "?q=Ново"))).Total.ShouldBe(1);
        (await teacher.Client.GetJsonAsync<MaterialListResponse>(Path(teacher, "?q=нема-такво"))).Total.ShouldBe(0);
    }

    private static MultipartFormDataContent Form(string title, string fileName, byte[] bytes, string? description = null)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(title), "title" },
        };
        if (description is not null)
        {
            content.Add(new StringContent(description), "description");
        }

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return content;
    }

    private static async Task<HttpResponseMessage> PostUploadAsync(TeacherSession teacher, string title, string fileName, byte[] bytes, string? description = null)
    {
        using var content = Form(title, fileName, bytes, description);
        return await teacher.Client.SendContentAsync(HttpMethod.Post, Path(teacher), content);
    }

    private static async Task<MaterialResponse> UploadAsync(TeacherSession teacher, string title, string fileName, byte[] bytes, string? description = null)
    {
        using var response = await PostUploadAsync(teacher, title, fileName, bytes, description);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>(Ct))!;
    }

    private static async Task<MaterialResponse> AssignAsync(TeacherSession teacher, Guid materialId, object body) =>
        await ReadAsync<MaterialResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{materialId}/assignments"), body));

    private static async Task<(Guid Id, int Version)> StudentAsync(TeacherSession teacher, string firstName, string? email = null)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName, email });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var student = (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
        return (student.Id, student.Version);
    }

    private static async Task<GroupResponse> GroupAsync(TeacherSession teacher, string name, params Guid[] members)
    {
        var group = await ReadAsync<GroupResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups", new { name }));
        foreach (var member in members)
        {
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups/{group.Id}/members", new { studentId = member })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        return group;
    }

    /// <summary>Invites the student and accepts with the learner's existing account (student:self from then on).</summary>
    private async Task LinkAccountAsync(TeacherSession teacher, Guid studentId, TeacherSession learner)
    {
        var since = DateTime.UtcNow.AddMilliseconds(-5);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{studentId}/invitation", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await fixture.WaitForEmailAsync(learner.Email, "student.invitation", since)).FragmentValue("token");
        using var accept = await learner.Client.PostAsync("/api/student-invitations/accept", new { token });
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync(Ct));
    }

    private async Task<int> StoredFilesAsync(Guid workspaceId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM StoredFiles WHERE WorkspaceId = {workspaceId}").SingleAsync(Ct);
    }

    /// <summary>The workspace's folder in the test uploads root (empty or missing when it stores nothing).</summary>
    private string UploadsRoot(Guid workspaceId)
    {
        var path = System.IO.Path.Combine(fixture.Factory.FileRoot, workspaceId.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }
}
