using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Modules.Leads.Application;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Lessons;

/// <summary>R2-P6: working hours, free slots, public booking requests (D-23), confirm/decline/reschedule/cancel.</summary>
[Collection(ApiCollection.Name)]
public sealed class BookingTests(ClassHubApiFixture fixture)
{
    private static readonly TimeZoneInfo Skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Tomorrow => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Skopje)).AddDays(1);

    private sealed record Bookable(TeacherSession Teacher, string Slug, OfferResponse Offer) : IDisposable
    {
        public string Path(string rest = "") => $"/api/w/{Teacher.WorkspaceId}/booking{rest}";

        public void Dispose() => Teacher.Dispose();
    }

    [Fact]
    public async Task A_visitor_requests_a_free_slot_and_the_teacher_confirms_it_into_a_lesson()
    {
        using var site = await BookableAsync();
        using var visitor = fixture.Factory.CreateClient();

        var page = (await visitor.GetFromJsonAsync<PublicBookingResponse>($"/api/public/sites/{site.Slug}/booking", Ct))!;
        page.Available.ShouldBeTrue();
        page.TimeZone.ShouldBe("Europe/Skopje");
        page.Offers.ShouldHaveSingleItem().DurationMinutes.ShouldBe(60);
        var slot = await FirstSlotAsync(visitor, site);
        slot.Time.ShouldBe("09:00");
        (await visitor.GetStringAsync($"/{site.Slug}", Ct)).ShouldContain($"/{site.Slug}/book");
        (await visitor.GetStringAsync($"/{site.Slug}/book", Ct)).ShouldContain("booking-widget");

        // D-23: two visitors may request the same time; neither blocks it.
        await RequestAsync(visitor, site, slot.StartUtc, "ana@example.test");
        await RequestAsync(visitor, site, slot.StartUtc, "boris@example.test");
        (await FirstSlotAsync(visitor, site)).StartUtc.ShouldBe(slot.StartUtc);
        var pending = await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests?status=Pending"));
        pending.Count.ShouldBe(2);
        var ana = pending.Single(r => r.Email == "ana@example.test");
        ana.StartLocal.ShouldBe(Tomorrow.ToDateTime(new TimeOnly(9, 0)));

        var since = DateTime.UtcNow.AddSeconds(-1);
        var confirmed = await ReadAsync<BookingRequestResponse>(await site.Teacher.Client.PostAsync(site.Path($"/requests/{ana.Id}/confirm"), new { version = ana.Version }));
        confirmed.Status.ShouldBe("Confirmed");
        var lesson = await site.Teacher.Client.GetJsonAsync<LessonResponse>($"/api/w/{site.Teacher.WorkspaceId}/lessons/{confirmed.LessonId}");
        lesson.StartUtc.ShouldBe(slot.StartUtc);
        lesson.Title.ShouldBe(site.Offer.Title);
        lesson.Participants.ShouldHaveSingleItem().FirstName.ShouldBe("Марија");
        (await fixture.WaitForEmailAsync("ana@example.test", "booking.confirmed", since)).TextBody.ShouldContain("Europe/Skopje");

        // The slot is taken now; the other request for it cannot be confirmed and is declined instead.
        (await FirstSlotAsync(visitor, site)).StartUtc.ShouldNotBe(slot.StartUtc);
        var boris = pending.Single(r => r.Email == "boris@example.test");
        using (var clash = await site.Teacher.Client.PostAsync(site.Path($"/requests/{boris.Id}/confirm"), new { version = boris.Version }))
        {
            (await clash.ErrorCodeAsync()).ShouldBe(ErrorCodes.LessonOverlap);
        }

        var declined = await ReadAsync<BookingRequestResponse>(await site.Teacher.Client.PostAsync(site.Path($"/requests/{boris.Id}/decline"), new { version = boris.Version, reason = "Зафатено" }));
        declined.Status.ShouldBe("Declined");
        await fixture.WaitForEmailAsync("boris@example.test", "booking.declined", since);
    }

    [Fact]
    public async Task Parallel_confirmations_of_overlapping_requests_create_exactly_one_lesson()
    {
        using var site = await BookableAsync();
        using var visitor = fixture.Factory.CreateClient();
        var slot = await FirstSlotAsync(visitor, site);
        foreach (var email in new[] { "a@example.test", "b@example.test", "c@example.test" })
        {
            await RequestAsync(visitor, site, slot.StartUtc, email);
        }

        var pending = await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests?status=Pending"));
        // Each of the three twice: the same request and different requests for the same time race each other.
        var responses = await Task.WhenAll(pending.Concat(pending).Select(r => site.Teacher.Client.PostAsync(site.Path($"/requests/{r.Id}/confirm"), new { version = r.Version })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Lessons WHERE WorkspaceId = {site.Teacher.WorkspaceId}").SingleAsync(Ct)).ShouldBe(1);
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM BookingRequests WHERE WorkspaceId = {site.Teacher.WorkspaceId} AND Status = 2").SingleAsync(Ct)).ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_confirmed_booking_can_be_rescheduled_or_cancelled_and_the_visitor_is_told()
    {
        using var site = await BookableAsync();
        using var visitor = fixture.Factory.CreateClient();
        var slot = await FirstSlotAsync(visitor, site);
        await RequestAsync(visitor, site, slot.StartUtc, "mila@example.test");
        var request = (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldHaveSingleItem();
        var confirmed = await ReadAsync<BookingRequestResponse>(await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/confirm"), new { version = request.Version }));
        var since = DateTime.UtcNow.AddSeconds(-1);

        var moved = await ReadAsync<BookingRequestResponse>(await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/reschedule"),
            new { version = confirmed.Version, startLocal = $"{Tomorrow:yyyy-MM-dd}T15:00:00" }));
        moved.StartLocal.ShouldBe(Tomorrow.ToDateTime(new TimeOnly(15, 0)));
        (await site.Teacher.Client.GetJsonAsync<LessonResponse>($"/api/w/{site.Teacher.WorkspaceId}/lessons/{moved.LessonId}")).StartLocal.Hour.ShouldBe(15);
        await fixture.WaitForEmailAsync("mila@example.test", "booking.rescheduled", since);

        (await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/cancel"), new { version = moved.Version })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // reason
        var cancelled = await ReadAsync<BookingRequestResponse>(await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/cancel"), new { version = moved.Version, reason = "Болест" }));
        cancelled.Status.ShouldBe("Cancelled");
        (await site.Teacher.Client.GetJsonAsync<LessonResponse>($"/api/w/{site.Teacher.WorkspaceId}/lessons/{moved.LessonId}")).Status.ShouldBe("Cancelled");
        await fixture.WaitForEmailAsync("mila@example.test", "booking.cancelled", since);
        (await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/decline"), new { version = cancelled.Version })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Changing_a_booked_lesson_in_the_lesson_editor_keeps_the_booking_in_step()
    {
        using var site = await BookableAsync();
        using var visitor = fixture.Factory.CreateClient();
        await RequestAsync(visitor, site, (await FirstSlotAsync(visitor, site)).StartUtc, "nina@example.test");
        var request = (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldHaveSingleItem();
        var confirmed = await ReadAsync<BookingRequestResponse>(await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/confirm"), new { version = request.Version }));
        var lesson = await site.Teacher.Client.GetJsonAsync<LessonResponse>($"/api/w/{site.Teacher.WorkspaceId}/lessons/{confirmed.LessonId}");
        var since = DateTime.UtcNow.AddSeconds(-1);

        var moved = await ReadAsync<LessonResponse>(await site.Teacher.Client.PutAsync($"/api/w/{site.Teacher.WorkspaceId}/lessons/{lesson.Id}", new
        {
            kind = "Individual", studentId = lesson.Participants[0].StudentId, title = lesson.Title, startLocal = $"{Tomorrow:yyyy-MM-dd}T14:00:00", durationMinutes = 60, mode = "Online", version = lesson.Version,
        }));
        var booking = await site.Teacher.Client.GetJsonAsync<BookingRequestResponse>(site.Path($"/requests/{request.Id}"));
        booking.StartLocal.ShouldBe(Tomorrow.ToDateTime(new TimeOnly(14, 0)));
        await fixture.WaitForEmailAsync("nina@example.test", "booking.rescheduled", since);

        await ReadAsync<LessonResponse>(await site.Teacher.Client.PostAsync($"/api/w/{site.Teacher.WorkspaceId}/lessons/{lesson.Id}/cancel", new { version = moved.Version, reason = "Патување" }));
        booking = await site.Teacher.Client.GetJsonAsync<BookingRequestResponse>(site.Path($"/requests/{request.Id}"));
        booking.Status.ShouldBe("Cancelled");
        booking.Reason.ShouldBe("Патување");
        await fixture.WaitForEmailAsync("nina@example.test", "booking.cancelled", since);
    }

    [Fact]
    public async Task A_confirmation_that_fails_converts_nothing()
    {
        using var site = await BookableAsync();
        using var visitor = fixture.Factory.CreateClient();
        var slot = await FirstSlotAsync(visitor, site);
        await RequestAsync(visitor, site, slot.StartUtc, "late@example.test");
        // The teacher takes that time with another lesson before confirming.
        Guid otherId;
        using (var other = await site.Teacher.Client.PostAsync($"/api/w/{site.Teacher.WorkspaceId}/students", new { firstName = "Друг" }))
        {
            otherId = (await other.Content.ReadFromJsonAsync<ClassHub.Core.Modules.Students.Contracts.StudentResponse>(Ct))!.Id;
        }

        (await site.Teacher.Client.PostAsync($"/api/w/{site.Teacher.WorkspaceId}/lessons", new
        {
            kind = "Individual", studentId = otherId, title = "Друг час", startLocal = TimeZoneInfo.ConvertTimeFromUtc(slot.StartUtc, Skopje), durationMinutes = 60, mode = "Online",
        })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var request = (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldHaveSingleItem();

        using (var clash = await site.Teacher.Client.PostAsync(site.Path($"/requests/{request.Id}/confirm"), new { version = request.Version }))
        {
            (await clash.ErrorCodeAsync()).ShouldBe(ErrorCodes.LessonOverlap);
        }

        const string email = "late@example.test";
        await using var db = fixture.CreateDbContext();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Students WHERE WorkspaceId = {site.Teacher.WorkspaceId} AND Email = {email}").SingleAsync(Ct)).ShouldBe(0);
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Leads WHERE Id = {request.LeadId} AND StudentId IS NULL").SingleAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Only_free_slots_of_bookable_offers_can_be_requested()
    {
        using var site = await BookableAsync();
        using var visitor = fixture.Factory.CreateClient();
        var slot = await FirstSlotAsync(visitor, site);

        // Outside working hours, not on the step, and an offer that is not public.
        foreach (var start in new[] { Tomorrow.ToDateTime(new TimeOnly(22, 0)), Tomorrow.ToDateTime(new TimeOnly(9, 10)) })
        {
            using var wrong = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Booking(await TokenAsync(site.Slug), site.Offer.Id, TimeZoneInfo.ConvertTimeToUtc(start, Skopje), "x@example.test"), Ct);
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrong.ReadJsonAsync()).GetProperty("errors").TryGetProperty("slotStartUtc", out _).ShouldBeTrue();
        }

        var hidden = await CreateOfferAsync(site.Teacher, "Приватен час", showOnPublicSite: false);
        (await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Booking(await TokenAsync(site.Slug), hidden.Id, slot.StartUtc, "x@example.test"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Booking off: the page says so and requested times are refused.
        var settings = await site.Teacher.Client.GetJsonAsync<BookingSettingsResponse>(site.Path("/settings"));
        await SaveSettingsAsync(site.Teacher, settings.Version, enabled: false);
        (await visitor.GetFromJsonAsync<PublicBookingResponse>($"/api/public/sites/{site.Slug}/booking", Ct))!.Available.ShouldBeFalse();
        (await visitor.GetStringAsync($"/{site.Slug}", Ct)).ShouldNotContain($"/{site.Slug}/book");
        (await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Booking(await TokenAsync(site.Slug), site.Offer.Id, slot.StartUtc, "x@example.test"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Spam_rules_apply_to_booking_requests()
    {
        using var site = await BookableAsync();
        await using var limited = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Leads:MaxPerSitePerHour", "1"));
        using var visitor = limited.CreateClient();
        var slot = await FirstSlotAsync(visitor, site);

        // A filled honeypot is accepted on the surface and records nothing.
        using (var bot = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Booking(await TokenAsync(site.Slug), site.Offer.Id, slot.StartUtc, "bot@example.test", website: "http://spam.test"), Ct))
        {
            bot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldBeEmpty();
        await RequestAsync(visitor, site, slot.StartUtc, "one@example.test");
        using var second = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Booking(await TokenAsync(site.Slug), site.Offer.Id, slot.StartUtc, "two@example.test"), Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Settings_are_validated_and_bookings_are_isolated()
    {
        using var site = await BookableAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var settings = await site.Teacher.Client.GetJsonAsync<BookingSettingsResponse>(site.Path("/settings"));
        settings.WorkingHours.Count.ShouldBe(7);

        using (var overlap = await site.Teacher.Client.PutAsync(site.Path("/settings"), Settings(settings.Version, hours: [new(DayOfWeek.Monday, new(9, 0), new(12, 0)), new(DayOfWeek.Monday, new(11, 0), new(13, 0))])))
        {
            overlap.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await site.Teacher.Client.PutAsync(site.Path("/settings"), Settings(settings.Version, step: 20))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Missing or null fields are validation errors, not server errors.
        (await site.Teacher.Client.PutAsync(site.Path("/settings"), new { enabled = true, version = settings.Version })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await site.Teacher.Client.PutAsync(site.Path("/settings"), new
        {
            enabled = true, slotStepMinutes = (int?)null, minNoticeHours = 0, horizonDays = 30, workingHours = new object?[] { null }, timeOff = new object?[] { null }, version = settings.Version,
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await site.Teacher.Client.PutAsync(site.Path("/settings"), Settings(settings.Version - 1))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var visitor = fixture.Factory.CreateClient();
        await RequestAsync(visitor, site, (await FirstSlotAsync(visitor, site)).StartUtc, "iso@example.test");
        var request = (await site.Teacher.Client.GetJsonAsync<List<BookingRequestResponse>>(site.Path("/requests"))).ShouldHaveSingleItem();
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, site.Path($"/requests/{request.Id}"));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, site.Path($"/requests/{request.Id}/confirm"), new { version = request.Version });
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, site.Path("/settings"));
        (await intruder.Client.GetAsync($"/api/w/{intruder.WorkspaceId}/booking/requests/{request.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Bookable> BookableAsync()
    {
        var teacher = await fixture.CreateTeacherAsync(confirmEmail: true);
        var offer = await CreateOfferAsync(teacher, "Час по математика");
        var slug = $"b-{Guid.NewGuid().ToString("N")[..12]}";
        using (var save = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/site",
            new { slug, template = "classic", palette = "ocean", language = "mk", draft = new { sections = SiteService.DefaultContent("mk").Sections }, version = (int?)null }))
        {
            save.StatusCode.ShouldBe(HttpStatusCode.OK, await save.Content.ReadAsStringAsync(Ct));
            var site = (await save.Content.ReadFromJsonAsync<SiteResponse>(Ct))!;
            (await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/site/publish", new { version = site.Version })).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await SaveSettingsAsync(teacher, 0, enabled: true);
        return new Bookable(teacher, slug, offer);
    }

    /// <summary>Every day 09:00–17:00, 30-minute steps, no notice, 30 days ahead.</summary>
    private static object Settings(int version, bool enabled = true, int step = 30, IReadOnlyList<WorkingHoursItem>? hours = null) => new
    {
        enabled,
        slotStepMinutes = step,
        minNoticeHours = 0,
        horizonDays = 30,
        workingHours = hours ?? [.. Enum.GetValues<DayOfWeek>().Select(d => new WorkingHoursItem(d, new TimeOnly(9, 0), new TimeOnly(17, 0)))],
        timeOff = Array.Empty<TimeOffItem>(),
        version,
    };

    private static async Task SaveSettingsAsync(TeacherSession teacher, int version, bool enabled)
    {
        using var response = await teacher.Client.PutAsync($"/api/w/{teacher.WorkspaceId}/booking/settings", Settings(version, enabled));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<PublicSlot> FirstSlotAsync(HttpClient visitor, Bookable site)
    {
        var slots = (await visitor.GetFromJsonAsync<PublicSlotsResponse>($"/api/public/sites/{site.Slug}/booking/slots?offerId={site.Offer.Id}&from={Tomorrow:yyyy-MM-dd}", Ct))!;
        return slots.Days.First().Slots.First();
    }

    private async Task RequestAsync(HttpClient visitor, Bookable site, DateTime startUtc, string email)
    {
        using var response = await visitor.PostAsJsonAsync($"/api/public/sites/{site.Slug}/leads", Booking(await TokenAsync(site.Slug), site.Offer.Id, startUtc, email), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
    }

    private Task<string> TokenAsync(string slug) =>
        fixture.WithServiceAsync<LeadFormTokens, string>(tokens => Task.FromResult(tokens.Issue(slug, DateTimeOffset.UtcNow.AddSeconds(-10))));

    private static object Booking(string token, Guid offerId, DateTime slotStartUtc, string email, string website = "") => new
    {
        type = "LessonRequest",
        name = "Марија Илиевска",
        email,
        phone = "+389 70 123 456",
        message = (string?)null,
        offerId,
        subjectId = (Guid?)null,
        consent = true,
        ageConfirmed = true,
        consentVersion = LeadConsent.Version,
        formToken = token,
        website,
        slotStartUtc,
    };

    private static async Task<OfferResponse> CreateOfferAsync(TeacherSession teacher, string title, bool showOnPublicSite = true)
    {
        var math = (await teacher.Client.GetJsonAsync<ClassHub.Core.Modules.Catalog.Contracts.CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == "math").Id;
        using var response = await teacher.Client.PostAsync($"/api/w/{teacher.WorkspaceId}/offers", new
        {
            kind = "SingleLesson", title, subjectId = math, durationMinutes = 60, priceMinor = 120_000L, mode = "Online", isActive = true, showOnPublicSite,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(Ct))!;
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
