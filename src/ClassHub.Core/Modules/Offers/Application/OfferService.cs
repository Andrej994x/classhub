using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Offers.Domain;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Offers.Application;

/// <summary>Offers of the current (verified) workspace. Every query is tenant-filtered; archived offers are hidden.</summary>
internal sealed class OfferService(
    ClassHubDbContext db,
    ITenantContext tenant,
    ICatalogLookup catalog,
    IWorkspaceSettings workspace,
    IPublicSiteCache siteCache,
    TimeProvider clock) : IOfferLookup
{
    public async Task<IReadOnlyList<OfferResponse>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Offer>().AsNoTracking()
            .OrderBy(o => o.DisplayOrder).ThenBy(o => o.CreatedAtUtc)
            .Select(o => ToResponse(o))
            .ToListAsync(cancellationToken);

    public async Task<OfferResponse> GetAsync(Guid offerId, CancellationToken cancellationToken) =>
        ToResponse(await LoadAsync(offerId, cancellationToken, tracking: false));

    public async Task<OfferResponse> CreateAsync(SaveOfferRequest request, CancellationToken cancellationToken)
    {
        await ThrowIfUnknownSubjectAsync(request.SubjectId!.Value, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var lastOrder = await db.Set<Offer>().Select(o => (int?)o.DisplayOrder).MaxAsync(cancellationToken);
        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            WorkspaceId = tenant.RequireWorkspaceId(),
            Title = string.Empty,
            Currency = request.Currency?.ToUpperInvariant() ?? await workspace.GetCurrencyAsync(cancellationToken),
            DisplayOrder = (lastOrder ?? 0) + 1,
            CreatedAtUtc = now,
        };
        Apply(offer, request, now);
        db.Set<Offer>().Add(offer);
        await db.SaveChangesAsync(cancellationToken);
        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        return ToResponse(offer);
    }

    public async Task<OfferResponse> UpdateAsync(Guid offerId, SaveOfferRequest request, CancellationToken cancellationToken)
    {
        var offer = await LoadAsync(offerId, cancellationToken, tracking: true);
        if (request.Version != offer.Version)
        {
            throw new ConflictException("The offer was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        // A subject the ClassHub team deactivated stays valid on the offers that already use it (R1-P11).
        if (request.SubjectId!.Value != offer.SubjectId)
        {
            await ThrowIfUnknownSubjectAsync(request.SubjectId.Value, cancellationToken);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.Currency is not null)
        {
            offer.Currency = request.Currency.ToUpperInvariant();
        }

        // Price changes apply to new requests only; agreed packages keep their own price snapshot (R2-P10).
        Apply(offer, request, now);
        offer.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        return ToResponse(offer);
    }

    /// <summary>Archives the offer: hidden from the teacher and the public, never bookable again, history kept.</summary>
    public async Task ArchiveAsync(Guid offerId, CancellationToken cancellationToken)
    {
        var offer = await LoadAsync(offerId, cancellationToken, tracking: true);
        offer.ArchivedAtUtc = clock.GetUtcNow().UtcDateTime;
        offer.IsActive = false;
        offer.ShowOnPublicSite = false;
        offer.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
    }

    /// <summary>Sets the display order. The list must contain exactly the workspace's current offers.</summary>
    public async Task<IReadOnlyList<OfferResponse>> ReorderAsync(IReadOnlyList<Guid> offerIds, CancellationToken cancellationToken)
    {
        var offers = await db.Set<Offer>().ToListAsync(cancellationToken);
        if (offerIds.Count != offers.Count || offerIds.Distinct().Count() != offerIds.Count || !offers.All(o => offerIds.Contains(o.Id)))
        {
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["offerIds"] = ["The list must contain every current offer exactly once."],
            });
        }

        var byId = offers.ToDictionary(o => o.Id);
        for (var i = 0; i < offerIds.Count; i++)
        {
            var offer = byId[offerIds[i]];
            if (offer.DisplayOrder != i + 1)
            {
                offer.DisplayOrder = i + 1;
                offer.Version++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await siteCache.EvictCurrentWorkspaceAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    public Task<bool> CanReceiveRequestsAsync(Guid offerId, CancellationToken cancellationToken) =>
        db.Set<Offer>().AnyAsync(o => o.Id == offerId && o.IsActive, cancellationToken);

    public async Task<OfferTerms?> TermsAsync(Guid offerId, CancellationToken cancellationToken) =>
        await db.Set<Offer>().AsNoTracking().Where(o => o.Id == offerId)
            .Select(o => new OfferTerms(o.Id, o.Title, o.Kind == OfferKind.Package ? o.LessonCount ?? 1 : 1, o.PriceMinor, o.Currency))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken) => db.Set<Offer>().CountAsync(cancellationToken);

    public async Task<(int Active, int Public)> CountActiveAsync(CancellationToken cancellationToken)
    {
        var counts = await db.Set<Offer>().AsNoTracking().Where(o => o.IsActive)
            .GroupBy(_ => 1)
            .Select(g => new { Active = g.Count(), Public = g.Count(o => o.ShowOnPublicSite) })
            .SingleOrDefaultAsync(cancellationToken);
        return (counts?.Active ?? 0, counts?.Public ?? 0);
    }

    public async Task<IReadOnlyList<PublicOffer>> ListPublicAsync(CancellationToken cancellationToken) =>
        await db.Set<Offer>().AsNoTracking()
            .Where(o => o.ShowOnPublicSite && o.IsActive)
            .OrderBy(o => o.DisplayOrder).ThenBy(o => o.CreatedAtUtc)
            .Select(o => new PublicOffer(o.Id, o.Title, o.Description, o.Kind.ToString(), o.SubjectId, o.DurationMinutes, o.PriceMinor, o.Currency,
                o.Mode.ToString(), o.LessonCount, o.Seats))
            .ToListAsync(cancellationToken);

    private static void Apply(Offer offer, SaveOfferRequest request, DateTime now)
    {
        offer.Kind = Enum.Parse<OfferKind>(request.Kind!);
        offer.Title = request.Title!.Trim();
        offer.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        offer.SubjectId = request.SubjectId!.Value;
        offer.DurationMinutes = request.DurationMinutes!.Value;
        offer.PriceMinor = request.PriceMinor!.Value;
        offer.Mode = Enum.Parse<OfferMode>(request.Mode!);
        offer.LessonCount = offer.Kind == OfferKind.Package ? request.LessonCount : null;
        offer.Seats = offer.Kind == OfferKind.GroupProgram ? request.Seats : null;
        offer.IsActive = request.IsActive;
        offer.ShowOnPublicSite = request.ShowOnPublicSite;
        offer.UpdatedAtUtc = now;
    }

    private async Task<Offer> LoadAsync(Guid offerId, CancellationToken cancellationToken, bool tracking)
    {
        var query = tracking ? db.Set<Offer>() : db.Set<Offer>().AsNoTracking();
        return await query.SingleOrDefaultAsync(o => o.Id == offerId, cancellationToken) ?? throw new NotFoundException();
    }

    private async Task ThrowIfUnknownSubjectAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        if ((await catalog.UnknownSubjectsAsync([subjectId], cancellationToken)).Count > 0)
        {
            throw new RequestValidationException(new Dictionary<string, string[]> { ["subjectId"] = ["Unknown subject."] });
        }
    }

    private static OfferResponse ToResponse(Offer o) => new(
        o.Id, o.Kind.ToString(), o.Title, o.Description, o.SubjectId, o.DurationMinutes, o.PriceMinor, o.Currency, o.Mode.ToString(),
        o.LessonCount, o.Seats, o.IsActive, o.ShowOnPublicSite, o.DisplayOrder, o.UpdatedAtUtc, o.Version);
}

internal sealed class SaveOfferRequestValidator : AbstractValidator<SaveOfferRequest>
{
    public SaveOfferRequestValidator()
    {
        RuleFor(x => x.Kind).NotEmpty().Must(BeNamed<OfferKind>).WithMessage("Kind must be SingleLesson, Package or GroupProgram.");
        RuleFor(x => x.Title).NotEmpty().Must(t => !string.IsNullOrWhiteSpace(t)).MaximumLength(Offer.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(Offer.DescriptionMaxLength);
        RuleFor(x => x.SubjectId).NotNull().NotEqual(Guid.Empty);
        RuleFor(x => x.DurationMinutes).NotNull().InclusiveBetween(Offer.MinDurationMinutes, Offer.MaxDurationMinutes);
        RuleFor(x => x.PriceMinor).NotNull().InclusiveBetween(0, Offer.MaxPriceMinor).WithMessage("The price cannot be negative.");
        RuleFor(x => x.Currency).Must(Currencies.IsActiveIsoCode).WithMessage("Currency must be an active ISO 4217 code.").When(x => x.Currency is not null);
        RuleFor(x => x.Mode).NotEmpty().Must(BeNamed<OfferMode>).WithMessage("Mode must be Online, InPerson or Hybrid.");

        RuleFor(x => x.LessonCount).NotNull().InclusiveBetween(1, Offer.MaxLessonCount).WithMessage("A package has at least one lesson.")
            .When(x => x.Kind == nameof(OfferKind.Package));
        RuleFor(x => x.LessonCount).Null().WithMessage("Only packages have a lesson count.")
            .When(x => x.Kind is not null && x.Kind != nameof(OfferKind.Package));
        RuleFor(x => x.Seats).NotNull().InclusiveBetween(1, Offer.MaxSeats).WithMessage("A group program has at least one seat.")
            .When(x => x.Kind == nameof(OfferKind.GroupProgram));
        RuleFor(x => x.Seats).Null().WithMessage("Only group programs have seats.")
            .When(x => x.Kind is not null && x.Kind != nameof(OfferKind.GroupProgram));
    }

    /// <summary>The enum member's name exactly (numbers like "1" are refused).</summary>
    private static bool BeNamed<TEnum>(string? value)
        where TEnum : struct, Enum =>
        value is not null && Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal);
}
