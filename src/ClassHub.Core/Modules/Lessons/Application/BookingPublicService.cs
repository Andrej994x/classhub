using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Persistence;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>
/// The visitor's side of booking (R2-P6): which offers can be booked and their free slots, read in the published site's
/// workspace bound <b>read-only</b>. Pending requests do not block slots (D-23). The request itself is recorded through
/// the public request form (Leads module), which calls <see cref="IBookingIntake"/>.
/// </summary>
internal sealed class BookingPublicService(
    ClassHubDbContext db,
    LessonQueries queries,
    IPublicSites sites,
    TenantContext tenant,
    IWorkspaceOwnerContact owners,
    IWorkspaceSettings workspace,
    IOfferLookup offers,
    TimeProvider clock) : IBookingIntake
{
    /// <summary>The slot list covers at most this many days per request.</summary>
    public const int MaxDaysPerRequest = 14;

    public async Task<PublicBookingResponse> GetPageAsync(string slug, CancellationToken cancellationToken)
    {
        var workspaceId = await ResolveAsync(slug, cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var zone = await workspace.GetTimeZoneAsync(cancellationToken);
        if (!await IsBookableAsync(workspaceId, settings, cancellationToken))
        {
            return new PublicBookingResponse(false, zone, 0, []);
        }

        var bookable = await BookableOffersAsync(cancellationToken);
        return new PublicBookingResponse(true, zone, settings!.HorizonDays,
            [.. bookable.Select(o => new PublicBookingOffer(o.Id, o.Title, o.DurationMinutes, LessonModeOf(o.Mode).ToString()))]);
    }

    public async Task<PublicSlotsResponse> GetSlotsAsync(string slug, Guid offerId, DateOnly from, CancellationToken cancellationToken)
    {
        var workspaceId = await ResolveAsync(slug, cancellationToken);
        var settings = await SettingsAsync(cancellationToken);
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        if (!await IsBookableAsync(workspaceId, settings, cancellationToken))
        {
            return new PublicSlotsResponse(zoneId, []);
        }

        var offer = (await BookableOffersAsync(cancellationToken)).FirstOrDefault(o => o.Id == offerId) ?? throw new NotFoundException();
        var slots = await SlotsAsync(workspaceId, settings!, TimeZoneInfo.FindSystemTimeZoneById(zoneId), offer.DurationMinutes, from, from.AddDays(MaxDaysPerRequest - 1), cancellationToken);
        return new PublicSlotsResponse(zoneId, [.. slots.GroupBy(s => s.Date).Select(g => new PublicSlotDay(g.Key, [.. g.Select(s => new PublicSlot(s.StartUtc, s.Time.ToString("HH:mm")))]))]);
    }

    public async Task<BookableOffer> CheckAsync(Guid offerId, DateTime startUtc, CancellationToken cancellationToken)
    {
        var workspaceId = ((ClassHub.Core.SharedKernel.Tenancy.ITenantContext)tenant).RequireWorkspaceId();
        var settings = await SettingsAsync(cancellationToken);
        if (!await IsBookableAsync(workspaceId, settings, cancellationToken))
        {
            throw Unavailable();
        }

        var offer = (await BookableOffersAsync(cancellationToken)).FirstOrDefault(o => o.Id == offerId) ?? throw Unavailable();
        var zoneId = await workspace.GetTimeZoneAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var start = DateTime.SpecifyKind(startUtc.ToUniversalTime(), DateTimeKind.Utc);
        var day = DateOnly.FromDateTime(LessonTime.ToLocal(start, zone));
        var slots = await SlotsAsync(workspaceId, settings!, zone, offer.DurationMinutes, day, day, cancellationToken);
        return slots.Any(s => s.StartUtc == start)
            ? new BookableOffer(offer.Id, offer.Title, offer.SubjectId, offer.DurationMinutes, LessonModeOf(offer.Mode).ToString(), zoneId)
            : throw Unavailable();
    }

    public async Task AddAsync(Guid workspaceId, Guid leadId, BookableOffer offer, DateTime startUtc, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var start = DateTime.SpecifyKind(startUtc.ToUniversalTime(), DateTimeKind.Utc);
        db.Set<BookingRequest>().Add(new BookingRequest
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            LeadId = leadId,
            OfferId = offer.Id,
            OfferTitle = offer.Title,
            SubjectId = offer.SubjectId,
            Mode = Enum.Parse<LessonMode>(offer.Mode),
            StartUtc = start,
            EndUtc = start.AddMinutes(offer.DurationMinutes),
            TimeZone = offer.TimeZone,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BookableSlot>> SlotsAsync(
        Guid workspaceId, BookingSettings settings, TimeZoneInfo zone, int durationMinutes, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hours = await db.Set<WorkingHours>().AsNoTracking().ToListAsync(cancellationToken);
        var off = await db.Set<TimeOff>().AsNoTracking().Where(o => o.EndDate >= from && o.StartDate <= to).ToListAsync(cancellationToken);
        var owner = await owners.GetOwnerAsync(workspaceId, cancellationToken) ?? throw Unavailable();
        // The teacher's lessons in every workspace occupy their time; only start and end are read.
        var busy = await queries.BusyAsync(owner.UserId, LessonTime.StartOfDayUtc(from, zone).AddDays(-1), LessonTime.StartOfDayUtc(to.AddDays(1), zone).AddDays(1), cancellationToken);
        return BookingSlots.Compute(hours, off, busy, zone, now, durationMinutes, settings.SlotStepMinutes, settings.MinNoticeHours, settings.HorizonDays, from, to);
    }

    /// <summary>Public, active offers whose duration a lesson can have (offers may be longer than a lesson).</summary>
    private async Task<IReadOnlyList<PublicOffer>> BookableOffersAsync(CancellationToken cancellationToken) =>
        [.. (await offers.ListPublicAsync(cancellationToken)).Where(o => o.DurationMinutes is >= Lesson.MinDurationMinutes and <= Lesson.MaxDurationMinutes)];

    private Task<BookingSettings?> SettingsAsync(CancellationToken cancellationToken) =>
        db.Set<BookingSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>On, with working hours, and the teacher can receive requests (confirmed email, D-10b).</summary>
    private async Task<bool> IsBookableAsync(Guid workspaceId, BookingSettings? settings, CancellationToken cancellationToken) =>
        settings is { Enabled: true }
        && await db.Set<WorkingHours>().AnyAsync(cancellationToken)
        && await owners.GetOwnerAsync(workspaceId, cancellationToken) is { EmailConfirmed: true };

    private async Task<Guid> ResolveAsync(string slug, CancellationToken cancellationToken)
    {
        var site = await sites.FindPublishedWorkspaceAsync(slug, cancellationToken) ?? throw new NotFoundException();
        tenant.SetPublicReadOnly(site.WorkspaceId);
        return site.WorkspaceId;
    }

    /// <summary>A hybrid offer is booked as an online lesson; the teacher can change it.</summary>
    private static LessonMode LessonModeOf(string offerMode) => offerMode == nameof(LessonMode.InPerson) ? LessonMode.InPerson : LessonMode.Online;

    private static RequestValidationException Unavailable() =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["slotStartUtc"] = ["This time is no longer available. Choose another one."] });
}

/// <summary>
/// Whether the current workspace takes bookings (the public site links to <c>/{slug}/book</c>). Kept apart from the
/// public booking service: the site renderer uses it, and that service itself reads the published sites.
/// </summary>
internal sealed class BookingAvailability(ClassHubDbContext db) : IBookingAvailability
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        await db.Set<BookingSettings>().AnyAsync(s => s.Enabled, cancellationToken) && await db.Set<WorkingHours>().AnyAsync(cancellationToken);
}
