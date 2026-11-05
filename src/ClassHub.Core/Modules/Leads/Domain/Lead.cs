using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Leads.Domain;

/// <summary>What the visitor asked for. A booking slot comes with R2-P6 (D-13): a lesson request has none.</summary>
public enum LeadType
{
    Message = 1,
    LessonRequest = 2,
}

public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Converted = 3,
    Closed = 4,
}

/// <summary>
/// An interested visitor's request from the public site (M03). Belongs to exactly one workspace; archived leads are
/// hidden from the inbox but kept. Converting a lead into a student comes with R2-P1 (D-12).
/// </summary>
public sealed class Lead : ITenantOwned, IArchivable
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 40;
    public const int MessageMaxLength = 2000;
    public const int ConsentVersionMaxLength = 40;
    public const int OfferTitleMaxLength = 120;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public LeadType Type { get; set; }

    public LeadStatus Status { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    public string? Phone { get; set; }

    public string? Message { get; set; }

    /// <summary>The public offer the request is about (same workspace, composite FK).</summary>
    public Guid? OfferId { get; set; }

    /// <summary>The offer's title when the request was sent (the offer may change or be archived later).</summary>
    public string? OfferTitle { get; set; }

    /// <summary>The subject the visitor is interested in (catalog).</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Version of the consent and privacy notice text the visitor accepted (D-18).</summary>
    public required string ConsentVersion { get; set; }

    public DateTime ConsentedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    /// <summary>Optimistic concurrency for status changes and archiving.</summary>
    public int Version { get; set; }
}

/// <summary>A private note of the teacher on a lead.</summary>
public sealed class LeadNote : ITenantOwned
{
    public const int TextMaxLength = 2000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid LeadId { get; set; }

    public required string Text { get; set; }

    public Guid? AuthorUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Allowed status changes: forward through the pipeline, closing at any point, reopening a closed lead, and stepping
/// back one stage. "Converted" is not reachable from "Closed" without reopening first.
/// </summary>
public static class LeadStatusRules
{
    private static readonly Dictionary<LeadStatus, LeadStatus[]> Allowed = new()
    {
        [LeadStatus.New] = [LeadStatus.Contacted, LeadStatus.Converted, LeadStatus.Closed],
        [LeadStatus.Contacted] = [LeadStatus.New, LeadStatus.Converted, LeadStatus.Closed],
        [LeadStatus.Converted] = [LeadStatus.Contacted, LeadStatus.Closed],
        [LeadStatus.Closed] = [LeadStatus.New, LeadStatus.Contacted],
    };

    public static bool CanChange(LeadStatus from, LeadStatus to) => Allowed[from].Contains(to);
}
