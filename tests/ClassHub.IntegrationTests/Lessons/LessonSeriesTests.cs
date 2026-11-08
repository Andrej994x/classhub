using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Jobs;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Lessons;

/// <summary>R2-P5: recurring lessons, the 12-week horizon job, conflicts per occurrence and the D-24 edit scopes.</summary>
[Collection(ApiCollection.Name)]
public sealed class LessonSeriesTests(ClassHubApiFixture fixture)
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Tomorrow in the workspace time zone: series start today or later.</summary>
    private static DateOnly Tomorrow => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);

    private static string Series(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/lesson-series{rest}";

    [Fact]
    public async Task A_series_is_created_12_weeks_ahead_and_the_job_extends_it_until_its_end()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");

        var result = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 20));

        result.Created.ShouldBe(12);
        result.Conflicts.ShouldBeEmpty();
        (await CountAsync(result.Series.Id)).ShouldBe(12);
        var job = fixture.Factory.Services.GetRequiredService<LessonSeriesHorizonJob>();
        await job.RunAtAsync(DateTime.UtcNow.AddDays(28), Ct);
        (await CountAsync(result.Series.Id)).ShouldBe(16);
        await job.RunAtAsync(DateTime.UtcNow.AddDays(28), Ct); // idempotent
        (await CountAsync(result.Series.Id)).ShouldBe(16);
        await job.RunAtAsync(DateTime.UtcNow.AddDays(400), Ct);
        (await CountAsync(result.Series.Id)).ShouldBe(20); // ends after 20 lessons

        // Every occurrence keeps the local time and weekday, whatever the season.
        var lessons = await LessonsAsync(teacher, Tomorrow, Tomorrow.AddDays(55));
        lessons.Count.ShouldBe(8);
        lessons.ShouldAllBe(l => l.StartLocal.Hour == 17 && l.StartLocal.Minute == 0 && l.SeriesId == result.Series.Id);
        lessons.Select(l => l.StartLocal.DayOfWeek).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Series_that_cannot_progress_never_starve_the_horizon_job()
    {
        // Blockers first (older ids): a series whose teacher left the workspace and one that ended on a date.
        using var leaver = await fixture.CreateTeacherAsync();
        var leaverStudent = await StudentAsync(leaver, "Ана");
        var orphan = await CreateAsync(leaver, Weekly(leaverStudent.Id, Tomorrow, "17:00", count: null));
        var ended = await CreateAsync(leaver, Weekly(leaverStudent.Id, Tomorrow, "19:00", count: null));
        (await leaver.Client.PostAsync(Series(leaver, $"/{ended.Series.Id}/end"), new { fromDate = Tomorrow.AddDays(30), version = ended.Series.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var db = fixture.CreateDbContext())
        {
            // The teacher leaves: nobody may act for the orphan any more.
            await db.Database.ExecuteSqlAsync($"UPDATE WorkspaceMembers SET RevokedAtUtc = SYSUTCDATETIME() WHERE UserId = {leaver.Me.Id} AND WorkspaceId = {leaver.WorkspaceId}", Ct);
        }

        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var active = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: null));
        (await CountAsync(active.Series.Id)).ShouldBe(12);

        // One series per pass: every pass must still make progress for some series until this one is extended.
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Lessons:HorizonBatchSize", "1"));
        var job = factory.Services.GetRequiredService<LessonSeriesHorizonJob>();
        var later = DateTime.UtcNow.AddDays(70);
        for (var pass = 0; pass < 60 && await CountAsync(active.Series.Id) == 12; pass++)
        {
            await job.RunAtAsync(later, Ct);
        }

        (await CountAsync(active.Series.Id)).ShouldBe(22);
        (await CountAsync(orphan.Series.Id)).ShouldBe(12); // nobody acts for a teacher who left
    }

    [Fact]
    public async Task A_date_ended_series_catches_up_after_the_job_was_down()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var created = await CreateAsync(teacher, new
        {
            kind = "Individual", studentId = ana.Id, title = "x", startDate = Tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1,
            endDate = Tomorrow.AddDays(120), mode = "Online",
        });
        (await CountAsync(created.Series.Id)).ShouldBe(12);

        // The job first runs long after the end date: every occurrence up to it is still created.
        await fixture.Factory.Services.GetRequiredService<LessonSeriesHorizonJob>().RunAtAsync(DateTime.UtcNow.AddDays(200), Ct);

        (await CountAsync(created.Series.Id)).ShouldBe(18); // weeks 0..17 fall on or before the end date
    }

    [Fact]
    public async Task Changing_or_ending_a_series_keeps_a_lesson_that_already_started_today()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var created = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 4));
        // The first lesson started a minute ago and is not marked as held yet.
        await using (var db = fixture.CreateDbContext())
        {
            var started = DateTime.UtcNow.AddMinutes(-1);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE Lessons SET StartUtc = {started}, EndUtc = {started.AddHours(1)} WHERE SeriesId = {created.Series.Id} AND SeriesIndex = 0", Ct);
        }

        await ReadAsync<SeriesResponse>(await teacher.Client.PostAsync(Series(teacher, $"/{created.Series.Id}/end"), new { fromDate = Tomorrow.AddDays(-1), version = created.Series.Version }));

        await using var check = fixture.CreateDbContext();
        (await check.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Lessons WHERE SeriesId = {created.Series.Id} AND SeriesIndex = 0").SingleAsync(Ct)).ShouldBe(1);
        (await CountAsync(created.Series.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_series_edit_reads_its_times_in_the_zone_the_form_showed()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var created = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 3));
        var before = (await LessonsAsync(teacher, Tomorrow, Tomorrow)).ShouldHaveSingleItem().StartUtc;
        (await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new { timeZone = "Europe/London" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The series page shows 17:00 in the series' zone (Skopje); renaming keeps the instants.
        var renamed = await ReadAsync<SeriesResultResponse>(await teacher.Client.PutAsync(Series(teacher, $"/{created.Series.Id}"), new
        {
            scope = "AllFuture", version = created.Series.Version, timeZone = "Europe/Skopje", series = Weekly(ana.Id, Tomorrow, "17:00", count: 3),
        }));
        (await LessonsAsync(teacher, Tomorrow, Tomorrow)).ShouldHaveSingleItem().StartUtc.ShouldBe(before);

        // Only the series' zone or the workspace's current one.
        using var other = await teacher.Client.PutAsync(Series(teacher, $"/{renamed.Series.Id}"), new
        {
            scope = "AllFuture", version = renamed.Series.Version, timeZone = "America/New_York", series = Weekly(ana.Id, Tomorrow, "17:00", count: 3),
        });
        other.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await other.ReadJsonAsync()).GetProperty("errors").TryGetProperty("timeZone", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task This_and_following_cuts_at_the_lesson_itself_whatever_the_calendars_say()
    {
        var pagoPago = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Pago_Pago"); // UTC-11
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pagoPago);
        Assert.SkipWhen(localNow.TimeOfDay > new TimeSpan(23, 40, 0), "Tonight's lesson must still be ahead.");
        var todayThere = DateOnly.FromDateTime(localNow);
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        (await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new { timeZone = "Pacific/Pago_Pago" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await CreateAsync(teacher, Weekly(ana.Id, todayThere, "23:50", count: 3));
        var tonight = (await LessonsAsync(teacher, todayThere, todayThere)).ShouldHaveSingleItem();

        // The workspace moves to Kiritimati (UTC+14): there, tonight's lesson is already two calendar days later.
        (await teacher.Client.PatchAsync($"/api/w/{teacher.WorkspaceId}", new { timeZone = "Pacific/Kiritimati" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var shown = await teacher.Client.GetJsonAsync<LessonResponse>($"/api/w/{teacher.WorkspaceId}/lessons/{tonight.Id}");
        var shownDate = DateOnly.FromDateTime(shown.StartLocal);
        await ReadAsync<SeriesResultResponse>(await teacher.Client.PutAsync(Series(teacher, $"/{created.Series.Id}"), new
        {
            scope = "ThisAndFollowing", fromLessonId = tonight.Id, version = created.Series.Version, timeZone = "Pacific/Kiritimati",
            series = Weekly(ana.Id, shownDate, $"{shown.StartLocal.AddHours(1):HH:mm}", count: 3),
        }));

        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Lessons WHERE Id = {tonight.Id}").SingleAsync(Ct)).ShouldBe(0); // replaced
        (await CountAsync(created.Series.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task An_occurrence_that_overlaps_another_lesson_is_skipped_and_reported()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var busy = Tomorrow.AddDays(14);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons",
            new { kind = "Individual", studentId = boris.Id, title = "Друг час", startLocal = $"{busy:yyyy-MM-dd}T17:30:00", durationMinutes = 60, mode = "Online" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var result = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 5));

        result.Created.ShouldBe(4);
        result.Conflicts.ShouldHaveSingleItem().ShouldBe(new SeriesSkipResponse(busy, "Overlap"));
        (await teacher.Client.GetJsonAsync<SeriesResponse>(Series(teacher, $"/{result.Series.Id}"))).Skips.ShouldHaveSingleItem().Date.ShouldBe(busy);
    }

    [Fact]
    public async Task This_and_following_keeps_earlier_held_cancelled_and_individually_changed_lessons()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var created = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 8));
        var lessons = await LessonsAsync(teacher, Tomorrow, Tomorrow.AddDays(55));
        lessons.Count.ShouldBe(8);

        // Lesson 6 is moved on its own, lesson 7 is cancelled.
        var moved = lessons[5];
        (await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{moved.Id}", new
        {
            kind = "Individual", studentId = ana.Id, title = "Поместен", startLocal = $"{moved.StartLocal:yyyy-MM-dd}T09:00:00", durationMinutes = 60, mode = "Online", version = moved.Version,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lessons[6].Id}/cancel", new { version = lessons[6].Version, reason = "Празник" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // From lesson 5 on: an hour later, until the original end.
        var from = lessons[4];
        var fromDate = DateOnly.FromDateTime(from.StartLocal);
        var edit = await ReadAsync<SeriesResultResponse>(await teacher.Client.PutAsync(Series(teacher, $"/{created.Series.Id}"), new
        {
            scope = "ThisAndFollowing", fromLessonId = from.Id, version = created.Series.Version,
            series = Weekly(ana.Id, fromDate, "18:00", count: 4),
        }));

        edit.Series.Id.ShouldNotBe(created.Series.Id);
        var old = await teacher.Client.GetJsonAsync<SeriesResponse>(Series(teacher, $"/{created.Series.Id}"));
        old.ReplacedBySeriesId.ShouldBe(edit.Series.Id);
        old.EndDate.ShouldBe(fromDate.AddDays(-1));

        var after = await LessonsAsync(teacher, Tomorrow, Tomorrow.AddDays(55));
        after.Where(l => DateOnly.FromDateTime(l.StartLocal) < fromDate).ShouldAllBe(l => l.StartLocal.Hour == 17 && l.SeriesId == created.Series.Id);
        after.Single(l => l.Id == moved.Id).StartLocal.Hour.ShouldBe(9); // changed on its own: untouched
        after.Single(l => l.Id == lessons[6].Id).Status.ShouldBe("Cancelled"); // final: untouched
        var continued = after.Where(l => l.SeriesId == edit.Series.Id).ToList();
        continued.Count.ShouldBe(4);
        continued.ShouldAllBe(l => l.StartLocal.Hour == 18);
        // The new series' lessons do not overlap the kept ones (they are an hour later); nothing was skipped.
        edit.Conflicts.ShouldBeEmpty();
    }

    [Fact]
    public async Task All_future_replaces_every_upcoming_lesson_and_a_failing_edit_changes_nothing()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var created = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 4));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{boris.Id}/archive", new { version = boris.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Atomic: the new student is archived, so the whole edit is refused and the series stays as it was.
        using (var refused = await teacher.Client.PutAsync(Series(teacher, $"/{created.Series.Id}"), new
        {
            scope = "AllFuture", version = created.Series.Version, series = Weekly(boris.Id, Tomorrow, "16:00", count: 4),
        }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await CountAsync(created.Series.Id)).ShouldBe(4);
        (await teacher.Client.GetJsonAsync<SeriesResponse>(Series(teacher, $"/{created.Series.Id}"))).ReplacedBySeriesId.ShouldBeNull();

        var edit = await ReadAsync<SeriesResultResponse>(await teacher.Client.PutAsync(Series(teacher, $"/{created.Series.Id}"), new
        {
            scope = "AllFuture", version = created.Series.Version, series = Weekly(ana.Id, Tomorrow.AddDays(1), "16:00", count: 4),
        }));

        edit.Created.ShouldBe(4);
        (await CountAsync(created.Series.Id)).ShouldBe(0); // nothing of the old series had happened yet
        (await LessonsAsync(teacher, Tomorrow, Tomorrow.AddDays(30))).ShouldAllBe(l => l.SeriesId == edit.Series.Id && l.StartLocal.Hour == 16);

        // The replaced series cannot be edited again; a stale version conflicts.
        using (var replaced = await teacher.Client.PostAsync(Series(teacher, $"/{created.Series.Id}/end"), new { fromDate = Tomorrow, version = created.Series.Version + 1 }))
        {
            (await replaced.ErrorCodeAsync()).ShouldBe(ErrorCodes.InvalidStatusTransition);
        }

        (await teacher.Client.PostAsync(Series(teacher, $"/{edit.Series.Id}/end"), new { fromDate = Tomorrow, version = 99 })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ending_a_series_removes_its_upcoming_lessons_from_that_date()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var created = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: null));
        (await CountAsync(created.Series.Id)).ShouldBe(12);

        var ended = await ReadAsync<SeriesResponse>(await teacher.Client.PostAsync(Series(teacher, $"/{created.Series.Id}/end"), new { fromDate = Tomorrow.AddDays(21), version = created.Series.Version }));

        ended.EndDate.ShouldBe(Tomorrow.AddDays(20));
        (await CountAsync(created.Series.Id)).ShouldBe(3);
        await fixture.Factory.Services.GetRequiredService<LessonSeriesHorizonJob>().RunAtAsync(DateTime.UtcNow.AddDays(100), Ct);
        (await CountAsync(created.Series.Id)).ShouldBe(3); // the job respects the end
    }

    [Fact]
    public async Task Two_series_for_the_same_slot_created_in_parallel_never_double_book()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");

        var responses = await Task.WhenAll(
            teacher.Client.PostAsync(Series(teacher), Weekly(ana.Id, Tomorrow, "17:00", count: 6)),
            teacher.Client.PostAsync(Series(teacher), Weekly(boris.Id, Tomorrow, "17:30", count: 6)));
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<SeriesResultResponse>(Ct)));

        results.Sum(r => r!.Created).ShouldBe(6);
        results.Sum(r => r!.Conflicts.Count).ShouldBe(6);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Series_requests_are_validated_and_isolated()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");

        (await teacher.Client.PostAsync(Series(teacher), Weekly(ana.Id, Tomorrow.AddDays(-3), "17:00", count: 3))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Series(teacher), new
        {
            kind = "Individual", studentId = ana.Id, title = "x", startDate = Tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 3, mode = "Online",
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var both = await teacher.Client.PostAsync(Series(teacher), new
        {
            kind = "Individual", studentId = ana.Id, title = "x", startDate = Tomorrow, startTime = "17:00:00", durationMinutes = 60, intervalWeeks = 1,
            endDate = Tomorrow.AddDays(30), count = 3, mode = "Online",
        }))
        {
            (await both.ReadJsonAsync()).GetProperty("errors").TryGetProperty("endDate", out _).ShouldBeTrue();
        }

        var created = await CreateAsync(teacher, Weekly(ana.Id, Tomorrow, "17:00", count: 2));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Series(teacher, $"/{created.Series.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, Series(teacher, $"/{created.Series.Id}/end"), new { fromDate = Tomorrow, version = created.Series.Version });
        (await intruder.Client.GetAsync(Series(intruder, $"/{created.Series.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PostAsync(Series(intruder), Weekly(ana.Id, Tomorrow, "17:00", count: 2))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // not their student
    }

    private static object Weekly(Guid studentId, DateOnly start, string time, int? count) => new
    {
        kind = "Individual", studentId, title = "Математика", startDate = start, startTime = $"{time}:00", durationMinutes = 60, intervalWeeks = 1, count, mode = "Online",
    };

    private async Task<int> CountAsync(Guid seriesId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Lessons WHERE SeriesId = {seriesId}").SingleAsync(Ct);
    }

    private static Task<List<LessonResponse>> LessonsAsync(TeacherSession teacher, DateOnly from, DateOnly to) =>
        teacher.Client.GetJsonAsync<List<LessonResponse>>($"/api/w/{teacher.WorkspaceId}/lessons?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }

    private static async Task<SeriesResultResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(Series(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<SeriesResultResponse>(Ct))!;
    }

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }
}
