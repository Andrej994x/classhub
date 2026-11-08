using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Lessons;

/// <summary>R2-P4: scheduling without overlaps (also under parallel requests), statuses (D-22), time zones and isolation.</summary>
[Collection(ApiCollection.Name)]
public sealed class LessonTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/lessons{rest}";

    [Fact]
    public async Task A_lesson_is_scheduled_in_local_time_and_an_overlap_is_refused()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var lesson = await CreateAsync(teacher, Individual(ana.Id, "2030-11-12T17:00:00", 60));

        // Europe/Skopje in November is UTC+1.
        lesson.StartUtc.ShouldBe(new DateTime(2030, 11, 12, 16, 0, 0, DateTimeKind.Utc));
        lesson.StartLocal.ShouldBe(new DateTime(2030, 11, 12, 17, 0, 0));
        lesson.EndLocal.ShouldBe(new DateTime(2030, 11, 12, 18, 0, 0));
        lesson.TimeZone.ShouldBe("Europe/Skopje");
        lesson.Status.ShouldBe(nameof(LessonStatus.Scheduled));
        lesson.Participants.ShouldHaveSingleItem().FirstName.ShouldBe("Ана");

        using (var overlap = await teacher.Client.PostAsync(Path(teacher), Individual(ana.Id, "2030-11-12T17:30:00", 60)))
        {
            overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await overlap.ErrorCodeAsync()).ShouldBe(ErrorCodes.LessonOverlap);
        }

        // Touching lessons are fine: one ends when the next starts.
        await CreateAsync(teacher, Individual(ana.Id, "2030-11-12T18:00:00", 45));
        await CreateAsync(teacher, Individual(ana.Id, "2030-11-12T16:15:00", 45));

        var day = await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-11-12&to=2030-11-12"));
        day.Select(l => l.StartLocal.Hour * 60 + l.StartLocal.Minute).ShouldBe([16 * 60 + 15, 17 * 60, 18 * 60]);
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-11-13&to=2030-11-20"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Parallel_requests_for_overlapping_times_schedule_exactly_one_lesson()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            teacher.Client.PostAsync(Path(teacher), Individual(ana.Id, $"2030-11-14T10:{i * 10:00}:00", 60))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-11-14&to=2030-11-14"))).ShouldHaveSingleItem();
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Held_and_cancelled_lessons_are_final_and_a_cancelled_one_frees_the_time()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var future = await CreateAsync(teacher, Individual(ana.Id, "2030-11-15T09:00:00", 60));

        using (var early = await teacher.Client.PostAsync(Path(teacher, $"/{future.Id}/complete"), new { version = future.Version }))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // not started yet
        }

        (await teacher.Client.PostAsync(Path(teacher, $"/{future.Id}/cancel"), new { version = future.Version })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // reason required
        var cancelled = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{future.Id}/cancel"), new { version = future.Version, reason = "Болен" }));
        cancelled.Status.ShouldBe(nameof(LessonStatus.Cancelled));
        cancelled.CancellationReason.ShouldBe("Болен");
        (await teacher.Client.PostAsync(Path(teacher, $"/{future.Id}/complete"), new { version = cancelled.Version })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await CreateAsync(teacher, Individual(ana.Id, "2030-11-15T09:00:00", 60)); // the time is free again

        // A past lesson can be recorded and marked as held, once; then it cannot be cancelled or changed.
        var past = await CreateAsync(teacher, Individual(ana.Id, "2024-03-05T15:00:00", 60));
        var held = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync(Path(teacher, $"/{past.Id}/complete"), new { version = past.Version }));
        held.Status.ShouldBe(nameof(LessonStatus.Completed));
        (await teacher.Client.PostAsync(Path(teacher, $"/{past.Id}/complete"), new { version = held.Version })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await teacher.Client.PostAsync(Path(teacher, $"/{past.Id}/cancel"), new { version = held.Version, reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var edit = await teacher.Client.PutAsync(Path(teacher, $"/{past.Id}"), Individual(ana.Id, "2024-03-05T16:00:00", 60, held.Version));
        edit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await edit.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
    }

    [Fact]
    public async Task Rescheduling_checks_overlaps_except_with_itself_and_uses_the_loaded_version()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var first = await CreateAsync(teacher, Individual(ana.Id, "2030-11-16T10:00:00", 60));
        await CreateAsync(teacher, Individual(boris.Id, "2030-11-16T12:00:00", 60));

        var longer = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{first.Id}"), Individual(boris.Id, "2030-11-16T10:00:00", 90, first.Version)));
        longer.EndLocal.ShouldBe(new DateTime(2030, 11, 16, 11, 30, 0));
        longer.Participants.ShouldHaveSingleItem().StudentId.ShouldBe(boris.Id); // participant replaced

        using (var clash = await teacher.Client.PutAsync(Path(teacher, $"/{first.Id}"), Individual(ana.Id, "2030-11-16T11:30:00", 60, longer.Version)))
        {
            (await clash.ErrorCodeAsync()).ShouldBe(ErrorCodes.LessonOverlap);
        }

        (await teacher.Client.PutAsync(Path(teacher, $"/{first.Id}"), Individual(ana.Id, "2030-11-16T09:00:00", 60, first.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_group_lesson_takes_the_active_members_as_participants()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var archived = await StudentAsync(teacher, "Бојан");
        var group = await GroupAsync(teacher, "Алгебра 8", ana.Id, boris.Id, archived.Id);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{archived.Id}/archive", new { version = archived.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var lesson = await CreateAsync(teacher, new { kind = "Group", groupId = group.Id, title = "Равенки", startLocal = "2030-11-18T17:00:00", durationMinutes = 90, mode = "InPerson", location = "Училница 3" });

        lesson.GroupName.ShouldBe("Алгебра 8");
        lesson.Participants.Select(p => p.StudentId).ShouldBe([ana.Id, boris.Id], ignoreOrder: true);
        lesson.Location.ShouldBe("Училница 3");
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, $"?from=2030-11-18&to=2030-11-18&studentId={ana.Id}"))).ShouldHaveSingleItem();
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, $"?from=2030-11-18&to=2030-11-18&studentId={archived.Id}"))).ShouldBeEmpty();
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, $"?from=2030-11-18&to=2030-11-18&groupId={group.Id}"))).ShouldHaveSingleItem();

        var empty = await GroupAsync(teacher, "Празна");
        using var none = await teacher.Client.PostAsync(Path(teacher), new { kind = "Group", groupId = empty.Id, title = "x", startLocal = "2030-11-19T17:00:00", durationMinutes = 60, mode = "Online" });
        none.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Daylight_saving_changes_are_handled_explicitly()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");

        // 02:30 does not exist on 31 March 2030 in Skopje (clocks jump from 02:00 to 03:00).
        using (var gap = await teacher.Client.PostAsync(Path(teacher), Individual(ana.Id, "2030-03-31T02:30:00", 60)))
        {
            gap.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await gap.ReadJsonAsync()).GetProperty("errors").TryGetProperty("startLocal", out _).ShouldBeTrue();
        }

        // On 27 October 2030 the clocks go back: two hours from 01:30 summer time end at 02:30 winter time.
        var night = await CreateAsync(teacher, Individual(ana.Id, "2030-10-27T01:30:00", 120));
        (night.EndUtc - night.StartUtc).ShouldBe(TimeSpan.FromHours(2));
        night.EndLocal.ShouldBe(new DateTime(2030, 10, 27, 2, 30, 0));
        // The 25-hour day: a lesson late that evening still belongs to it.
        var evening = await CreateAsync(teacher, Individual(ana.Id, "2030-10-27T23:30:00", 30));
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-10-27&to=2030-10-27"))).Select(l => l.Id).ShouldBe([night.Id, evening.Id]);
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-10-28&to=2030-10-28"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task After_the_workspace_time_zone_changes_lessons_show_and_group_in_the_new_zone()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        // 00:30 in Skopje on 12 November is 23:30 UTC on 11 November.
        var lesson = await CreateAsync(teacher, Individual(ana.Id, "2030-11-12T00:30:00", 60));
        (await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new { timeZone = "Europe/London" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var day = (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-11-11&to=2030-11-11"))).ShouldHaveSingleItem();
        day.StartLocal.ShouldBe(new DateTime(2030, 11, 11, 23, 30, 0)); // grouped and shown on the day it is read for
        day.TimeZone.ShouldBe("Europe/London");
        (await teacher.Client.GetJsonAsync<List<LessonResponse>>(Path(teacher, "?from=2030-11-12&to=2030-11-12"))).ShouldBeEmpty();

        // Saving what the editor shows keeps the same instant.
        var saved = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync(Path(teacher, $"/{lesson.Id}"), Individual(ana.Id, "2030-11-11T23:30:00", 60, lesson.Version)));
        saved.StartUtc.ShouldBe(lesson.StartUtc);
    }

    [Fact]
    public async Task Requests_are_validated()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");

        (await teacher.Client.GetAsync(Path(teacher, "?from=2030-01-01&to=2030-04-01"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // > 62 days
        (await teacher.Client.GetAsync(Path(teacher, "?from=2030-02-01&to=2030-01-01"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher), Individual(ana.Id, "2030-11-20T10:00:00", 10))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // < 15 minutes
        (await teacher.Client.PostAsync(Path(teacher), new { kind = "Individual", title = "x", startLocal = "2030-11-20T10:00:00", durationMinutes = 60, mode = "Online" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var script = await teacher.Client.PostAsync(Path(teacher), new { kind = "Individual", studentId = ana.Id, title = "x", startLocal = "2030-11-20T10:00:00", durationMinutes = 60, mode = "Online", meetingUrl = "javascript:alert(1)" });
        script.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await script.ReadJsonAsync()).GetProperty("errors").TryGetProperty("meetingUrl", out _).ShouldBeTrue();
        var online = await CreateAsync(teacher, new { kind = "Individual", studentId = ana.Id, title = "x", startLocal = "2030-11-20T10:00:00", durationMinutes = 60, mode = "Online", meetingUrl = "https://meet.example.test/abc", location = "ignored" });
        online.MeetingUrl.ShouldBe("https://meet.example.test/abc");
        online.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Another_teacher_cannot_see_use_or_change_lessons()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(owner, "Ана");
        var lesson = await CreateAsync(owner, Individual(ana.Id, "2030-11-21T10:00:00", 60));

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(owner, $"/{lesson.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, Path(owner, $"/{lesson.Id}/cancel"), new { version = lesson.Version, reason = "x" });
        (await intruder.Client.GetAsync(Path(intruder, $"/{lesson.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.GetJsonAsync<List<LessonResponse>>(Path(intruder, "?from=2030-11-21&to=2030-11-21"))).ShouldBeEmpty();
        // The owner's student is unknown in the intruder's workspace; the database refuses a cross-tenant participant too.
        (await intruder.Client.PostAsync(Path(intruder), Individual(ana.Id, "2030-11-21T12:00:00", 60))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Another teacher's schedule does not block this one.
        var own = await StudentAsync(intruder, "Мила");
        await CreateAsync(intruder, Individual(own.Id, "2030-11-21T10:00:00", 60));

        await using var db = fixture.CreateDbContext();
        var error = await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO LessonParticipants (WorkspaceId, LessonId, StudentId) VALUES ({owner.WorkspaceId}, {lesson.Id}, {own.Id})", Ct));
        error.Number.ShouldBe(547);
    }

    private static object Individual(Guid studentId, string startLocal, int durationMinutes, int? version = null) =>
        new { kind = "Individual", studentId, title = "Математика", startLocal, durationMinutes, mode = "Online", version };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }

    private static async Task<LessonResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(Path(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    private static async Task<GroupResponse> GroupAsync(TeacherSession teacher, string name, params Guid[] members)
    {
        GroupResponse group;
        using (var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups", new { name }))
        {
            group = (await response.Content.ReadFromJsonAsync<GroupResponse>(Ct))!;
        }

        foreach (var member in members)
        {
            group = await ReadAsync<GroupResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups/{group.Id}/members", new { studentId = member }));
        }

        return group;
    }
}
