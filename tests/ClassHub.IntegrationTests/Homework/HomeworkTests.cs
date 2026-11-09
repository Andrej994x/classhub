using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Homework;

/// <summary>R2-P9: homework, per-student statuses, answers with files, the review, resubmission and late rules (D-41).</summary>
[Collection(ApiCollection.Name)]
public sealed class HomeworkTests(ClassHubApiFixture fixture)
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << >>\n%%EOF\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/homework{rest}";

    private static string Portal(Guid workspaceId, string rest = "") => $"/api/student/w/{workspaceId}/homework{rest}";

    [Fact]
    public async Task A_student_submits_with_a_file_resubmits_and_the_teacher_reviews()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        var homework = await CreateAsync(teacher, new { title = "Равенки", description = "Задачи 1–5", dueDate = "2099-12-31" });
        var assigned = await AssignAsync(teacher, homework.Id, new { studentIds = new[] { ana } });
        var assignment = assigned.Assignments.ShouldHaveSingleItem();
        assignment.Status.ShouldBe("Assigned");

        var mine = await learner.Client.GetJsonAsync<List<StudentHomeworkResponse>>(Portal(teacher.WorkspaceId));
        mine.ShouldHaveSingleItem().Title.ShouldBe("Равенки");
        mine[0].DueDate.ShouldBe(new DateOnly(2099, 12, 31));

        var submitted = await SubmitAsync(learner, teacher.WorkspaceId, assignment.Id, "Решение во прилог.", ("reshenie.pdf", PdfBytes));
        submitted.Status.ShouldBe("Submitted");
        submitted.Late.ShouldBeFalse();
        submitted.File.ShouldNotBeNull().FileName.ShouldBe("reshenie.pdf");
        submitted.TeacherComment.ShouldBeNull();

        // Resubmitting before the review replaces the answer; without a new file the old one stays.
        var resubmitted = await SubmitAsync(learner, teacher.WorkspaceId, assignment.Id, "Поправено решение.", null);
        resubmitted.AnswerText.ShouldBe("Поправено решение.");
        resubmitted.File.ShouldNotBeNull().FileName.ShouldBe("reshenie.pdf");
        resubmitted.Version.ShouldBeGreaterThan(submitted.Version);

        // The teacher sees the answer and its file.
        var forTeacher = (await teacher.Client.GetJsonAsync<HomeworkResponse>(Path(teacher, $"/{homework.Id}"))).Assignments.Single();
        forTeacher.AnswerText.ShouldBe("Поправено решение.");
        using (var file = await teacher.Client.GetAsync(Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}/file")))
        {
            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await file.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(PdfBytes);
            file.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
            file.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
        }

        // A review of an older version (the student resubmitted since) is a conflict.
        (await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}/review"), new { comment = "x", version = submitted.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var reviewed = await ReadAsync<HomeworkResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}/review"),
            new { comment = "Одлично, само провери ја задачата 4.", version = forTeacher.Version }));
        reviewed.Assignments.Single().Status.ShouldBe("Reviewed");

        var final = await learner.Client.GetJsonAsync<StudentHomeworkResponse>(Portal(teacher.WorkspaceId, $"/{assignment.Id}"));
        final.Status.ShouldBe("Reviewed");
        final.TeacherComment.ShouldBe("Одлично, само провери ја задачата 4.");

        // Reviewed is final for the student.
        using var late = await PostSubmissionAsync(learner, teacher.WorkspaceId, assignment.Id, "Уште една верзија", null);
        late.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await late.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
    }

    [Fact]
    public async Task Answers_after_the_due_day_are_accepted_and_flagged_late()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        var overdue = await CreateAsync(teacher, new { title = "Минато", dueDate = "2020-01-31" });
        var open = await CreateAsync(teacher, new { title = "Без рок" });
        var lateAssignment = (await AssignAsync(teacher, overdue.Id, new { studentIds = new[] { ana } })).Assignments.Single();
        var openAssignment = (await AssignAsync(teacher, open.Id, new { studentIds = new[] { ana } })).Assignments.Single();
        overdue.DueAtUtc.ShouldBe(new DateTime(2020, 1, 31, 23, 0, 0, DateTimeKind.Utc)); // end of the day in Europe/Skopje

        (await SubmitAsync(learner, teacher.WorkspaceId, lateAssignment.Id, "Задоцнето", null)).Late.ShouldBeTrue();
        (await SubmitAsync(learner, teacher.WorkspaceId, openAssignment.Id, "Без рок", null)).Late.ShouldBeFalse();
        (await teacher.Client.GetJsonAsync<HomeworkResponse>(Path(teacher, $"/{overdue.Id}"))).Assignments.Single().Late.ShouldBeTrue();

        // Moving the due date later clears the flag (late is always against the current due date).
        var current = await teacher.Client.GetJsonAsync<HomeworkResponse>(Path(teacher, $"/{overdue.Id}"));
        await ReadAsync<HomeworkResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{overdue.Id}"), new { title = "Минато", dueDate = "2099-01-31", version = current.Version }));
        (await learner.Client.GetJsonAsync<StudentHomeworkResponse>(Portal(teacher.WorkspaceId, $"/{lateAssignment.Id}"))).Late.ShouldBeFalse();
    }

    [Fact]
    public async Task Students_reach_only_their_own_assignments()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var anaAccount = await fixture.CreateTeacherAsync();
        using var borisAccount = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, anaAccount, "Ана");
        await LinkedStudentAsync(teacher, borisAccount, "Борис");
        var homework = await CreateAsync(teacher, new { title = "Само за Ана" });
        var assignment = (await AssignAsync(teacher, homework.Id, new { studentIds = new[] { ana } })).Assignments.Single();
        await SubmitAsync(anaAccount, teacher.WorkspaceId, assignment.Id, "Мое", ("moe.pdf", PdfBytes));

        (await borisAccount.Client.GetJsonAsync<List<StudentHomeworkResponse>>(Portal(teacher.WorkspaceId))).ShouldBeEmpty();
        (await borisAccount.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{assignment.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await borisAccount.Client.GetAsync(Portal(teacher.WorkspaceId, $"/{assignment.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var foreign = await PostSubmissionAsync(borisAccount, teacher.WorkspaceId, assignment.Id, "Туѓо", null))
        {
            foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // Ana's answer is unchanged; the student's own routes and the teacher's routes stay apart.
        (await anaAccount.Client.GetJsonAsync<StudentHomeworkResponse>(Portal(teacher.WorkspaceId, $"/{assignment.Id}"))).AnswerText.ShouldBe("Мое");
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(anaAccount.Client, HttpMethod.Get, Path(teacher, $"/{homework.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(anaAccount.Client, HttpMethod.Get, Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}/file"));

        // Another teacher: nothing of this workspace, and not under their own route either.
        using var intruder = await fixture.CreateTeacherAsync();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher, $"/{homework.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}/review"), new { version = 1 });
        (await intruder.Client.GetAsync(Path(intruder, $"/{homework.Id}/assignments/{assignment.Id}/file"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PostAsync(Path(intruder, $"/{homework.Id}/assignments"), new { studentIds = new[] { ana } })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var anonymous = await ApiClient.CreateAsync(fixture);
        (await anonymous.GetAsync(Portal(teacher.WorkspaceId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Archived homework leaves the portal and takes no answers.
        var current = await teacher.Client.GetJsonAsync<HomeworkResponse>(Path(teacher, $"/{homework.Id}"));
        await ReadAsync<HomeworkResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/archive"), new { version = current.Version }));
        (await anaAccount.Client.GetJsonAsync<List<StudentHomeworkResponse>>(Portal(teacher.WorkspaceId))).ShouldBeEmpty();
        using var afterArchive = await PostSubmissionAsync(anaAccount, teacher.WorkspaceId, assignment.Id, "После архивирање", null);
        afterArchive.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Answer_files_follow_the_allow_list_and_the_storage_quota_and_replace_each_other()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        var homework = await CreateAsync(teacher, new { title = "Со датотека" });
        var assignment = (await AssignAsync(teacher, homework.Id, new { studentIds = new[] { ana } })).Assignments.Single();

        using (var exe = await PostSubmissionAsync(learner, teacher.WorkspaceId, assignment.Id, null, ("virus.pdf", [0x4D, 0x5A, 0x90, 0x00])))
        {
            exe.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await exe.ErrorCodeAsync()).ShouldBe(ErrorCodes.FileNotAllowed);
        }

        using (var empty = await PostSubmissionAsync(learner, teacher.WorkspaceId, assignment.Id, "   ", null))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // neither text nor a file
        }

        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(0);

        var first = await SubmitAsync(learner, teacher.WorkspaceId, assignment.Id, null, ("prva.pdf", PdfBytes));
        var second = await SubmitAsync(learner, teacher.WorkspaceId, assignment.Id, null, ("vtora.pdf", PdfBytes));
        second.File!.FileName.ShouldBe("vtora.pdf");
        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(1); // the first file was replaced
        Directory.EnumerateFiles(HomeworkRoot(teacher.WorkspaceId)).Count().ShouldBe(1);

        // Removing the file needs text in its place.
        using (var nothingLeft = await PostSubmissionAsync(learner, teacher.WorkspaceId, assignment.Id, null, null, removeFile: true))
        {
            nothingLeft.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var textOnly = await SubmitAsync(learner, teacher.WorkspaceId, assignment.Id, "Само текст", null, removeFile: true);
        textOnly.File.ShouldBeNull();
        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(0);
        Directory.EnumerateFiles(HomeworkRoot(teacher.WorkspaceId)).ShouldBeEmpty();
        first.Version.ShouldBeLessThan(textOnly.Version);

        // The teacher's plan storage is full: the answer file is refused and nothing is left behind.
        await AddStoredBytesAsync(teacher.WorkspaceId, (500L * 1024 * 1024) - 10);
        using var full = await PostSubmissionAsync(learner, teacher.WorkspaceId, assignment.Id, null, ("treta.pdf", PdfBytes));
        full.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await full.ErrorCodeAsync()).ShouldBe(ErrorCodes.PlanLimitReached);
        (await StoredFilesAsync(teacher.WorkspaceId)).ShouldBe(1); // only the filler
        Directory.EnumerateFiles(HomeworkRoot(teacher.WorkspaceId)).ShouldBeEmpty();
        (await learner.Client.GetJsonAsync<StudentHomeworkResponse>(Portal(teacher.WorkspaceId, $"/{assignment.Id}"))).AnswerText.ShouldBe("Само текст");
    }

    [Fact]
    public async Task Groups_assign_their_current_members_and_assigning_again_changes_nothing()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var mila = await StudentAsync(teacher, "Мила");
        var foreign = await StudentAsync(other, "Туѓ");
        var group = await GroupAsync(teacher, "Алгебра", ana.Id, boris.Id);
        var homework = await CreateAsync(teacher, new { title = "За групата" });

        (await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments"), new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments"), new { studentIds = new[] { foreign.Id } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var byGroup = await AssignAsync(teacher, homework.Id, new { groupId = group.Id, studentIds = new[] { mila.Id } });
        byGroup.Assignments.Select(a => a.StudentName).ShouldBe(["Ана", "Борис", "Мила"]);

        var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments"), new { groupId = group.Id })));
        parallel.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        foreach (var response in parallel)
        {
            response.Dispose();
        }

        var list = await teacher.Client.GetJsonAsync<HomeworkListResponse>(Path(teacher));
        list.Items.ShouldHaveSingleItem().Assigned.ShouldBe(3);

        // Only an unanswered assignment can be removed; archived students get no new homework.
        var milaAssignment = byGroup.Assignments.Single(a => a.StudentId == mila.Id);
        (await ReadAsync<HomeworkResponse>(await teacher.Client.DeleteAsync(Path(teacher, $"/{homework.Id}/assignments/{milaAssignment.Id}")))).Assignments.Count.ShouldBe(2);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{mila.Id}/archive", new { version = mila.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments"), new { studentIds = new[] { mila.Id } })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Answered_assignments_stay_and_reviews_need_an_answer()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        var homework = await CreateAsync(teacher, new { title = "Правила" });
        var assignment = (await AssignAsync(teacher, homework.Id, new { studentIds = new[] { ana } })).Assignments.Single();

        using (var nothingToReview = await teacher.Client.PostAsync(Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}/review"), new { comment = "x", version = assignment.Version }))
        {
            nothingToReview.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        await SubmitAsync(learner, teacher.WorkspaceId, assignment.Id, "Одговор", null);
        using var remove = await teacher.Client.DeleteAsync(Path(teacher, $"/{homework.Id}/assignments/{assignment.Id}"));
        remove.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await remove.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
    }

    [Fact]
    public async Task Homework_links_to_a_lesson_of_the_workspace_and_validates_its_fields()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var student = await StudentAsync(teacher, "Ана");
        var lesson = await LessonAsync(teacher, student.Id);
        var foreignLesson = await LessonAsync(other, (await StudentAsync(other, "Туѓ")).Id);

        var linked = await CreateAsync(teacher, new { title = "По часот", lessonId = lesson.Id });
        linked.Lesson.ShouldNotBeNull().Title.ShouldBe("Равенки");
        (await teacher.Client.PostAsync(Path(teacher), new { title = "Туѓ час", lessonId = foreignLesson.Id })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher), new { title = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher), new { title = "x", description = new string('x', 4001) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PutAsync(Path(teacher, $"/{linked.Id}"), new { title = "Ново", version = linked.Version + 5 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var renamed = await ReadAsync<HomeworkResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{linked.Id}"), new { title = "Ново", lessonId = lesson.Id, version = linked.Version }));
        renamed.Title.ShouldBe("Ново");
        (await teacher.Client.GetJsonAsync<HomeworkListResponse>(Path(teacher, "?q=Ново"))).Total.ShouldBe(1);
        await CreateAsync(teacher, new { title = "Без час" });
        (await teacher.Client.GetJsonAsync<HomeworkListResponse>(Path(teacher, $"?lessonId={lesson.Id}"))).Items.ShouldHaveSingleItem().Id.ShouldBe(linked.Id);
    }

    private static MultipartFormDataContent Form(string? text, (string Name, byte[] Bytes)? file, bool removeFile)
    {
        var content = new MultipartFormDataContent();
        if (text is not null)
        {
            content.Add(new StringContent(text), "text");
        }

        if (removeFile)
        {
            content.Add(new StringContent("true"), "removeFile");
        }

        if (file is { } f)
        {
            var part = new ByteArrayContent(f.Bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "file", f.Name);
        }

        return content;
    }

    private static async Task<HttpResponseMessage> PostSubmissionAsync(TeacherSession learner, Guid workspaceId, Guid assignmentId, string? text, (string Name, byte[] Bytes)? file, bool removeFile = false)
    {
        using var content = Form(text, file, removeFile);
        return await learner.Client.SendContentAsync(HttpMethod.Post, Portal(workspaceId, $"/{assignmentId}/submission"), content);
    }

    private static async Task<StudentHomeworkResponse> SubmitAsync(TeacherSession learner, Guid workspaceId, Guid assignmentId, string? text, (string Name, byte[] Bytes)? file, bool removeFile = false) =>
        await ReadAsync<StudentHomeworkResponse>(await PostSubmissionAsync(learner, workspaceId, assignmentId, text, file, removeFile));

    private static async Task<HomeworkResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(Path(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<HomeworkResponse>(Ct))!;
    }

    private static async Task<HomeworkResponse> AssignAsync(TeacherSession teacher, Guid homeworkId, object body) =>
        await ReadAsync<HomeworkResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{homeworkId}/assignments"), body));

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName, string? email = null)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName, email });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    private async Task<Guid> LinkedStudentAsync(TeacherSession teacher, TeacherSession learner, string firstName)
    {
        var student = await StudentAsync(teacher, firstName, learner.Email);
        var since = DateTime.UtcNow.AddMilliseconds(-5);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{student.Id}/invitation", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await fixture.WaitForEmailAsync(learner.Email, "student.invitation", since)).FragmentValue("token");
        using var accept = await learner.Client.PostAsync("/api/student-invitations/accept", new { token });
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync(Ct));
        return student.Id;
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

    private static async Task<LessonResponse> LessonAsync(TeacherSession teacher, Guid studentId)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons",
            new { kind = "Individual", studentId, title = "Равенки", startLocal = "2024-03-12T10:00:00", durationMinutes = 60, mode = "Online" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private async Task AddStoredBytesAsync(Guid workspaceId, long bytes)
    {
        await using var db = fixture.CreateDbContext();
        db.Set<StoredFile>().Add(new StoredFile
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            Purpose = "material",
            ContentType = "application/pdf",
            SizeBytes = bytes,
            Sha256 = new string('0', 64),
            StorageKey = $"{workspaceId:N}/materials/{Guid.NewGuid():N}.pdf",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<int> StoredFilesAsync(Guid workspaceId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM StoredFiles WHERE WorkspaceId = {workspaceId}").SingleAsync(Ct);
    }

    private string HomeworkRoot(Guid workspaceId)
    {
        var path = System.IO.Path.Combine(fixture.Factory.FileRoot, workspaceId.ToString("N"), "homework");
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
