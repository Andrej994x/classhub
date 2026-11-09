using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Jobs;
using ClassHub.Core.Modules.Notifications.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.Modules.Notifications.Jobs;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
    public async Task Lessons_known_without_our_announcement_are_told_when_cancelled()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var known = await LessonAsync(teacher, ana.Id, StartIn(TimeSpan.FromDays(3)));
        var fresh = await LessonAsync(teacher, ana.Id, StartIn(TimeSpan.FromDays(4)));
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series", new
        {
            kind = "Individual", studentId = ana.Id, title = "Италијански", startDate = tomorrow.AddDays(7), startTime = "18:00:00", durationMinutes = 60, intervalWeeks = 1, count = 3, mode = "Online",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var series = (await created.Content.ReadFromJsonAsync<SeriesResultResponse>(Ct))!;

        // The lesson and the series were scheduled before notifications existed: no notification rows at all.
        await using (var db = fixture.CreateDbContext())
        {
            (await db.Set<Notification>().IgnoreQueryFilters().Where(n => n.LessonId == known.Id || n.SeriesId == series.Series.Id).ExecuteDeleteAsync(Ct)).ShouldBe(2);
        }

        // Cancelled: the known lesson is told; the new one, cancelled before its announcement went out, is not.
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{known.Id}/cancel", new { version = known.Version, reason = "Болест" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{fresh.Id}/cancel", new { version = fresh.Version, reason = "Болест" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ReadAsync<SeriesResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series/{series.Series.Id}/end",
            new { fromDate = tomorrow, version = series.Series.Version }));
        await RunAsync(DateTime.UtcNow);

        (await StatusesAsync(known.Id)).ShouldBe([(NotificationKind.LessonCancelled, NotificationStatus.Sent)]);
        (await StatusesAsync(fresh.Id)).ShouldBe([(NotificationKind.LessonScheduled, NotificationStatus.Cancelled)]);
        var cancelled = await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-cancelled", since);
        cancelled.TextBody.ShouldContain(NotificationRules.LocalTime(known.StartUtc, "Europe/Skopje", "mk"));
        (await fixture.WaitForEmailAsync(ana.Email!, "notify.series-ended", since)).Subject.ShouldContain("Италијански");
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-cancelled", since)).ShouldBe(1);
        (await EmailsAsync(ana.Email!, "notify.lesson-scheduled", since)).ShouldBe(0);
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
    public async Task A_student_taken_out_and_added_back_is_told_again()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var boris = await StudentAsync(teacher, "Борис", ApiTestExtensions.UniqueEmail("boris"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var start = StartIn(TimeSpan.FromDays(3));
        var lesson = await LessonAsync(teacher, ana.Id, start);
        await RunAsync(DateTime.UtcNow);

        var toBoris = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(boris.Id, start, lesson.Version)));
        await RunAsync(DateTime.UtcNow);
        await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(ana.Id, start, toBoris.Version)));
        await RunAsync(DateTime.UtcNow);

        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-cancelled", since);
        await fixture.WaitForEmailAsync(boris.Email!, "notify.lesson-cancelled", since);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-scheduled", since)).ShouldBe(2); // she is in it again: told again
        (await EmailsAsync(ana.Email!, "notify.lesson-cancelled", since)).ShouldBe(1);
        (await EmailsAsync(boris.Email!, "notify.lesson-scheduled", since)).ShouldBe(1);
        (await EmailsAsync(boris.Email!, "notify.lesson-cancelled", since)).ShouldBe(1);
    }

    [Fact]
    public async Task A_student_taken_out_and_back_before_sending_gets_no_cancellation()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var boris = await StudentAsync(teacher, "Борис", ApiTestExtensions.UniqueEmail("boris"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var start = StartIn(TimeSpan.FromDays(3));
        var lesson = await LessonAsync(teacher, ana.Id, start);
        await RunAsync(DateTime.UtcNow); // Ana is told

        // Out and back in before the dispatcher runs again: the cancellation is no longer true.
        var toBoris = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(boris.Id, start, lesson.Version)));
        await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(ana.Id, start, toBoris.Version)));
        await RunAsync(DateTime.UtcNow);

        var anaNotices = (await StatusesAsync(lesson.Id)).Count(s => s.Kind == NotificationKind.LessonCancelled && s.Status == NotificationStatus.Sent);
        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-scheduled", since);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-cancelled", since)).ShouldBe(0);
        (await EmailsAsync(ana.Email!, "notify.lesson-scheduled", since)).ShouldBe(2);
        anaNotices.ShouldBe(0); // Boris was never told about the lesson, so he gets no cancellation either
    }

    [Fact]
    public async Task A_lesson_moved_away_and_back_gets_a_reminder_for_its_time_again()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var now = DateTime.UtcNow;
        var since = now.AddSeconds(-1);
        var start = StartIn(TimeSpan.FromHours(30));
        var lesson = await LessonAsync(teacher, ana.Id, start);
        await RunAsync(now.AddHours(5)); // announced; the reminder for this time is planned

        var away = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(ana.Id, start.AddHours(10), lesson.Version)));
        await RunAsync(now.AddHours(5));
        await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}", LessonBody(ana.Id, start, away.Version)));
        await RunAsync(now.AddHours(7)); // back at the first time: told, and a new reminder for it
        await RunAsync(now.AddHours(8));

        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-reminder", since);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-reminder", since)).ShouldBe(1);
        (await EmailsAsync(ana.Email!, "notify.lesson-rescheduled", since)).ShouldBe(2);
        (await StatusesAsync(lesson.Id)).Where(s => s.Kind == NotificationKind.LessonReminder).ToList()
            .ShouldBe([(NotificationKind.LessonReminder, NotificationStatus.Cancelled), (NotificationKind.LessonReminder, NotificationStatus.Sent)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_series_is_announced_once_and_ending_it_tells_who_was_reminded()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series", new
        {
            kind = "Individual", studentId = ana.Id, title = "Англиски", startDate = tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1, count = 3, mode = "Online",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var series = (await created.Content.ReadFromJsonAsync<SeriesResultResponse>(Ct))!;
        series.Created.ShouldBe(3);
        await RunAsync(DateTime.UtcNow);
        var announcement = await fixture.WaitForEmailAsync(ana.Email!, "notify.series-scheduled", since);
        announcement.Subject.ShouldContain("Англиски");

        // The first lesson's reminder goes out; then the series ends from tomorrow (all three lessons are removed).
        var first = TimeZoneInfo.ConvertTimeToUtc(tomorrow.ToDateTime(new TimeOnly(17, 0)), Skopje);
        var remindAt = first.AddHours(-23) > DateTime.UtcNow ? first.AddHours(-23) : DateTime.UtcNow;
        await RunAsync(remindAt);
        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-reminder", since);
        await ReadAsync<SeriesResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series/{series.Series.Id}/end",
            new { fromDate = tomorrow, version = series.Series.Version }));
        await RunAsync(remindAt.AddMinutes(1));

        // Told it is off: the reminded first lesson (removed, named as it was), and once that the series' lessons are off.
        var cancelled = await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-cancelled", since);
        cancelled.Subject.ShouldContain("Англиски");
        var ended = await fixture.WaitForEmailAsync(ana.Email!, "notify.series-ended", since);
        ended.TextBody.ShouldContain(NotificationRules.LocalTime(first, "Europe/Skopje", "mk"));
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-cancelled", since)).ShouldBe(1); // not one per removed lesson
        (await EmailsAsync(ana.Email!, "notify.series-ended", since)).ShouldBe(1);
        (await EmailsAsync(ana.Email!, "notify.series-scheduled", since)).ShouldBe(1);
        (await EmailsAsync(ana.Email!, "notify.lesson-reminder", since)).ShouldBe(1);
    }

    [Fact]
    public async Task Ending_a_series_names_the_individually_changed_lesson_that_stays()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series", new
        {
            kind = "Individual", studentId = ana.Id, title = "Руски", startDate = tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1, count = 3, mode = "Online",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var series = (await created.Content.ReadFromJsonAsync<SeriesResultResponse>(Ct))!;
        await RunAsync(DateTime.UtcNow);
        await fixture.WaitForEmailAsync(ana.Email!, "notify.series-scheduled", since);

        // The third lesson is moved on its own (it stays when the series ends); then the series ends from tomorrow.
        var lessons = await teacher.Client.GetJsonAsync<List<LessonResponse>>($"/api/w/{teacher.WorkspaceId}/lessons?from={tomorrow:yyyy-MM-dd}&to={tomorrow.AddDays(20):yyyy-MM-dd}");
        var third = lessons.OrderBy(l => l.StartUtc).Last();
        var moved = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{third.Id}",
            LessonBody(ana.Id, third.StartUtc.AddHours(1), third.Version)));
        await ReadAsync<SeriesResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series/{series.Series.Id}/end",
            new { fromDate = tomorrow, version = series.Series.Version }));
        await RunAsync(DateTime.UtcNow);

        var ended = await fixture.WaitForEmailAsync(ana.Email!, "notify.series-ended", since);
        ended.TextBody.ShouldContain("посебно изменети остануваат закажани");
        ended.TextBody.ShouldContain(NotificationRules.LocalTime(moved.StartUtc, "Europe/Skopje", "mk"));
    }

    [Fact]
    public async Task A_series_starting_beyond_the_horizon_is_announced_with_its_first_lesson()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(14 * 7); // 14 weeks ahead
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series", new
        {
            kind = "Individual", studentId = ana.Id, title = "Шпански", startDate = start, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1, count = 3, mode = "Online",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<SeriesResultResponse>(Ct))!.Created.ShouldBe(0); // nothing within 12 weeks yet
        await RunAsync(DateTime.UtcNow);

        // Weeks later the horizon reaches it: its first lessons are created and the series is announced (once).
        var horizon = fixture.Factory.Services.GetRequiredService<LessonSeriesHorizonJob>();
        await horizon.RunAtAsync(DateTime.UtcNow.AddDays(21), Ct);
        await horizon.RunAtAsync(DateTime.UtcNow.AddDays(28), Ct);
        await RunAsync(DateTime.UtcNow);

        var announcement = await fixture.WaitForEmailAsync(ana.Email!, "notify.series-scheduled", since);
        announcement.Subject.ShouldContain("Шпански");
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.series-scheduled", since)).ShouldBe(1);
    }

    [Fact]
    public async Task A_lesson_of_an_announced_series_is_told_when_cancelled()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series", new
        {
            kind = "Individual", studentId = ana.Id, title = "Француски", startDate = tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1, count = 3, mode = "Online",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        await RunAsync(DateTime.UtcNow);
        await fixture.WaitForEmailAsync(ana.Email!, "notify.series-scheduled", since);

        // The third lesson is cancelled (no reminder for it yet): Ana knew about it from the series announcement.
        var lessons = await teacher.Client.GetJsonAsync<List<LessonResponse>>($"/api/w/{teacher.WorkspaceId}/lessons?from={tomorrow:yyyy-MM-dd}&to={tomorrow.AddDays(20):yyyy-MM-dd}");
        var third = lessons.OrderBy(l => l.StartUtc).Last();
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{third.Id}/cancel", new { version = third.Version, reason = "Празник" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RunAsync(DateTime.UtcNow);

        var cancelled = await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-cancelled", since);
        cancelled.TextBody.ShouldContain(NotificationRules.LocalTime(third.StartUtc, "Europe/Skopje", "mk"));
    }

    [Fact]
    public async Task Cancelling_the_first_lesson_of_a_series_keeps_its_announcement()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lesson-series", new
        {
            kind = "Individual", studentId = ana.Id, title = "Германски", startDate = tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1, count = 3, mode = "Online",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var lessons = await teacher.Client.GetJsonAsync<List<LessonResponse>>($"/api/w/{teacher.WorkspaceId}/lessons?from={tomorrow:yyyy-MM-dd}&to={tomorrow.AddDays(20):yyyy-MM-dd}");
        var first = lessons.OrderBy(l => l.StartUtc).First();

        // The first lesson is cancelled before anything is sent: the series is still announced, naming its next lesson.
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{first.Id}/cancel", new { version = first.Version, reason = "Празник" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RunAsync(DateTime.UtcNow);

        var announcement = await fixture.WaitForEmailAsync(ana.Email!, "notify.series-scheduled", since);
        var second = lessons.OrderBy(l => l.StartUtc).Skip(1).First();
        announcement.TextBody.ShouldContain(TimeZoneInfo.ConvertTimeFromUtc(second.StartUtc, Skopje).ToString("yyyy", CultureInfo.InvariantCulture));
        announcement.TextBody.ShouldContain(NotificationRules.LocalTime(second.StartUtc, "Europe/Skopje", "mk"));
    }

    [Fact]
    public async Task A_removed_student_hears_about_the_lesson_as_they_knew_it()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var boris = await StudentAsync(teacher, "Борис", ApiTestExtensions.UniqueEmail("boris"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var start = StartIn(TimeSpan.FromDays(3));
        var lesson = await LessonAsync(teacher, ana.Id, start);
        await RunAsync(DateTime.UtcNow); // Ana is told about "Час по математика" at the first time

        // Boris replaces Ana, and the lesson gets a new title and time in the same edit.
        var moved = start.AddHours(3);
        await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}",
            new { kind = "Individual", studentId = boris.Id, title = "Физика", startLocal = Local(moved), durationMinutes = 60, mode = "Online", version = lesson.Version }));
        await RunAsync(DateTime.UtcNow);

        var cancelled = await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-cancelled", since);
        cancelled.Subject.ShouldContain("Час по математика");
        cancelled.TextBody.ShouldContain(NotificationRules.LocalTime(start, "Europe/Skopje", "mk"));
        cancelled.TextBody.ShouldNotContain("Физика");
    }

    [Fact]
    public async Task A_reminder_found_outdated_is_replaced_when_the_lesson_is_back_at_its_time()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var now = DateTime.UtcNow;
        var since = now.AddSeconds(-1);
        var lesson = await LessonAsync(teacher, ana.Id, StartIn(TimeSpan.FromHours(30)));
        await RunAsync(now.AddHours(5)); // the reminder is planned (pending)

        // As when a booked lesson moved away and the old reminder was found outdated when it came due.
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE Notifications SET Status = 3, Outcome = 'outdated', Version = Version + 1 WHERE LessonId = {lesson.Id} AND Kind = 4", Ct);
        }

        await RunAsync(now.AddHours(7)); // the lesson is (back) at that time: a new reminder

        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-reminder", since);
        (await StatusesAsync(lesson.Id)).Where(s => s.Kind == NotificationKind.LessonReminder).ToList()
            .ShouldBe([(NotificationKind.LessonReminder, NotificationStatus.Suppressed), (NotificationKind.LessonReminder, NotificationStatus.Sent)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_reminder_delayed_into_the_last_hour_is_not_sent()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var now = DateTime.UtcNow;
        var since = now.AddSeconds(-1);
        var start = StartIn(TimeSpan.FromHours(30));
        var lesson = await LessonAsync(teacher, ana.Id, start);
        await RunAsync(now.AddHours(5)); // planned for 24 h before the lesson

        await RunAsync(start.AddMinutes(-30)); // the dispatcher only gets to it half an hour before the lesson

        (await StatusesAsync(lesson.Id)).Single(s => s.Kind == NotificationKind.LessonReminder).Status.ShouldBe(NotificationStatus.Suppressed);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.lesson-reminder", since)).ShouldBe(0);
    }

    [Fact]
    public async Task A_reminder_reaching_the_last_hour_during_a_long_pass_is_not_sent()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var lesson = await LessonAsync(teacher, ana.Id, StartIn(TimeSpan.FromMinutes(95)));

        // The pass plans the reminder at its start (95 minutes ahead), but by the time it is sent 40 minutes have gone by.
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new SlowPassClock())));
        await factory.Services.GetRequiredService<NotificationJob>().RunAtAsync(DateTime.UtcNow, Ct);

        (await StatusesAsync(lesson.Id)).ShouldContain((NotificationKind.LessonReminder, NotificationStatus.Suppressed));
        await fixture.WaitForEmailAsync(ana.Email!, "notify.lesson-scheduled", since);
        (await EmailsAsync(ana.Email!, "notify.lesson-reminder", since)).ShouldBe(0);
    }

    /// <summary>Every look at the elapsed time finds 40 more minutes gone: a pass that takes long.</summary>
    private sealed class SlowPassClock : TimeProvider
    {
        private long looks;

        public override long GetTimestamp() => base.GetTimestamp() + (Interlocked.Increment(ref looks) * TimestampFrequency * 60 * 40);
    }

    [Fact]
    public async Task Reminders_are_planned_for_the_whole_window_page_by_page()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var students = new List<StudentResponse>();
        var lessons = new List<LessonResponse>();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            var student = await StudentAsync(teacher, $"Ученик {i}", ApiTestExtensions.UniqueEmail($"p{i}"));
            students.Add(student);
            lessons.Add(await LessonAsync(teacher, student.Id, StartIn(TimeSpan.FromHours(30 + i))));
        }

        // One lesson per page: every page of the window is read in the same pass.
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Notifications:PlanningPageSize", "1"));
        await factory.Services.GetRequiredService<NotificationJob>().RunAtAsync(now.AddHours(10), Ct);

        foreach (var lesson in lessons)
        {
            (await StatusesAsync(lesson.Id)).ShouldContain((NotificationKind.LessonReminder, NotificationStatus.Sent));
        }
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

        // Further passes keep that outcome: no new reminder rows for an unchanged lesson time.
        await RunAsync(DateTime.UtcNow.AddMinutes(1));
        await RunAsync(DateTime.UtcNow.AddMinutes(2));
        (await StatusesAsync(lesson.Id)).Count(s => s.Kind == NotificationKind.LessonReminder).ShouldBe(1);

        using var anonymous = await ApiClient.CreateAsync(fixture);
        (await anonymous.GetAsync("/api/me/notification-preferences")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task First_preference_saves_at_the_same_moment_all_apply()
    {
        using var learner = await fixture.CreateTeacherAsync(); // any account, without saved choices yet
        var saves = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => learner.Client.PutAsync("/api/me/notification-preferences",
            i % 2 == 0 ? new { lessonReminders = (bool?)false, homeworkEmails = (bool?)null } : new { lessonReminders = (bool?)null, homeworkEmails = (bool?)false })));

        saves.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        foreach (var response in saves)
        {
            response.Dispose();
        }

        (await learner.Client.GetJsonAsync<NotificationPreferencesResponse>("/api/me/notification-preferences")).ShouldBe(new NotificationPreferencesResponse(false, false));
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
    public async Task Homework_removed_and_assigned_again_is_announced_once()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var homework = await HomeworkAsync(teacher, "Задача", ana.Id);
        var first = homework.Assignments.Single();
        await ReadAsync<HomeworkResponse>(await teacher.Client.DeleteAsync($"/api/w/{teacher.WorkspaceId}/homework/{homework.Id}/assignments/{first.Id}"));
        await ReadAsync<HomeworkResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/homework/{homework.Id}/assignments", new { studentIds = new[] { ana.Id } }));

        await RunAsync(DateTime.UtcNow);

        await fixture.WaitForEmailAsync(ana.Email!, "notify.homework-assigned", since);
        await Task.Delay(500, Ct); // the outbox delivers anything left
        (await EmailsAsync(ana.Email!, "notify.homework-assigned", since)).ShouldBe(1);
        (await OutcomesAsync(homework.Id)).ShouldBe([("homework", NotificationStatus.Suppressed), (null, NotificationStatus.Sent)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_pass_drains_a_backlog_larger_than_one_batch()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", ApiTestExtensions.UniqueEmail("ana"));
        var boris = await StudentAsync(teacher, "Борис", ApiTestExtensions.UniqueEmail("boris"));
        var since = DateTime.UtcNow.AddSeconds(-1);
        var homework = await HomeworkAsync(teacher, "Заостанато", ana.Id, boris.Id);

        // Batches of one: both notices (and any other test's due ones) are still sent in this single pass.
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Notifications:BatchSize", "1"));
        await factory.Services.GetRequiredService<NotificationJob>().RunAtAsync(DateTime.UtcNow, Ct);

        (await OutcomesAsync(homework.Id)).ShouldAllBe(o => o.Status == NotificationStatus.Sent);
        await fixture.WaitForEmailAsync(ana.Email!, "notify.homework-assigned", since);
        await fixture.WaitForEmailAsync(boris.Email!, "notify.homework-assigned", since);
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

        // As in production, the database reads with row versioning (READ_COMMITTED_SNAPSHOT): the claim must still lock.
        await using (var db = fixture.CreateDbContext())
        {
            (await db.Database.SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()").SingleAsync(Ct)).ShouldBeTrue();
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
