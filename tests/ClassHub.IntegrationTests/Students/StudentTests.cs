using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Students;

/// <summary>R2-P1: student records, email dedupe, idempotent lead conversion, the Free active-student limit, notes.</summary>
[Collection(ApiCollection.Name)]
public sealed class StudentTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/students{rest}";

    [Fact]
    public async Task A_teacher_adds_edits_archives_and_restores_a_minor_student_without_a_login()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;

        var created = await CreateAsync(teacher, new
        {
            firstName = "Марко",
            lastName = "Стојанов",
            email = "Marko.S@Example.test",
            isMinor = true,
            guardianName = "Весна Стојанова",
            guardianPhone = "+389 70 123 456",
            subjectIds = new[] { math },
        });
        created.Email.ShouldBe("marko.s@example.test");
        created.IsMinor.ShouldBeTrue();
        created.SubjectIds.ShouldBe([math]);

        using (var update = await teacher.Client.PutAsync(Path(teacher, $"/{created.Id}"), new { firstName = "Марко", lastName = "Стојановски", email = created.Email, isMinor = true, guardianName = "Весна", subjectIds = Array.Empty<Guid>(), version = created.Version }))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await teacher.Client.PutAsync(Path(teacher, $"/{created.Id}"), new { firstName = "Стара", version = created.Version })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var current = await teacher.Client.GetJsonAsync<StudentResponse>(Path(teacher, $"/{created.Id}"));
        current.LastName.ShouldBe("Стојановски");
        current.SubjectIds.ShouldBeEmpty();
        using (var archive = await teacher.Client.PostAsync(Path(teacher, $"/{created.Id}/archive"), new { version = current.Version }))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await archive.Content.ReadFromJsonAsync<StudentResponse>(Ct))!.Archived.ShouldBeTrue();
        }

        (await teacher.Client.GetJsonAsync<StudentListResponse>(Path(teacher))).Items.ShouldNotContain(s => s.Id == created.Id);
        (await teacher.Client.GetJsonAsync<StudentListResponse>(Path(teacher, "?archived=true"))).Items.ShouldContain(s => s.Id == created.Id);
        var archived = await teacher.Client.GetJsonAsync<StudentResponse>(Path(teacher, $"/{created.Id}"));
        using (var restore = await teacher.Client.PostAsync(Path(teacher, $"/{created.Id}/restore"), new { version = archived.Version }))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await teacher.Client.GetJsonAsync<StudentListResponse>(Path(teacher, "?q=Стојановски"))).Items.ShouldHaveSingleItem().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Another_teacher_cannot_see_or_change_a_student()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var student = await CreateAsync(owner, new { firstName = "Ана" });

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(owner, $"/{student.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, Path(owner, $"/{student.Id}"), new { firstName = "Hijacked", version = student.Version });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(owner, $"/{student.Id}/notes"));
        // Under their own workspace the id does not exist either.
        (await intruder.Client.GetAsync(Path(intruder, $"/{student.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.Client.GetJsonAsync<StudentResponse>(Path(owner, $"/{student.Id}"))).FirstName.ShouldBe("Ана");
    }

    [Fact]
    public async Task Emails_are_unique_among_a_teachers_students_including_archived_ones()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var first = await CreateAsync(teacher, new { firstName = "Ева", email = "eva@example.test" });
        (await teacher.Client.PostAsync(Path(teacher, $"/{first.Id}/archive"), new { version = first.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var duplicate = await teacher.Client.PostAsync(Path(teacher), new { firstName = "Ева 2", email = " EVA@example.test " });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe(ErrorCodes.StudentEmailTaken);
        // Another teacher's records are separate.
        (await CreateAsync(other, new { firstName = "Ева", email = "eva@example.test" })).Email.ShouldBe("eva@example.test");
    }

    [Fact]
    public async Task Converting_a_request_creates_the_student_once_and_links_later_requests_by_email()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var leadId = await AddLeadAsync(teacher, "Јована Петрова Илиевска", "Jovana@Example.test", "+389 71 000 111");

        using (var first = await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            var result = (await first.Content.ReadFromJsonAsync<ConvertLeadResponse>(Ct))!;
            result.Created.ShouldBeTrue();
            result.Student.ShouldSatisfyAllConditions(
                s => s.FirstName.ShouldBe("Јована"),
                s => s.LastName.ShouldBe("Петрова Илиевска"),
                s => s.Email.ShouldBe("jovana@example.test"),
                s => s.Phone.ShouldBe("+389 71 000 111"),
                s => s.SourceLeadId.ShouldBe(leadId));
        }

        var lead = await teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{teacher.WorkspaceId}/leads/{leadId}");
        lead.Status.ShouldBe("Converted");
        lead.StudentId.ShouldNotBeNull();

        // Again: idempotent.
        using (var again = await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId }))
        {
            var result = (await again.Content.ReadFromJsonAsync<ConvertLeadResponse>(Ct))!;
            result.Created.ShouldBeFalse();
            result.Student.Id.ShouldBe(lead.StudentId!.Value);
        }

        // A later request from the same address joins the same student.
        var second = await AddLeadAsync(teacher, "Јована", "JOVANA@example.test", null);
        using (var linked = await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId = second }))
        {
            var result = (await linked.Content.ReadFromJsonAsync<ConvertLeadResponse>(Ct))!;
            result.Created.ShouldBeFalse();
            result.Student.Id.ShouldBe(lead.StudentId!.Value);
        }

        (await teacher.Client.GetJsonAsync<StudentListResponse>(Path(teacher))).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_database_keeps_students_and_requests_in_one_workspace()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var foreignLead = await AddLeadAsync(other, "Туѓ", "foreign@example.test", null);
        var student = await CreateAsync(teacher, new { firstName = "Сопствен" });
        var otherStudent = await CreateAsync(other, new { firstName = "Туѓ ученик" });
        var ownLead = await AddLeadAsync(teacher, "Сопствено барање", "own@example.test", null);

        await using var db = fixture.CreateDbContext();
        var sourceError = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE Students SET SourceLeadId = {foreignLead} WHERE Id = {student.Id}", Ct));
        sourceError.Number.ShouldBe(547); // composite tenant FK: a request of another workspace
        var leadError = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE Leads SET StudentId = {otherStudent.Id} WHERE Id = {ownLead}", Ct));
        leadError.Number.ShouldBe(547); // composite tenant FK: a student of another workspace
    }

    [Fact]
    public async Task A_closed_request_is_reopened_before_it_becomes_a_student()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var leadId = await AddLeadAsync(teacher, "Затворено Барање", "closed@example.test", null, LeadStatus.Closed);

        using (var blocked = await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId }))
        {
            blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await blocked.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        (await teacher.Client.GetJsonAsync<StudentListResponse>(Path(teacher))).Items.ShouldBeEmpty();
        var lead = await teacher.Client.GetJsonAsync<LeadResponse>($"/api/w/{teacher.WorkspaceId}/leads/{leadId}");
        (await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/leads/{leadId}/status", new { status = "Contacted", version = lead.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_converted_phone_number_in_the_request_format_stays_valid_on_edit()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var leadId = await AddLeadAsync(teacher, "Ивана Тасева", "ivana@example.test", "070/123.456");
        using var converted = await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId });
        var student = (await converted.Content.ReadFromJsonAsync<ConvertLeadResponse>(Ct))!.Student;
        student.Phone.ShouldBe("070/123.456");

        using var edit = await teacher.Client.PutAsync(Path(teacher, $"/{student.Id}"), new { firstName = "Ивана", lastName = "Тасевска", email = student.Email, phone = student.Phone, version = student.Version });

        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_double_submitted_conversion_creates_exactly_one_student()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var leadId = await AddLeadAsync(teacher, "Давид Ристов", "david@example.test", null);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId })));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ConvertLeadResponse>(Ct)));
        results.Select(r => r!.Student.Id).Distinct().ShouldHaveSingleItem();
        results.Count(r => r!.Created).ShouldBe(1);
        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Students WHERE WorkspaceId = {teacher.WorkspaceId}").SingleAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task The_free_plan_limits_active_students_and_parallel_adds_cannot_exceed_it()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var students = new List<StudentResponse>();
        for (var i = 0; i < 9; i++)
        {
            students.Add(await CreateAsync(teacher, new { firstName = $"Ученик {i}" }));
        }

        // Three adds at once for the last free place: exactly one gets it.
        var parallel = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => teacher.Client.PostAsync(Path(teacher), new { firstName = $"Паралелен {i}" })));
        parallel.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        parallel.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(2);

        using (var blocked = await teacher.Client.PostAsync(Path(teacher), new { firstName = "Единаесетти" }))
        {
            blocked.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await blocked.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);
        }

        var newLead = await AddLeadAsync(teacher, "Нов Ученик", "new@example.test", null);
        (await teacher.Client.PostAsync(Path(teacher, "/from-lead"), new { leadId = newLead })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Archived students do not count; restoring one counts again.
        var archived = students[0];
        (await teacher.Client.PostAsync(Path(teacher, $"/{archived.Id}/archive"), new { version = archived.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CreateAsync(teacher, new { firstName = "На местото" })).Archived.ShouldBeFalse();
        var reloaded = await teacher.Client.GetJsonAsync<StudentResponse>(Path(teacher, $"/{archived.Id}"));
        using (var restore = await teacher.Client.PostAsync(Path(teacher, $"/{archived.Id}/restore"), new { version = reloaded.Version }))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await restore.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);
        }

        (await teacher.Client.GetJsonAsync<StudentListResponse>(Path(teacher))).ActiveCount.ShouldBe(10);
    }

    [Fact]
    public async Task Private_notes_have_their_own_endpoint_and_are_not_part_of_the_student()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var student = await CreateAsync(teacher, new { firstName = "Сара" });
        const string secret = "Има тешкотии со читање — да се разговара со родителот.";

        using (var add = await teacher.Client.PostAsync(Path(teacher, $"/{student.Id}/notes"), new { text = secret }))
        {
            add.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await teacher.Client.PostAsync(Path(teacher, $"/{student.Id}/notes"), new { text = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetJsonAsync<List<StudentNoteResponse>>(Path(teacher, $"/{student.Id}/notes"))).ShouldHaveSingleItem().Text.ShouldBe(secret);
        (await (await teacher.Client.GetAsync(Path(teacher, $"/{student.Id}"))).Content.ReadAsStringAsync(Ct)).ShouldNotContain(secret);
        (await (await teacher.Client.GetAsync(Path(teacher))).Content.ReadAsStringAsync(Ct)).ShouldNotContain(secret);
    }

    [Theory]
    [InlineData("""{"firstName":""}""", "firstName")]
    [InlineData("""{"firstName":"A","email":"not-an-email"}""", "email")]
    [InlineData("""{"firstName":"A","phone":"call me"}""", "phone")]
    [InlineData("""{"firstName":"A","guardianEmail":"x@"}""", "guardianEmail")]
    public async Task Student_fields_are_validated(string body, string field)
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await teacher.Client.SendContentAsync(HttpMethod.Post, Path(teacher), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
    }

    private static async Task<StudentResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(Path(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    /// <summary>A request as the public form would store it (arranged in the database; the intake is tested in LeadTests).</summary>
    private async Task<Guid> AddLeadAsync(TeacherSession teacher, string name, string email, string? phone, LeadStatus status = LeadStatus.New)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var lead = new Lead
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = teacher.WorkspaceId,
            Type = LeadType.Message,
            Status = status,
            Name = name,
            Email = email.ToLowerInvariant(),
            Phone = phone,
            Message = "Здраво",
            ConsentVersion = "test",
            ConsentedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Set<Lead>().Add(lead);
        await db.SaveChangesAsync(Ct);
        return lead.Id;
    }
}
