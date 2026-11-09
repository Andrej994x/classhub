using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Finance.Contracts;
using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.Modules.Finance.Jobs;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Finance;

/// <summary>
/// R2-P10: packages with snapshotted terms, consumption from held lessons (once, D-22/D-25), reversals, payments,
/// corrections, currencies kept apart, the report, isolation and the audit trail.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinanceTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Path(TeacherSession t, string rest = "") => $"/api/w/{t.WorkspaceId}/finance{rest}";

    [Fact]
    public async Task A_package_copies_the_offer_terms_and_later_price_changes_do_not_alter_it()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var offer = await OfferAsync(teacher, lessons: 4, price: 480_000);

        var finance = await AssignAsync(teacher, ana.Id, new { offerId = offer.Id });
        var package = finance.Packages.ShouldHaveSingleItem();
        package.Title.ShouldBe("Пакет математика");
        package.LessonCount.ShouldBe(4);
        package.RemainingLessons.ShouldBe(4);
        package.PriceMinor.ShouldBe(480_000);
        package.Currency.ShouldBe("MKD");
        finance.Balances.ShouldHaveSingleItem().OutstandingMinor.ShouldBe(480_000);

        // The teacher raises the price: the student's package keeps what was agreed.
        await ReadAsync<OfferResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/offers/{offer.Id}",
            OfferBody(await MathAsync(teacher), lessons: 4, price: 600_000, version: offer.Version)));
        (await GetAsync(teacher, ana.Id)).Packages.Single().PriceMinor.ShouldBe(480_000);

        // A custom package needs complete terms.
        var custom = await AssignAsync(teacher, ana.Id, new { title = "Интензивен курс", lessonCount = 10, priceMinor = 25_000, currency = "EUR" });
        custom.Packages.Select(p => p.Currency).ShouldBe(["EUR", "MKD"]);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/packages"), new { title = "x", lessonCount = 0, priceMinor = 1, currency = "MKD" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/packages"), new { title = "x", lessonCount = 1, priceMinor = 1, currency = "XYZ" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/packages"), new { offerId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_held_lesson_consumes_one_lesson_per_present_student_and_attendance_changes_reverse_it()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
        await AssignAsync(teacher, boris.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
        var lesson = await GroupLessonAsync(teacher, ana.Id, boris.Id);

        var held = await CompleteAsync(teacher, lesson, new[] { new { studentId = boris.Id, status = "Absent" } });
        await ProcessAsync();

        (await GetAsync(teacher, ana.Id)).Packages.Single().RemainingLessons.ShouldBe(3);
        (await GetAsync(teacher, boris.Id)).Packages.Single().RemainingLessons.ShouldBe(4); // absent: nothing (D-22)

        // Ana was absent after all, Boris present: Ana's lesson is given back, Boris's consumed.
        var corrected = await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}/attendance",
            new { version = held.Version, attendance = new[] { new { studentId = ana.Id, status = "Absent" }, new { studentId = boris.Id, status = "Present" } } }));
        await ProcessAsync();
        var anaFinance = await GetAsync(teacher, ana.Id);
        anaFinance.Packages.Single().RemainingLessons.ShouldBe(4);
        anaFinance.Entries.Where(e => e.LessonId == lesson.Id).Select(e => e.Kind).ShouldBe(["LessonReversed", "LessonConsumed"]);
        (await GetAsync(teacher, boris.Id)).Packages.Single().RemainingLessons.ShouldBe(3);

        // And back: Ana present again consumes again (a new entry; the history keeps all three).
        await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}/attendance",
            new { version = corrected.Version, attendance = new[] { new { studentId = ana.Id, status = "Present" }, new { studentId = boris.Id, status = "Present" } } }));
        await ProcessAsync();
        anaFinance = await GetAsync(teacher, ana.Id);
        anaFinance.Packages.Single().RemainingLessons.ShouldBe(3);
        anaFinance.Entries.Count(e => e.LessonId == lesson.Id).ShouldBe(3);
        (await LessonEventsPendingAsync(teacher.WorkspaceId)).ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_processing_of_a_held_lesson_consumes_it_once()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
        var lesson = await IndividualLessonAsync(teacher, ana.Id);

        // Parallel completions (one transition and one event) processed by parallel workers.
        var completions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}/complete", new { version = lesson.Version })));
        completions.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        foreach (var response in completions)
        {
            response.Dispose();
        }

        var job = fixture.Factory.Services.GetRequiredService<LessonConsumptionJob>();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => job.RunOnceAsync(Ct), Ct)));
        await ProcessAsync();

        var finance = await GetAsync(teacher, ana.Id);
        finance.Packages.Single().RemainingLessons.ShouldBe(3);
        finance.Entries.Count(e => e.Kind == "LessonConsumed").ShouldBe(1);
        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM LessonLedger WHERE LessonId = {lesson.Id}").SingleAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Two_events_of_one_lesson_handled_at_the_same_time_consume_once()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var students = new List<Guid>();
        var lessons = new List<LessonResponse>();
        for (var i = 0; i < 4; i++)
        {
            var student = await StudentAsync(teacher, $"Ученик {i}");
            await AssignAsync(teacher, student.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
            var lesson = await IndividualLessonAsync(teacher, student.Id, $"2024-04-0{i + 1}T10:00:00");
            // Held, then the attendance saved again: two pending events for the same lesson.
            var held = await CompleteAsync(teacher, lesson, Array.Empty<object>());
            await ReadAsync<LessonResponse>(await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}/attendance",
                new { version = held.Version, attendance = new[] { new { studentId = student.Id, status = "Present" } } }));
            students.Add(student.Id);
            lessons.Add(lesson);
        }

        // Each event in its own worker, all at once (the outbox mark alone cannot serialize different events).
        IReadOnlyList<PendingLessonEvent> pending;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            pending = [.. (await scope.ServiceProvider.GetRequiredService<ILessonEventFeed>().PendingAsync(10_000, Ct)).Where(e => e.WorkspaceId == teacher.WorkspaceId)];
        }

        pending.Count.ShouldBe(8);
        await Task.WhenAll(pending.Select(e => Task.Run(async () =>
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(e.WorkspaceId, e.TeacherUserId, ClassHub.Core.SharedKernel.Tenancy.WorkspaceRole.Owner);
            await scope.ServiceProvider.GetRequiredService<ClassHub.Core.Modules.Finance.Application.LessonConsumption>().ProcessAsync(e, Ct);
        }, Ct)));
        await ProcessAsync();

        foreach (var student in students)
        {
            var finance = await GetAsync(teacher, student);
            finance.Packages.Single().RemainingLessons.ShouldBe(3);
            finance.Entries.Count(e => e.Kind == "LessonConsumed").ShouldBe(1);
        }

        (await LessonEventsPendingAsync(teacher.WorkspaceId)).ShouldBe(0);
    }

    [Fact]
    public async Task Without_an_open_package_a_present_student_gets_an_unpaid_lesson_and_cancelled_lessons_never_consume()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        var finance = await AssignAsync(teacher, ana.Id, new { title = "Еден час", lessonCount = 1, priceMinor = 120_000, currency = "MKD" });

        // D-25: in a group lesson, Ana uses her package and Boris (no package) is recorded as unpaid.
        await CompleteAsync(teacher, await GroupLessonAsync(teacher, ana.Id, boris.Id), Array.Empty<object>());
        await ProcessAsync();
        (await GetAsync(teacher, ana.Id)).Packages.Single().RemainingLessons.ShouldBe(0);
        (await GetAsync(teacher, boris.Id)).UnpaidLessons.ShouldBe(1);

        // Ana's package is used up: her next lesson is unpaid too; a closed package is not used either.
        await CompleteAsync(teacher, await IndividualLessonAsync(teacher, ana.Id, "2024-03-19T10:00:00"), Array.Empty<object>());
        var withNew = await AssignAsync(teacher, ana.Id, new { title = "Нов пакет", lessonCount = 4, priceMinor = 400_000, currency = "MKD" });
        var newPackage = withNew.Packages.Single(p => p.Title == "Нов пакет");
        await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/packages/{newPackage.Id}/close"), new { version = newPackage.Version }));
        await ProcessAsync();
        var after = await GetAsync(teacher, ana.Id);
        after.UnpaidLessons.ShouldBe(1);
        after.Packages.Single(p => p.Id == newPackage.Id).RemainingLessons.ShouldBe(4);

        // A cancelled lesson never consumes.
        var cancelled = await IndividualLessonAsync(teacher, boris.Id, "2024-03-20T10:00:00");
        (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{cancelled.Id}/cancel", new { version = cancelled.Version, reason = "Болен" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await ProcessAsync();
        (await GetAsync(teacher, boris.Id)).UnpaidLessons.ShouldBe(1);
        finance.StudentId.ShouldBe(ana.Id);
    }

    [Fact]
    public async Task Partial_payments_and_reversals_keep_the_balance_per_currency()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var mkd = (await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" })).Packages.Single();
        await AssignAsync(teacher, ana.Id, new { title = "Online course", lessonCount = 2, priceMinor = 10_000, currency = "EUR" });

        await PayAsync(teacher, ana.Id, new { amountMinor = 200_000, currency = "MKD", paidOn = "2026-11-01", method = "Cash", packageId = mkd.Id });
        var paid = await PayAsync(teacher, ana.Id, new { amountMinor = 100_000, currency = "MKD", paidOn = "2026-11-05", method = "BankTransfer", note = "Втор дел" });
        var mkdBalance = paid.Balances.Single(b => b.Currency == "MKD");
        mkdBalance.PaidMinor.ShouldBe(300_000);
        mkdBalance.OutstandingMinor.ShouldBe(180_000);
        paid.Balances.Single(b => b.Currency == "EUR").OutstandingMinor.ShouldBe(10_000); // never mixed with MKD

        // A mistaken payment is reversed (once), never edited.
        var mistaken = paid.Entries.First(e => e.Kind == "Payment" && e.AmountMinor == 100_000);
        mistaken.Reversible.ShouldBeTrue();
        var reversed = await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/payments/{mistaken.Id}/reverse"), new { reason = "Двапати внесено" }));
        reversed.Balances.Single(b => b.Currency == "MKD").OutstandingMinor.ShouldBe(280_000);
        reversed.Entries.Single(e => e.Kind == "PaymentReversal").AmountMinor.ShouldBe(-100_000);
        reversed.Entries.Single(e => e.Id == mistaken.Id).Reversible.ShouldBeFalse();
        using (var again = await teacher.Client.PostAsync(Path(teacher, $"/payments/{mistaken.Id}/reverse"), new { reason = "Пак" }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // No negative or zero payments; the package's currency must match; no reason, no reversal.
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/payments"), new { amountMinor = -1, currency = "MKD", paidOn = "2026-11-01", method = "Cash" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/payments"), new { amountMinor = 100, currency = "EUR", paidOn = "2026-11-01", method = "Cash", packageId = mkd.Id })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/payments"), new { amountMinor = 100, currency = "MKD", paidOn = "2026-11-01", method = "Cheque" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/payments"), new { amountMinor = 100, currency = "MKD", paidOn = "2099-01-01", method = "Cash" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/payments/{mistaken.Id}/reverse"), new { reason = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Corrections_have_reasons_stay_in_range_and_are_traceable()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var package = (await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 2, priceMinor = 240_000, currency = "MKD" })).Packages.Single();

        // A no-show that counts (D-22), then a discount.
        var noShow = await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/packages/{package.Id}/lesson-adjustments"), new { quantity = 1, reason = "Не дојде без најава" }));
        noShow.Packages.Single().RemainingLessons.ShouldBe(1);
        var discounted = await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/adjustments"), new { amountMinor = -40_000, currency = "MKD", reason = "Попуст за брат/сестра" }));
        discounted.Balances.Single().OutstandingMinor.ShouldBe(200_000);
        discounted.Entries.Single(e => e.Kind == "Adjustment").Note.ShouldBe("Попуст за брат/сестра");

        using (var tooMany = await teacher.Client.PostAsync(Path(teacher, $"/packages/{package.Id}/lesson-adjustments"), new { quantity = 2, reason = "Премногу" }))
        {
            tooMany.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await tooMany.ErrorCodeAsync()).ShouldBe(ErrorCodes.PackageLessonsOutOfRange);
        }

        (await teacher.Client.PostAsync(Path(teacher, $"/packages/{package.Id}/lesson-adjustments"), new { quantity = 1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.PostAsync(Path(teacher, $"/students/{ana.Id}/adjustments"), new { amountMinor = 0, currency = "MKD", reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The correction is taken back with a reason; the reversal itself cannot be reversed.
        var entry = noShow.Entries.Single(e => e.Kind == "LessonAdjusted");
        var undone = await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/ledger/{entry.Id}/reverse"), new { reason = "Сепак беше болен" }));
        undone.Packages.Single().RemainingLessons.ShouldBe(2);
        var reversal = undone.Entries.Single(e => e.Kind == "LessonReversed");
        (await teacher.Client.PostAsync(Path(teacher, $"/ledger/{reversal.Id}/reverse"), new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await teacher.Client.PostAsync(Path(teacher, $"/ledger/{entry.Id}/reverse"), new { reason = "Пак" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Every change is in the audit log with its actor.
        await using var db = fixture.CreateDbContext();
        var actions = await db.Database.SqlQuery<string>($"SELECT [Action] AS [Value] FROM AuditLog WHERE WorkspaceId = {teacher.WorkspaceId} AND [Action] LIKE 'finance.%' AND ActorUserId = {teacher.Me.Id}").ToListAsync(Ct);
        actions.ShouldBe(["finance.package.assigned", "finance.lessons.adjusted", "finance.balance.adjusted", "finance.lessons.reversed"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_ledger_is_append_only()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var paid = await PayAsync(teacher, ana.Id, new { amountMinor = 1_000, currency = "MKD", paidOn = "2026-11-01", method = "Cash" });

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(teacher.WorkspaceId, teacher.Me.Id, ClassHub.Core.SharedKernel.Tenancy.WorkspaceRole.Owner);
        var db = scope.ServiceProvider.GetRequiredService<ClassHub.Core.Infrastructure.Persistence.ClassHubDbContext>();
        var payment = await db.Set<Payment>().SingleAsync(p => p.Id == paid.Entries.Single().Id, Ct);

        payment.AmountMinor = 1;
        await Should.ThrowAsync<AppendOnlyViolationException>(() => db.SaveChangesAsync(Ct));
        db.ChangeTracker.Clear();
        db.Set<Payment>().Remove(await db.Set<Payment>().SingleAsync(p => p.Id == payment.Id, Ct));
        await Should.ThrowAsync<AppendOnlyViolationException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task The_overview_and_the_period_report_keep_currencies_apart()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var boris = await StudentAsync(teacher, "Борис");
        await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
        await AssignAsync(teacher, boris.Id, new { title = "Course", lessonCount = 2, priceMinor = 10_000, currency = "EUR" });
        await PayAsync(teacher, ana.Id, new { amountMinor = 200_000, currency = "MKD", paidOn = "2026-11-02", method = "Cash" });
        await PayAsync(teacher, boris.Id, new { amountMinor = 5_000, currency = "EUR", paidOn = "2026-10-15", method = "Card" });
        await CompleteAsync(teacher, await IndividualLessonAsync(teacher, ana.Id), Array.Empty<object>());
        await ProcessAsync();

        var overview = await teacher.Client.GetJsonAsync<FinanceOverviewResponse>(Path(teacher, "/overview"));
        overview.Students.Select(s => s.Name).ShouldBe(["Ана", "Борис"]);
        overview.Students[0].Balances.ShouldHaveSingleItem().OutstandingMinor.ShouldBe(280_000);
        overview.Students[0].RemainingLessons.ShouldBe(3);
        overview.Students[1].Balances.ShouldHaveSingleItem().Currency.ShouldBe("EUR");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await teacher.Client.GetJsonAsync<FinanceReportResponse>(Path(teacher, $"/report?from=2026-11-01&to={today.AddDays(1):yyyy-MM-dd}"));
        var mkd = report.Currencies.Single(c => c.Currency == "MKD");
        mkd.ReceivedMinor.ShouldBe(200_000);
        mkd.PackagesSold.ShouldBe(1);
        mkd.ChargedMinor.ShouldBe(480_000);
        report.Currencies.Single(c => c.Currency == "EUR").ReceivedMinor.ShouldBe(0); // paid in October, outside the period
        report.LessonsConsumed.ShouldBe(1);
        (await teacher.Client.GetAsync(Path(teacher, "/report?from=2026-11-10&to=2026-11-01"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await teacher.Client.GetAsync(Path(teacher, "/report?from=2024-01-01&to=2026-01-01"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Finance_is_isolated_per_workspace()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var ana = await StudentAsync(teacher, "Ана");
        var finance = await AssignAsync(teacher, ana.Id, new { title = "Пакет", lessonCount = 4, priceMinor = 480_000, currency = "MKD" });
        var paid = await PayAsync(teacher, ana.Id, new { amountMinor = 1_000, currency = "MKD", paidOn = "2026-11-01", method = "Cash" });
        var package = finance.Packages.Single();
        var payment = paid.Entries.Single(e => e.Kind == "Payment");

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher, $"/students/{ana.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, Path(teacher, "/overview"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, Path(teacher, $"/payments/{payment.Id}/reverse"), new { reason = "x" });
        // Under one's own route, another workspace's records do not exist.
        (await intruder.Client.GetAsync(Path(intruder, $"/students/{ana.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PostAsync(Path(intruder, $"/students/{ana.Id}/payments"), new { amountMinor = 1, currency = "MKD", paidOn = "2026-11-01", method = "Cash" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PostAsync(Path(intruder, $"/packages/{package.Id}/close"), new { version = package.Version })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.PostAsync(Path(intruder, $"/payments/{payment.Id}/reverse"), new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.Client.GetJsonAsync<FinanceOverviewResponse>(Path(intruder, "/overview"))).Students.ShouldBeEmpty();
    }

    /// <summary>Runs the consumption job once (the hosted job may also have run meanwhile: it is idempotent).</summary>
    private async Task ProcessAsync() => await fixture.Factory.Services.GetRequiredService<LessonConsumptionJob>().RunOnceAsync(Ct);

    private async Task<int> LessonEventsPendingAsync(Guid workspaceId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM LessonEvents WHERE WorkspaceId = {workspaceId} AND ProcessedAtUtc IS NULL").SingleAsync(Ct);
    }

    private static async Task<StudentFinanceResponse> GetAsync(TeacherSession teacher, Guid studentId) =>
        await teacher.Client.GetJsonAsync<StudentFinanceResponse>(Path(teacher, $"/students/{studentId}"));

    private static async Task<StudentFinanceResponse> AssignAsync(TeacherSession teacher, Guid studentId, object body) =>
        await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/students/{studentId}/packages"), body));

    private static async Task<StudentFinanceResponse> PayAsync(TeacherSession teacher, Guid studentId, object body) =>
        await ReadAsync<StudentFinanceResponse>(await teacher.Client.PostAsync(Path(teacher, $"/students/{studentId}/payments"), body));

    private static async Task<StudentResponse> StudentAsync(TeacherSession teacher, string firstName)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/students", new { firstName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<StudentResponse>(Ct))!;
    }

    private static async Task<Guid> MathAsync(TeacherSession teacher) =>
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;

    private static object OfferBody(Guid subject, int lessons, long price, int? version = null) => new
    {
        kind = "Package",
        title = "Пакет математика",
        description = "Четири часа",
        subjectId = subject,
        durationMinutes = 60,
        priceMinor = price,
        currency = "MKD",
        mode = "Online",
        lessonCount = lessons,
        seats = (int?)null,
        isActive = true,
        showOnPublicSite = false,
        version,
    };

    private static async Task<OfferResponse> OfferAsync(TeacherSession teacher, int lessons, long price)
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/offers", OfferBody(await MathAsync(teacher), lessons, price));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(Ct))!;
    }

    private static async Task<LessonResponse> IndividualLessonAsync(TeacherSession teacher, Guid studentId, string start = "2024-03-12T10:00:00")
    {
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons",
            new { kind = "Individual", studentId, title = "Час", startLocal = start, durationMinutes = 60, mode = "Online" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private static async Task<LessonResponse> GroupLessonAsync(TeacherSession teacher, params Guid[] students)
    {
        var group = await ReadAsync<GroupResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups", new { name = $"Група {Guid.NewGuid():N}"[..12] }));
        foreach (var student in students)
        {
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/groups/{group.Id}/members", new { studentId = student })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons",
            new { kind = "Group", groupId = group.Id, title = "Групен час", startLocal = "2024-03-05T17:00:00", durationMinutes = 90, mode = "Online" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LessonResponse>(Ct))!;
    }

    private static async Task<LessonResponse> CompleteAsync(TeacherSession teacher, LessonResponse lesson, object attendance) =>
        await ReadAsync<LessonResponse>(await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/lessons/{lesson.Id}/complete", new { version = lesson.Version, attendance }));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
        }
    }
}
