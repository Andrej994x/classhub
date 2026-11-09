using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Dashboard;
using ClassHub.Core.Modules.Finance.Contracts;
using ClassHub.Core.Modules.Finance.Jobs;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Dashboard;

/// <summary>
/// R2-P12: the teacher's teaching numbers (lessons ahead, active students, money per currency) and the student's own
/// lessons in the portal, each for the signed-in user only.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TeachingDashboardTests(ClassHubApiFixture fixture)
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_teacher_starts_with_nothing_ahead_and_six_empty_months()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var dashboard = await TeachingAsync(teacher);

        (dashboard.Lessons.Next7Days, dashboard.ActiveStudents).ShouldBe((0, 0));
        dashboard.Lessons.Next.ShouldBeEmpty();
        (dashboard.Finance.StudentsOwing, dashboard.Finance.RemainingLessons, dashboard.Finance.UnpaidLessons).ShouldBe((0, 0, 0));
        dashboard.Finance.Outstanding.ShouldBeEmpty();
        dashboard.Finance.RunningLow.ShouldBeEmpty();
        dashboard.Finance.Income.Count.ShouldBe(6);
        dashboard.Finance.Income[^1].Month.ShouldBe(Today().ToString("yyyy-MM", CultureInfo.InvariantCulture));
        dashboard.Finance.Income.ShouldAllBe(m => m.Received.Count == 0);
    }

    [Fact]
    public async Task The_teaching_numbers_match_the_workspaces_own_lessons_students_and_money()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана", "Петрова");
        var boris = await StudentAsync(teacher, "Борис", null);
        var cveta = await StudentAsync(teacher, "Цвета", null);

        // Lessons: two within the next 7 days (in start order), one later, one cancelled, one in the past.
        var soon = await LessonAsync(teacher, ana.Id, LocalIn(TimeSpan.FromDays(2)));
        var later = await LessonAsync(teacher, boris.Id, LocalIn(TimeSpan.FromDays(3)));
        await LessonAsync(teacher, ana.Id, LocalIn(TimeSpan.FromDays(10)));
        var cancelled = await LessonAsync(teacher, boris.Id, LocalIn(TimeSpan.FromDays(1)));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{cancelled.Id}/cancel", new { version = cancelled.Version, reason = "Болест" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Money: Ana owes MKD, Boris EUR (one lesson left), Cveta had a lesson held without a package and is archived.
        await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
        await AssignAsync(teacher, boris.Id, new { title = "Час", lessonCount = 1, priceMinor = 5_000, currency = "EUR" });
        var today = Today();
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        await PayAsync(teacher, ana.Id, new { amountMinor = 200_000, currency = "MKD", paidOn = today, method = "Cash" });
        await PayAsync(teacher, ana.Id, new { amountMinor = 100_000, currency = "MKD", paidOn = lastMonth, method = "BankTransfer" });
        var held = await LessonAsync(teacher, cveta.Id, LocalIn(TimeSpan.FromDays(-3)));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{held.Id}/complete", new { version = held.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await fixture.Factory.Services.GetRequiredService<LessonConsumptionJob>().RunOnceAsync(Ct);
        var current = await teacher.Client.GetJsonAsync<StudentResponse>($"/api/w/{teacher.WorkspaceId}/students/{cveta.Id}");
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{cveta.Id}/archive", new { version = current.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var dashboard = await TeachingAsync(teacher);

        dashboard.Lessons.Next7Days.ShouldBe(2);
        dashboard.Lessons.Next.Select(l => (l.Id, l.Students.Single())).ShouldBe([(soon.Id, "Ана Петрова"), (later.Id, "Борис")]);
        dashboard.ActiveStudents.ShouldBe(2);
        dashboard.Finance.Outstanding.ShouldBe([new CurrencyAmountResponse("EUR", 5_000), new CurrencyAmountResponse("MKD", 180_000)]);
        dashboard.Finance.StudentsOwing.ShouldBe(2);
        dashboard.Finance.RemainingLessons.ShouldBe(5);
        dashboard.Finance.RunningLow.ShouldBe([new PackageRunningLowResponse(boris.Id, "Борис", 1)]);
        dashboard.Finance.StudentsRunningLow.ShouldBe(1);
        dashboard.Finance.UnpaidLessons.ShouldBe(1);
        dashboard.Finance.Income[^1].Received.ShouldBe([new CurrencyAmountResponse("MKD", 200_000)]);
        dashboard.Finance.Income[^2].Month.ShouldBe(lastMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        dashboard.Finance.Income[^2].Received.ShouldBe([new CurrencyAmountResponse("MKD", 100_000)]);

        // Another teacher sees none of it, and cannot read this workspace's numbers.
        using var other = await fixture.CreateTeacherAsync();
        var theirs = await TeachingAsync(other);
        (theirs.Lessons.Next7Days, theirs.ActiveStudents, theirs.Finance.Outstanding.Count, theirs.Finance.RemainingLessons).ShouldBe((0, 0, 0, 0));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(other.Client, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/dashboard/teaching");
    }

    [Fact]
    public async Task Every_student_running_low_is_counted_although_only_the_first_are_listed()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var admin = await fixture.CreatePlatformStaffAsync(PlatformRoles.Admin);
        using (var pro = await admin.Client.PostAsync($"/api/admin/workspaces/{teacher.WorkspaceId}/subscription/pro", new
        {
            months = 1, amountMinor = 60_000, currency = "MKD", paymentMethod = "bank-transfer", paymentReference = "INV-DASH", paidOn = Today(), note = "Test",
        }))
        {
            pro.StatusCode.ShouldBe(HttpStatusCode.OK, await pro.Content.ReadAsStringAsync(Ct));
        }

        // More students with one lesson left than the dashboard lists.
        const int students = 12;
        for (var i = 0; i < students; i++)
        {
            var student = await StudentAsync(teacher, $"Ученик {i:D2}", null);
            await AssignAsync(teacher, student.Id, new { title = "Час", lessonCount = 1, priceMinor = 1_000, currency = "MKD" });
        }

        var finance = (await TeachingAsync(teacher)).Finance;
        finance.StudentsRunningLow.ShouldBe(students);
        finance.RunningLow.Count.ShouldBe(10);
    }

    [Fact]
    public async Task A_student_sees_only_their_own_lessons_attendance_and_shared_summaries()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var anaAccount = await fixture.CreateTeacherAsync();
        var ana = await LinkedStudentAsync(teacher, anaAccount, "Ана");
        var boris = await StudentAsync(teacher, "Борис", null);

        // Ahead: Ana's online lesson (with its link), Boris's own lesson and a cancelled one of Ana's (neither shown).
        var next = await LessonAsync(teacher, ana, LocalIn(TimeSpan.FromDays(2)), meetingUrl: "https://meet.example.com/ana");
        await LessonAsync(teacher, boris.Id, LocalIn(TimeSpan.FromDays(1)));
        var cancelled = await LessonAsync(teacher, ana, LocalIn(TimeSpan.FromDays(4)));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{cancelled.Id}/cancel", new { version = cancelled.Version, reason = "Болест" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Past: one held with Ana present (a shared summary and a private note), one held with Ana absent, one not recorded.
        var present = await LessonAsync(teacher, ana, LocalIn(TimeSpan.FromDays(-7)));
        var presentHeld = await ReadAsync<LessonResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{present.Id}/complete", new { version = present.Version }));
        (await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{present.Id}/notes",
            new { version = presentHeld.Version, privateNote = "Тајна белешка", sharedSummary = "Вежбавме дропки" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var absent = await LessonAsync(teacher, ana, LocalIn(TimeSpan.FromDays(-5)));
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{absent.Id}/complete",
            new { version = absent.Version, attendance = new[] { new { studentId = ana, status = "Absent" } } })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var unrecorded = await LessonAsync(teacher, ana, LocalIn(TimeSpan.FromDays(-1)));

        using var response = await anaAccount.Client.GetAsync($"/api/student/w/{teacher.WorkspaceId}/lessons");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain("Тајна белешка");
        body.ShouldNotContain("Борис");
        var lessons = (await response.Content.ReadFromJsonAsync<StudentLessonsResponse>(Ct))!;

        var upcoming = lessons.Upcoming.ShouldHaveSingleItem();
        (upcoming.Id, upcoming.MeetingUrl, upcoming.Attendance).ShouldBe((next.Id, "https://meet.example.com/ana", null));
        lessons.Past.Select(l => (l.Id, l.Status, l.Attendance, l.SharedSummary)).ShouldBe([
            (unrecorded.Id, LessonStatus.Scheduled, null, null),
            (absent.Id, LessonStatus.Completed, AttendanceStatus.Absent, null),
            (present.Id, LessonStatus.Completed, AttendanceStatus.Present, "Вежбавме дропки"),
        ]);
        lessons.Past.ShouldAllBe(l => l.MeetingUrl == null);
        lessons.Attendance.ShouldBe(new StudentAttendanceResponse(2, 1, 1, 0));

        // Another workspace's portal, and the teacher's route, are closed to the student; a teacher is not a student here.
        using var other = await fixture.CreateTeacherAsync();
        (await anaAccount.Client.GetAsync($"/api/student/w/{other.WorkspaceId}/lessons")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(anaAccount.Client, HttpMethod.Get, $"/api/w/{teacher.WorkspaceId}/dashboard/teaching");
        (await teacher.Client.GetAsync($"/api/student/w/{teacher.WorkspaceId}/lessons")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje));

    /// <summary>A local start this far from now, on a whole hour.</summary>
    private static string LocalIn(TimeSpan delay)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow + delay, Skopje);
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static async Task<TeachingDashboardResponse> TeachingAsync(TeacherSession teacher) =>
        await teacher.Client.GetJsonAsync<TeachingDashboardResponse>($"/api/w/{teacher.WorkspaceId}/dashboard/teaching");

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName, string? lastName)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName, lastName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    private async Task<Guid> LinkedStudentAsync(TeacherSession teacher, TeacherSession learner, string firstName)
    {
        using var created = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName, email = learner.Email });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var student = (await created.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
        var since = DateTime.UtcNow.AddMilliseconds(-5);
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students/{student.Id}/invitation", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await fixture.WaitForEmailAsync(learner.Email, "student.invitation", since)).FragmentValue("token");
        using var accept = await learner.Client.PostAsync("/api/student-invitations/accept", new { token });
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync(Ct));
        return student.Id;
    }

    private static async Task<LessonResponse> LessonAsync(TeacherSession teacher, Guid studentId, string startLocal, string? meetingUrl = null)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons",
            new { kind = "Individual", studentId, title = "Час по математика", startLocal, durationMinutes = 60, mode = "Online", meetingUrl });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private static async Task<StudentFinanceResponse> AssignAsync(TeacherSession teacher, Guid studentId, object body) =>
        await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/finance/students/{studentId}/packages", body));

    private static async Task<StudentFinanceResponse> PayAsync(TeacherSession teacher, Guid studentId, object body) =>
        await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/finance/students/{studentId}/payments", body));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }
}
