using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Offers.Domain;

public enum OfferKind
{
    SingleLesson = 1,
    Package = 2,
    GroupProgram = 3,
}

public enum OfferMode
{
    Online = 1,
    InPerson = 2,
    Hybrid = 3,
}

/// <summary>
/// A service the teacher offers (M05): a single lesson, a package of lessons or a group program. Archived offers
/// are hidden (and keep their history for later phases: requests, packages, enrollments).
/// </summary>
public sealed class Offer : ITenantOwned, IArchivable
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 600;
    public const int MaxLessonCount = 200;
    public const int MaxSeats = 500;
    /// <summary>10 000 000.00 in a currency with two decimals: far above any lesson price, below overflow.</summary>
    public const long MaxPriceMinor = 1_000_000_000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public OfferKind Kind { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public Guid SubjectId { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Price in minor units (D-21: integer amounts with an ISO 4217 currency, never floating point).</summary>
    public long PriceMinor { get; set; }

    public required string Currency { get; set; }

    public OfferMode Mode { get; set; }

    /// <summary>Packages only: lessons included (≥ 1).</summary>
    public int? LessonCount { get; set; }

    /// <summary>Group programs only: capacity (≥ 1).</summary>
    public int? Seats { get; set; }

    /// <summary>Inactive offers stay visible to the teacher but cannot receive new requests.</summary>
    public bool IsActive { get; set; }

    public bool ShowOnPublicSite { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    /// <summary>Optimistic concurrency: clients send the version they loaded.</summary>
    public int Version { get; set; }
}
