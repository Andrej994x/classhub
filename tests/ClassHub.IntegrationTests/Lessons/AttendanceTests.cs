using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Lessons;

/// <summary>R2-P7: idempotent completion with attendance (D-22), the Finance event, notes (D-40) and history.</summary>
[Collection(ApiCollection.Name)]
public sealed class AttendanceTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/lessons{rest}";

    [Fact]
    public async Task Completing_records_attendance_once_and_one_event_for_finance()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (lesson, ana, boris, mila) = await PastGroupLessonAsync(teacher);

        var held = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/complete"), new
        {
            version = lesson.Version,
            attendance = new[] { new { studentId = ana, status = "Absent" }, new { studentId = boris, status = "Excused" } },
        }));

        held.Status.ShouldBe("Completed");
        held.Participants.Single(p => p.StudentId == ana).Attendance.ShouldBe("Absent");
        held.Participants.Single(p => p.StudentId == boris).Attendance.ShouldBe("Excused");
        held.Participants.Single(p => p.StudentId == mila).Attendance.ShouldBe("Present"); // left out: present
        (await EventsAsync(lesson.Id, LessonEventKinds.Completed)).ShouldBe(1);

        // Again (even with the old version): the held lesson as it is, never recorded a second time.
        var again = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/complete"), new { version = lesson.Version }));
        again.Version.ShouldBe(held.Version);
        again.Participants.Single(p => p.StudentId == ana).Attendance.ShouldBe("Absent");
        (await EventsAsync(lesson.Id, LessonEventKinds.Completed)).ShouldBe(1);
    }

    [Fact]
    public async Task Lessons_held_before_attendance_existed_are_backfilled_as_present()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (lesson, ana, _, _) = await PastGroupLessonAsync(teacher);
        var held = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/complete"), new { version = lesson.Version }));
        await using (var db = fixture.CreateDbContext())
        {
            // As it was before this phase: held, without attendance.
            await db.Database.ExecuteSqlAsync($"UPDATE LessonParticipants SET Attendance = NULL WHERE LessonId = {lesson.Id}", Ct);
            await db.Database.ExecuteSqlRawAsync(ClassHub.Core.Modules.Lessons.Domain.LessonBackfills.CompletedLessonAttendanceSql, Ct);
        }

        var after = await teacher.Client.GetJsonAsync<LessonResponse>(Path(teacher, $"/{lesson.Id}"));
        after.Participants.ShouldAllBe(p => p.Attendance == "Present");
        (await teacher.Client.GetJsonAsync<LessonHistoryResponse>(Path(teacher, $"/history?studentId={ana}"))).Items.ShouldHaveSingleItem().Attendance.ShouldBe("Present");
        held.Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task Parallel_completions_make_one_transition_and_one_event()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (lesson, _, _, _) = await PastGroupLessonAsync(teacher);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/complete"), new { version = lesson.Version })));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await EventsAsync(lesson.Id, LessonEventKinds.Completed)).ShouldBe(1);
        (await teacher.Client.GetJsonAsync<LessonResponse>(Path(teacher, $"/{lesson.Id}"))).Version.ShouldBe(lesson.Version + 1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Attendance_is_corrected_on_held_lessons_only_and_finance_is_told()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (lesson, ana, _, _) = await PastGroupLessonAsync(teacher);

        using (var early = await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/attendance"), new { version = lesson.Version, attendance = new[] { new { studentId = ana, status = "Absent" } } }))
        {
            (await early.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        var held = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/complete"), new { version = lesson.Version }));
        var corrected = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/attendance"),
            new { version = held.Version, attendance = new[] { new { studentId = ana, status = "Absent" } } }));
        corrected.Participants.Single(p => p.StudentId == ana).Attendance.ShouldBe("Absent");
        (await EventsAsync(lesson.Id, LessonEventKinds.AttendanceChanged)).ShouldBe(1);

        var stranger = await StudentAsync(teacher, "Туѓ");
        (await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/attendance"), new { version = corrected.Version, attendance = new[] { new { studentId = stranger, status = "Present" } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/attendance"), new { version = corrected.Version, attendance = new[] { new { studentId = ana, status = "Late" } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_cancelled_lesson_is_never_held()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (lesson, _, _, _) = await PastGroupLessonAsync(teacher);
        var cancelled = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/cancel"), new { version = lesson.Version, reason = "Празник" }));

        using var complete = await teacher.Client.PostAsync(Path(teacher, $"/{lesson.Id}/complete"), new { version = cancelled.Version });

        complete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await EventsAsync(lesson.Id, LessonEventKinds.Completed)).ShouldBe(0);
    }

    [Fact]
    public async Task Notes_are_kept_private_and_shared_separately()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (lesson, _, _, _) = await PastGroupLessonAsync(teacher);

        var noted = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/notes"),
            new { version = lesson.Version, privateNote = "Бориса да го прашам за домашното.", sharedSummary = "Решававме линеарни равенки." }));

        noted.PrivateNote.ShouldBe("Бориса да го прашам за домашното.");
        noted.SharedSummary.ShouldBe("Решававме линеарни равенки.");
        (await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/notes"), new { version = lesson.Version, privateNote = "x" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}/notes"), new { version = noted.Version, privateNote = new string('x', 4001) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task History_lists_a_students_and_a_groups_lessons_newest_first()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var (groupLesson, ana, _, _) = await PastGroupLessonAsync(teacher);
        await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{groupLesson.Id}/complete"),
            new { version = groupLesson.Version, attendance = new[] { new { studentId = ana, status = "Absent" } } }));
        var single = await CreateAsync(teacher, new { kind = "Individual", studentId = ana, title = "Поединечно", startLocal = "2024-03-12T10:00:00", durationMinutes = 60, mode = "Online" });

        var history = await teacher.Client.GetJsonAsync<LessonHistoryResponse>(Path(teacher, $"/history?studentId={ana}"));
        history.Total.ShouldBe(2);
        history.Items.Select(i => i.Id).ShouldBe([single.Id, groupLesson.Id]); // newest first
        history.Items[1].Attendance.ShouldBe("Absent");
        history.Items[1].GroupName.ShouldBe("Алгебра");
        history.Items[0].Attendance.ShouldBeNull(); // not held yet

        var group = await teacher.Client.GetJsonAsync<LessonHistoryResponse>(Path(teacher, $"/history?groupId={groupLesson.GroupId}"));
        group.Items.ShouldHaveSingleItem().Id.ShouldBe(groupLesson.Id);
        (await teacher.Client.GetAsync(Path(teacher, "/history"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetAsync(Path(teacher, $"/history?studentId={ana}&groupId={groupLesson.GroupId}"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var intruder = await fixture.CreateTeacherAsync();
        (await intruder.Client.GetJsonAsync<LessonHistoryResponse>(Path(intruder, $"/history?studentId={ana}"))).Items.ShouldBeEmpty();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, Path(teacher, $"/{groupLesson.Id}/notes"), new { version = 0, privateNote = "x" });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher, $"/history?studentId={ana}"));
    }

    /// <summary>A group lesson in the past (it can be held) with three students.</summary>
    private static async Task<(LessonResponse Lesson, Guid Ana, Guid Boris, Guid Mila)> PastGroupLessonAsync(TeacherSession teacher)
    {
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var mila = await StudentAsync(teacher, "Мила");
        GroupResponse group;
        using (var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups", new { name = "Алгебра" }))
        {
            group = (await created.Content.ReadFromJsonAsync<GroupResponse>(Ct))!;
        }

        foreach (var student in new[] { ana, boris, mila })
        {
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups/{group.Id}/members", new { studentId = student })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var lesson = await CreateAsync(teacher, new { kind = "Group", groupId = group.Id, title = "Равенки", startLocal = "2024-03-05T17:00:00", durationMinutes = 90, mode = "Online" });
        return (lesson, ana, boris, mila);
    }

    private async Task<int> EventsAsync(Guid lessonId, string kind)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM LessonEvents WHERE LessonId = {lessonId} AND Kind = {kind}").SingleAsync(Ct);
    }

    private static async Task<LessonResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(Path(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private static async Task<Guid> StudentAsync(TeacherSession teacher, string firstName)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!.Id;
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
