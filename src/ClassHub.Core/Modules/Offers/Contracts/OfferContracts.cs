namespace ClassHub.Core.Modules.Offers.Contracts;

public sealed record OfferResponse(
    Guid Id,
    string Kind,
    string Title,
    string? Description,
    Guid SubjectId,
    int DurationMinutes,
    long PriceMinor,
    string Currency,
    string Mode,
    int? LessonCount,
    int? Seats,
    bool IsActive,
    bool ShowOnPublicSite,
    int DisplayOrder,
    DateTime UpdatedAtUtc,
    int Version);

/// <summary>
/// Create or replace an offer. <c>LessonCount</c> is required for packages only, <c>Seats</c> for group programs
/// only. <c>Currency</c> defaults to the workspace currency. <c>Version</c> is required on update (409 if stale).
/// </summary>
public sealed record SaveOfferRequest(
    string? Kind,
    string? Title,
    string? Description,
    Guid? SubjectId,
    int? DurationMinutes,
    long? PriceMinor,
    string? Currency,
    string? Mode,
    int? LessonCount,
    int? Seats,
    bool IsActive,
    bool ShowOnPublicSite,
    int? Version = null);

/// <summary>The full new order of the workspace's (non-archived) offers.</summary>
public sealed record ReorderOffersRequest(IReadOnlyList<Guid>? OfferIds);

/// <summary>For other modules (leads, public site, onboarding) through Contracts.</summary>
public interface IOfferLookup
{
    /// <summary>
    /// True when the current workspace's offer exists, is not archived and is active: only such offers may receive
    /// new requests (M05 rule "Неактивна понуда не може да прима нови барања").
    /// </summary>
    Task<bool> CanReceiveRequestsAsync(Guid offerId, CancellationToken cancellationToken);

    /// <summary>Number of the current workspace's offers that are not archived.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}
