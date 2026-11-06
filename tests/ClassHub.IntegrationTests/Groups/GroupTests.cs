using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Groups.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Groups;

/// <summary>R2-P2: groups, members (many-to-many), archive; members are students of the same workspace.</summary>
[Collection(ApiCollection.Name)]
public sealed class GroupTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/groups{rest}";

    [Fact]
    public async Task A_teacher_builds_a_group_and_a_student_can_be_in_several_groups()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");

        var algebra = await CreateAsync(teacher, new { name = "Алгебра 8", subjectId = math, description = "Вторник 17ч" });
        var physics = await CreateAsync(teacher, new { name = "Физика 8" });
        algebra = await AddAsync(teacher, algebra.Id, ana.Id);
        algebra = await AddAsync(teacher, algebra.Id, boris.Id);
        await AddAsync(teacher, physics.Id, ana.Id);

        algebra.Members.Select(m => m.FirstName).ShouldBe(["Ана", "Борис"], ignoreOrder: true);
        (await teacher.Client.GetJsonAsync<List<GroupListItem>>(Path(teacher, $"?studentId={ana.Id}"))).Select(g => g.Name).ShouldBe(["Алгебра 8", "Физика 8"]);
        (await teacher.Client.GetJsonAsync<List<GroupListItem>>(Path(teacher))).Single(g => g.Id == algebra.Id).MemberCount.ShouldBe(2);

        // Adding twice changes nothing, and member changes leave the version of the details alone.
        var again = await AddAsync(teacher, algebra.Id, ana.Id);
        again.Members.Count.ShouldBe(2);
        again.Version.ShouldBe(algebra.Version);

        using (var update = await teacher.Client.PutAsync(Path(teacher, $"/{algebra.Id}"), new { name = "Алгебра 8А", subjectId = math, version = algebra.Version }))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await teacher.Client.PutAsync(Path(teacher, $"/{algebra.Id}"), new { name = "Стара", version = algebra.Version })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Removing_a_member_and_archiving_keep_the_records()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var group = await AddAsync(teacher, (await CreateAsync(teacher, new { name = "Група" })).Id, ana.Id);

        using (var remove = await teacher.Client.DeleteAsync(Path(teacher, $"/{group.Id}/members/{ana.Id}")))
        {
            remove.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await remove.Content.ReadFromJsonAsync<GroupResponse>(Ct))!.Members.ShouldBeEmpty();
        }

        await using (var db = fixture.CreateDbContext())
        {
            // The membership row stays (with its end date) for the lesson history.
            (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM GroupMembers WHERE GroupId = {group.Id} AND RemovedAtUtc IS NOT NULL").SingleAsync(Ct)).ShouldBe(1);
        }

        (await teacher.Client.DeleteAsync(Path(teacher, $"/{group.Id}/members/{ana.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var readded = await AddAsync(teacher, group.Id, ana.Id);
        readded.Members.ShouldHaveSingleItem().StudentId.ShouldBe(ana.Id);

        using (var archive = await teacher.Client.PostAsync(Path(teacher, $"/{group.Id}/archive"), new { version = readded.Version }))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.OK);
            var archived = (await archive.Content.ReadFromJsonAsync<GroupResponse>(Ct))!;
            archived.Archived.ShouldBeTrue();
            archived.Members.ShouldHaveSingleItem(); // members kept
        }

        (await teacher.Client.GetJsonAsync<List<GroupListItem>>(Path(teacher))).ShouldNotContain(g => g.Id == group.Id);
        (await teacher.Client.GetJsonAsync<List<GroupListItem>>(Path(teacher, "?archived=true"))).ShouldContain(g => g.Id == group.Id);
        using var blocked = await teacher.Client.PostAsync(Path(teacher, $"/{group.Id}/members"), new { studentId = (await StudentAsync(teacher, "Бојан")).Id });
        blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_student_of_another_workspace_cannot_be_added_and_the_database_rejects_it()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var foreign = await StudentAsync(other, "Туѓ");
        var group = await CreateAsync(teacher, new { name = "Моја група" });

        using (var add = await teacher.Client.PostAsync(Path(teacher, $"/{group.Id}/members"), new { studentId = foreign.Id }))
        {
            add.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await add.ReadJsonAsync()).GetProperty("errors").TryGetProperty("studentId", out _).ShouldBeTrue();
        }

        await using var db = fixture.CreateDbContext();
        var error = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO GroupMembers (WorkspaceId, GroupId, StudentId, AddedAtUtc) VALUES ({teacher.WorkspaceId}, {group.Id}, {foreign.Id}, SYSUTCDATETIME())", Ct));
        error.Number.ShouldBe(547); // composite tenant FK: the student is not in this workspace
    }

    [Fact]
    public async Task Archived_students_are_restored_before_joining_a_group()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{ana.Id}/archive", new { version = ana.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var group = await CreateAsync(teacher, new { name = "Група" });

        using var add = await teacher.Client.PostAsync(Path(teacher, $"/{group.Id}/members"), new { studentId = ana.Id });

        add.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await add.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
    }

    [Fact]
    public async Task Parallel_adds_for_the_last_place_fill_the_group_exactly_once()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var group = await CreateAsync(teacher, new { name = "Голема група" });
        var now = DateTime.UtcNow;
        var students = Enumerable.Range(0, Group.MaxMembers + 2)
            .Select(i => new Student { Id = Guid.CreateVersion7(), WorkspaceId = teacher.WorkspaceId, FirstName = $"Ученик {i}", CreatedAtUtc = now, UpdatedAtUtc = now })
            .ToList();
        await using (var db = fixture.CreateDbContext())
        {
            // Seeded directly: the plan's active-student limit is not what this test is about.
            db.Set<Student>().AddRange(students);
            db.Set<GroupMember>().AddRange(students.Take(Group.MaxMembers - 1)
                .Select(s => new GroupMember { WorkspaceId = teacher.WorkspaceId, GroupId = group.Id, StudentId = s.Id, AddedAtUtc = now }));
            await db.SaveChangesAsync(Ct);
        }

        var responses = await Task.WhenAll(students.Skip(Group.MaxMembers - 1)
            .Select(s => teacher.Client.PostAsync(Path(teacher, $"/{group.Id}/members"), new { studentId = s.Id })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(2);
        (await teacher.Client.GetJsonAsync<GroupResponse>(Path(teacher, $"/{group.Id}"))).Members.Count.ShouldBe(Group.MaxMembers);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Another_teacher_cannot_see_or_change_a_group()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var group = await CreateAsync(owner, new { name = "Тајна група" });

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(owner, $"/{group.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, Path(owner, $"/{group.Id}"), new { name = "Hijacked", version = group.Version });
        (await intruder.Client.GetAsync(Path(intruder, $"/{group.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.Client.GetJsonAsync<GroupResponse>(Path(owner, $"/{group.Id}"))).Name.ShouldBe("Тајна група");
    }

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    private static async Task<GroupResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(Path(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<GroupResponse>(Ct))!;
    }

    private static async Task<GroupResponse> AddAsync(TeacherSession teacher, Guid groupId, Guid studentId)
    {
        using var response = await teacher.Client.PostAsync(Path(teacher, $"/{groupId}/members"), new { studentId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<GroupResponse>(Ct))!;
    }
}
