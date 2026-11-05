using System.Net;
using System.Net.Http.Json;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.IntegrationTests.Offers;

/// <summary>R1-P2: offers & programs (M05).</summary>
[Collection(ApiCollection.Name)]
public sealed class OfferTests(ClassHubApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_teacher_creates_all_three_kinds_and_lists_them_in_order()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");

        var single = await CreateAsync(teacher, Offer("SingleLesson", math));
        var package = await CreateAsync(teacher, Offer("Package", math, lessons: 10, price: 1_200_000));
        var group = await CreateAsync(teacher, Offer("GroupProgram", math, seats: 8, price: 0));

        single.Currency.ShouldBe("MKD"); // defaults to the workspace currency
        (package.LessonCount, package.Seats).ShouldBe((10, (int?)null));
        (group.Seats, group.LessonCount, group.PriceMinor).ShouldBe((8, (int?)null, 0L));
        var list = await teacher.Client.GetJsonAsync<List<OfferResponse>>(OffersPath(teacher));
        list.Select(o => o.Id).ShouldBe([single.Id, package.Id, group.Id]);
        list.Select(o => o.DisplayOrder).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Invalid_offers_are_rejected_with_field_errors()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");

        foreach (var (body, field) in new (object Body, string Field)[]
                 {
                     (Offer("SingleLesson", math, price: -100), "priceMinor"),
                     (Offer("Package", math), "lessonCount"),
                     (Offer("GroupProgram", math, seats: 0), "seats"),
                     (Offer("SingleLesson", math, seats: 5), "seats"),
                     (Offer("SingleLesson", Guid.NewGuid()), "subjectId"),
                     (Offer("SingleLesson", math, currency: "ABC"), "currency"),
                 })
        {
            using var response = await teacher.Client.PostAsync(OffersPath(teacher), body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, field);
            (await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        (await teacher.Client.GetJsonAsync<List<OfferResponse>>(OffersPath(teacher))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[PriceMinor]", "-1")]                 // price >= 0
    [InlineData("[LessonCount]", "0")]                 // package lessons >= 1
    [InlineData("[Seats]", "3")]                       // only group programs have seats
    public async Task The_database_enforces_the_rules_even_without_the_api(string column, string value)
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var package = await CreateAsync(teacher, Offer("Package", await SubjectAsync(teacher, "math"), lessons: 5));

        await using var db = fixture.CreateDbContext();
        // Column and value are fixed test constants (InlineData); the id is a parameter.
        var sql = "UPDATE Offers SET " + column + " = " + value + " WHERE Id = {0}";
        var error = await Should.ThrowAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql, [package.Id], Ct));
        error.Number.ShouldBe(547); // CHECK constraint violation
    }

    [Fact]
    public async Task Updates_need_the_current_version_and_stale_saves_are_refused()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var offer = await CreateAsync(teacher, Offer("SingleLesson", math));
        var path = $"{OffersPath(teacher)}/{offer.Id}";

        using (var updated = await teacher.Client.PutAsync(path, Offer("SingleLesson", math, title: "Нов наслов", price: 200_000, version: offer.Version)))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await updated.Content.ReadFromJsonAsync<OfferResponse>(Ct);
            (body!.Title, body.PriceMinor, body.Version).ShouldBe(("Нов наслов", 200_000L, offer.Version + 1));
        }

        using (var stale = await teacher.Client.PutAsync(path, Offer("SingleLesson", math, title: "Стар", version: offer.Version)))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await stale.ErrorCodeAsync()).ShouldBe(ErrorCodes.ConcurrencyConflict);
        }

        (await teacher.Client.GetJsonAsync<OfferResponse>(path)).Title.ShouldBe("Нов наслов");
    }

    [Fact]
    public async Task Inactive_and_archived_offers_cannot_receive_requests()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var active = await CreateAsync(teacher, Offer("SingleLesson", math));
        var inactive = await CreateAsync(teacher, Offer("SingleLesson", math, isActive: false));
        var archived = await CreateAsync(teacher, Offer("SingleLesson", math));

        using (var archive = await teacher.Client.DeleteAsync($"{OffersPath(teacher)}/{archived.Id}"))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await CanReceiveAsync(teacher, active.Id)).ShouldBeTrue();
        (await CanReceiveAsync(teacher, inactive.Id)).ShouldBeFalse();
        (await CanReceiveAsync(teacher, archived.Id)).ShouldBeFalse();

        // Archived offers disappear for the teacher too.
        (await teacher.Client.GetJsonAsync<List<OfferResponse>>(OffersPath(teacher))).Select(o => o.Id).ShouldBe([active.Id, inactive.Id]);
        using var gone = await teacher.Client.GetAsync($"{OffersPath(teacher)}/{archived.Id}");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_workspace_cannot_use_an_offer_even_through_the_lookup()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var other = await fixture.CreateTeacherAsync();
        var offer = await CreateAsync(owner, Offer("SingleLesson", await SubjectAsync(owner, "math")));

        (await CanReceiveAsync(other, offer.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_teacher_reorders_offers_and_the_list_must_be_complete()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(teacher, "math");
        var a = await CreateAsync(teacher, Offer("SingleLesson", math, title: "A"));
        var b = await CreateAsync(teacher, Offer("SingleLesson", math, title: "B"));
        var c = await CreateAsync(teacher, Offer("SingleLesson", math, title: "C"));
        var orderPath = $"{OffersPath(teacher)}/order";

        using (var reordered = await teacher.Client.PutAsync(orderPath, new { offerIds = new[] { c.Id, a.Id, b.Id } }))
        {
            reordered.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await teacher.Client.GetJsonAsync<List<OfferResponse>>(OffersPath(teacher))).Select(o => o.Title).ShouldBe(["C", "A", "B"]);

        foreach (var ids in new[] { new[] { c.Id, a.Id }, new[] { c.Id, a.Id, a.Id }, new[] { c.Id, a.Id, Guid.NewGuid() } })
        {
            using var invalid = await teacher.Client.PutAsync(orderPath, new { offerIds = ids });
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Another_teacher_cannot_read_change_archive_or_reorder_offers()
    {
        using var owner = await fixture.CreateTeacherAsync();
        using var intruder = await fixture.CreateTeacherAsync();
        var math = await SubjectAsync(owner, "math");
        var offer = await CreateAsync(owner, Offer("SingleLesson", math, title: "Mine"));
        var path = $"{OffersPath(owner)}/{offer.Id}";

        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, OffersPath(owner));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Post, OffersPath(owner), Offer("SingleLesson", math));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Get, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, path, Offer("SingleLesson", math, title: "Hijacked", version: offer.Version));
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Delete, path);
        await TenantIsolationKit.AssertCrossTenantDeniedAsync(intruder.Client, HttpMethod.Put, $"{OffersPath(owner)}/order", new { offerIds = new[] { offer.Id } });

        // The intruder's own workspace does not see the offer either (even with its id in their own URL).
        using (var ownPath = await intruder.Client.GetAsync($"{OffersPath(intruder)}/{offer.Id}"))
        {
            ownPath.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await owner.Client.GetJsonAsync<OfferResponse>(path)).Title.ShouldBe("Mine");
    }

    [Fact]
    public async Task The_first_offer_completes_the_onboarding_step()
    {
        using var teacher = await fixture.CreateTeacherAsync();
        var step = async () => (await teacher.Client.GetJsonAsync<OnboardingResponse>($"/api/w/{teacher.WorkspaceId}/onboarding"))
            .Steps.Single(s => s.Key == OnboardingSteps.FirstOffer);

        (await step()).ShouldBe(new OnboardingStep(OnboardingSteps.FirstOffer, false, true));
        await CreateAsync(teacher, Offer("SingleLesson", await SubjectAsync(teacher, "math")));
        (await step()).Completed.ShouldBeTrue();
    }

    private static string OffersPath(TeacherSession teacher) => $"/api/w/{teacher.WorkspaceId}/offers";

    private static object Offer(string kind, Guid subject, int? lessons = null, int? seats = null, long price = 150_000, string title = "Час по математика",
        string? currency = null, bool isActive = true, int? version = null) => new
    {
        kind,
        title,
        description = "Индивидуален час",
        subjectId = subject,
        durationMinutes = 60,
        priceMinor = price,
        currency,
        mode = "Online",
        lessonCount = lessons,
        seats,
        isActive,
        showOnPublicSite = true,
        version,
    };

    private static async Task<OfferResponse> CreateAsync(TeacherSession teacher, object body)
    {
        using var response = await teacher.Client.PostAsync(OffersPath(teacher), body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(Ct))!;
    }

    private static async Task<Guid> SubjectAsync(TeacherSession teacher, string code) =>
        (await teacher.Client.GetJsonAsync<CatalogResponse>("/api/catalog")).Subjects.Single(s => s.Code == code).Id;

    /// <summary>The lookup other modules (leads) use, called inside the teacher's workspace.</summary>
    private async Task<bool> CanReceiveAsync(TeacherSession teacher, Guid offerId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(teacher.WorkspaceId, teacher.Me.Id, WorkspaceRole.Owner);
        return await scope.ServiceProvider.GetRequiredService<IOfferLookup>().CanReceiveRequestsAsync(offerId, Ct);
    }
}
