using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Sites.Domain;

/// <summary>
/// The teacher's public website (M02): one per workspace. The draft is edited in the SPA; publishing (R1-P4) copies
/// it to the published content, so editing never changes what visitors see until the teacher publishes.
/// </summary>
public sealed class Site : ITenantOwned
{
    public const int LanguageMaxLength = 10;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>Unique across the platform (unique index), see <c>SiteSlugs</c>.</summary>
    public required string Slug { get; set; }

    public required string Template { get; set; }

    public required string Palette { get; set; }

    /// <summary>Language of the page content (D-15), independent of the teacher's UI language.</summary>
    public required string Language { get; set; }

    /// <summary>JSON of <c>SiteContent</c> (validated structured text; never HTML).</summary>
    public required string DraftJson { get; set; }

    /// <summary>JSON of the published content (R1-P4); null while never published.</summary>
    public string? PublishedJson { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
