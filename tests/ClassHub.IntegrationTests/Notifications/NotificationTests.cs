using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.Modules.Notifications.Jobs;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Notifications;

/// <summary>
/// R2-P11: lesson and homework notifications to students, reminders planned from the schedule, withdrawal on cancel and
/// reschedule, preferences, recipients resolved at send time, and dispatchers that never send twice.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ClassHubApiFixture fixture)
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_lesson_is_announced_once_to_the_students_account()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        var since = DateTime.UtcNow.AddSeconds(-1);
        var lesson = await LessonAsync(teacher, ana, StartIn(TimeSpan.FromDays(3)));

        await RunAsync(DateTime.UtcNow);
        await RunAsync(DateTime.UtcNow); // again: nothing new

        var email = await fixture.WaitForEmailAsync(learner.Email, "notify.lesson-scheduled", since);
        email.Subject.ShouldContain("Час по математика");
        email.TextBody.ShouldContain("(Europe/Skopje)");
        email.TextBody.ShouldContain($"/student/w/{teacher.WorkspaceId}");
        (await EmailsAsync(learner.Email, "notify.lesson-scheduled", since)).ShouldBe(1);
        (await StatusesAsync(lesson.Id)).ShouldBe([(NotificationKind.LessonScheduled, NotificationStatus.Sent)]);
    }

    [Fact]
    public async Task Cancelling_withdraws_the_pending_reminder_and_tells_who_was_told()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var now = DateTime.UtcNow;
        var since = now.AddSeconds(-1);
        var lesson = await LessonAsync(teacher, ana.Id, StartIn(TimeSpan.FromHours(30)));
        await RunAsync(now); // the announcement goes out; the reminder is not planned yet (more than 26 h away)

        await RunAsync(now.AddHours(5)); // the reminder is planned, due 24 h before the lesson
        (await StatusesAsync(lesson.Id)).ShouldContain((NotificationKind.LessonReminder, NotificationStatus.Pending));

        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}/cancel", new { version = lesson.Version, reason = "Болест" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RunAsync(now.AddHours(7));
        await RunAsync(now.AddHours(8));

        (await StatusesAsync(lesson.Id)).ShouldBe(
            [(NotificationKind.LessonScheduled, NotificationStatus.Sent), (NotificationKind.LessonReminder, NotificationStatus.Cancelled), (NotificationKind.LessonCancelled, NotificationStatus.Sent)],
            ignoreOrder: true);
        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-cancelled", since);
        (await EmailsAsync(ana.Email!, "notify.lesson-reminder", since)).ShouldBe(0); // never a reminder for a cancelled lesson
    }

    [Fact]
    public async Task Rescheduling_replaces_the_reminder_with_exactly_one_for_the_new_time()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var now = DateTime.UtcNow;
        var since = now.AddSeconds(-1);
        var start = StartIn(TimeSpan.FromHours(30));
        var lesson = await LessonAsync(teacher, ana.Id, start);
        await RunAsync(now.AddHours(5)); // announced; reminder for the old time planned (pending)

        var moved = start.AddHours(10);
        var updated = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}",
            LessonBody(ana.Id, moved, lesson.Version)));
        updated.StartUtc.ShouldBe(moved);
        await RunAsync(now.AddHours(6));  // the move is told; the old reminder is gone
        await RunAsync(now.AddHours(17)); // the new reminder: planned and due (24 h before the new time)
        await RunAsync(now.AddHours(18)); // again: nothing new

        var reminder = await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-reminder", since);
        reminder.TextBody.ShouldContain(TimeZoneInfo.ConvertTimeFromUtc(moved, Skopje).ToString("HH:mm", CultureInfo.InvariantCulture));
        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-rescheduled", since);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-reminder", since)).ShouldBe(1);
        (await EmailsAsync(ana.Email!, "notify.lesson-rescheduled", since)).ShouldBe(1);
        var reminders = (await StatusesAsync(lesson.Id)).Where(s => s.Kind == NotificationKind.LessonReminder).ToList();
        reminders.ShouldBe([(NotificationKind.LessonReminder, NotificationStatus.Cancelled), (NotificationKind.LessonReminder, NotificationStatus.Sent)], ignoreOrder: true);
    }

    [Fact]
    public async Task Optional_notifications_follow_the_students_preferences()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        (await learner.Client.GetJsonAsync<NotificationPreferencesResponse>("/api/me/notification-preferences")).ShouldBe(new NotificationPreferencesResponse(true, true));
        (await ReadAsync<NotificationPreferencesResponse>(await learner.Client.PutAsync("/api/me/notification-preferences", new { lessonReminders = false, homeworkEmails = false })))
            .ShouldBe(new NotificationPreferencesResponse(false, false));

        var since = DateTime.UtcNow.AddSeconds(-1);
        var lesson = await LessonAsync(teacher, ana, StartIn(TimeSpan.FromHours(20)));
        var homework = await HomeworkAsync(teacher, "Равенки", ana);
        await RunAsync(DateTime.UtcNow);

        var statuses = await StatusesAsync(lesson.Id);
        statuses.ShouldContain((NotificationKind.LessonScheduled, NotificationStatus.Sent)); // lesson changes always go out
        statuses.ShouldContain((NotificationKind.LessonReminder, NotificationStatus.Suppressed));
        (await HomeworkStatusAsync(homework.Id)).ShouldBe(NotificationStatus.Suppressed);
        (await EmailsAsync(learner.Email, "notify.lesson-reminder", since)).ShouldBe(0);
        (await EmailsAsync(learner.Email, "notify.homework-assigned", since)).ShouldBe(0);

        using var anonymous = await ApiClient.CreateAsync(fixture);
        (await anonymous.GetAsync("/api/me/notification-preferences")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recipients_are_resolved_when_sending()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var oldEmail = ApiTestExtensions.UniqueEmail("old");
        var newEmail = ApiTestExtensions.UniqueEmail("new");
        var ana = await StudentAsync(teacher, "Ана", oldEmail);
        var boris = await StudentAsync(teacher, "Борис", null); // no address
        var mila = await StudentAsync(teacher, "Мила", ApiTestExtensions.UniqueEmail("mila"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var homework = await HomeworkAsync(teacher, "Есеј", ana.Id, boris.Id, mila.Id);

        // Before sending: Ana's email changes and Mila is archived.
        await ReadAsync<StudentResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/students/{ana.Id}",
            new { firstName = "Ана", email = newEmail, isMinor = false, subjectIds = Array.Empty<Guid>(), version = ana.Version }));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{mila.Id}/archive", new { version = mila.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RunAsync(DateTime.UtcNow);

        var email = await fixture.WaitForEmailAsync(newEmail, "notify.homework-assigned", since);
        email.Subject.ShouldContain("Есеј");
        email.TextBody.ShouldNotContain("/student/w/"); // no account: no portal link
        (await EmailsAsync(oldEmail, "notify.homework-assigned", since)).ShouldBe(0);
        (await OutcomesAsync(homework.Id)).ShouldBe([("no-address", NotificationStatus.Suppressed), ("student", NotificationStatus.Suppressed), (null, NotificationStatus.Sent)], ignoreOrder: true);
    }

    [Fact]
    public async Task Parallel_dispatchers_send_every_notification_exactly_once()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var students = new List<StudentResponse>();
        for (var i = 0; i < 8; i++) // within the free plan's active students
        {
            students.Add(await StudentAsync(teacher, $"Ученик {i}", ApiTestExtensions.UniqueEmail($"s{i}")));
        }

        var since = DateTime.UtcNow.AddSeconds(-1);
        var homework = await HomeworkAsync(teacher, "Заедничка задача", [.. students.Select(s => s.Id)]);
        var job = fixture.Factory.Services.GetRequiredService<NotificationJob>();
        var now = DateTime.UtcNow;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => job.RunAtAsync(now, Ct), Ct)));
        await RunAsync(now);

        (await OutcomesAsync(homework.Id)).ShouldAllBe(o => o.Status == NotificationStatus.Sent);
        foreach (var student in students)
        {
            await fixture.WaitForEmailAsync(student.Email!, "notify.homework-assigned", since);
        }

        await Task.Delay(500, Ct); // the outbox delivers anything left
        foreach (var student in students)
        {
            (await EmailsAsync(student.Email!, "notify.homework-assigned", since)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Group_members_and_their_changes_are_told_and_new_homework_links_to_the_portal()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var learner = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, learner, "Ана");
        var boris = await StudentAsync(teacher, "Борис", ApiTestExtensions.UniqueEmail("boris"));
        var group = await ReadAsync<GroupResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups", new { name = "Алгебра" }));
        foreach (var member in new[] { ana, boris.Id })
        {
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups/{group.Id}/members", new { studentId = member })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var since = DateTime.UtcNow.AddSeconds(-1);
        var homework = await HomeworkAsync(teacher, "Групна задача", ana);
        await RunAsync(DateTime.UtcNow);
        var mail = await fixture.WaitForEmailAsync(learner.Email, "notify.homework-assigned", since);
        mail.TextBody.ShouldContain($"/student/w/{teacher.WorkspaceId}/homework/");

        // A group lesson: both are announced; moving the lesson to an individual one for Boris tells Ana it is off.
        var start = StartIn(TimeSpan.FromDays(4));
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons", new
        {
            kind = "Group", groupId = group.Id, title = "Час по математика", startLocal = Local(start), durationMinutes = 60, mode = "Online",
        });
        var lesson = (await created.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
        await RunAsync(DateTime.UtcNow);
        await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(boris.Id, start, lesson.Version)));
        await RunAsync(DateTime.UtcNow);

        await fixture.WaitForEmailAsync(learner.Email, "notify.lesson-cancelled", since);
        await fixture.WaitForEmailAsync(boris.Email!, "notify.lesson-scheduled", since);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(boris.Email!, "notify.lesson-scheduled", since)).ShouldBe(1); // he stays in it: no second announcement
        (await EmailsAsync(boris.Email!, "notify.lesson-cancelled", since)).ShouldBe(0);
        homework.Id.ShouldNotBe(Guid.Empty);
    }

    private async Task RunAsync(DateTime nowUtc) => await fixture.Factory.Services.GetRequiredService<NotificationJob>().RunAtAsync(nowUtc, Ct);

    /// <summary>A start this far from now, on a whole minute.</summary>
    private static DateTime StartIn(TimeSpan delay)
    {
        var start = DateTime.UtcNow + delay;
        return new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, DateTimeKind.Utc);
    }

    private static string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Skopje).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static object LessonBody(Guid studentId, DateTime startUtc, int? version = null) =>
        new { kind = "Individual", studentId, title = "Час по математика", startLocal = Local(startUtc), durationMinutes = 60, mode = "Online", version };

    private static async Task<LessonResponse> LessonAsync(TeacherSession teacher, Guid studentId, DateTime startUtc)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons", LessonBody(studentId, startUtc));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private static async Task<HomeworkResponse> HomeworkAsync(TeacherSession teacher, string title, params Guid[] students)
    {
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/homework", new { title });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var homework = (await created.Content.ReadFromJsonAsync<HomeworkResponse>(Ct))!;
        return await ReadAsync<HomeworkResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/homework/{homework.Id}/assignments", new { studentIds = students }));
    }

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName, string? email)
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

    private async Task<List<(NotificationKind Kind, NotificationStatus Status)>> StatusesAsync(Guid lessonId)
    {
        await using var db = fixture.CreateDbContext();
        return [.. (await db.Set<Notification>().IgnoreQueryFilters().AsNoTracking().Where(n => n.LessonId == lessonId).ToListAsync(Ct)).Select(n => (n.Kind, n.Status))];
    }

    private async Task<NotificationStatus> HomeworkStatusAsync(Guid homeworkId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Notification>().IgnoreQueryFilters().Where(n => n.HomeworkId == homeworkId).Select(n => n.Status).SingleAsync(Ct);
    }

    private async Task<List<(string? Outcome, NotificationStatus Status)>> OutcomesAsync(Guid homeworkId)
    {
        await using var db = fixture.CreateDbContext();
        return [.. (await db.Set<Notification>().IgnoreQueryFilters().AsNoTracking().Where(n => n.HomeworkId == homeworkId).ToListAsync(Ct)).Select(n => (n.Outcome, n.Status))];
    }

    private async Task<int> EmailsAsync(string to, string template, DateTime sinceUtc)
    {
        await using var db = fixture.CreateDbContext();
        var address = to.Trim().ToLowerInvariant();
        return await db.DevMailbox.CountAsync(m => m.ToAddress == address && m.Template == template && m.ReceivedAtUtc >= sinceUtc, Ct);
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
