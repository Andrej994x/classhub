using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Sites.Domain;

/// <summary>
/// The teacher's public website (M02): one per workspace. The draft is edited in the SPA; publishing (R1-P4) copies
/// it to the published content, so editing never changes what visitors see until the teacher publishes.
/// </summary>
public sealed class Site : ITenantOwned
{
    public const int LanguageMaxLength = 10;
    public const int HiddenReasonMaxLength = 500;

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

    /// <summary>
    /// Set by platform moderation (R1-P11): the whole site is hidden from the public (404, not in the sitemap, no
    /// requests or tests) until moderation shows it again. Independent of publishing; the teacher cannot clear it.
    /// </summary>
    public DateTime? HiddenAtUtc { get; set; }

    public string? HiddenReason { get; set; }

    /// <summary>JSON array of sections hidden by moderation (<see cref="HiddenSection"/>); null when none.</summary>
    public string? HiddenSectionsJson { get; set; }
}

/// <summary>One section type of a site hidden by moderation, with the reason shown to the teacher.</summary>
public sealed record HiddenSection(string Type, string Reason, DateTime HiddenAtUtc)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static IReadOnlyList<HiddenSection> Read(string? json) =>
        string.IsNullOrEmpty(json) ? [] : System.Text.Json.JsonSerializer.Deserialize<List<HiddenSection>>(json, Json) ?? [];

    public static string? Write(IReadOnlyCollection<HiddenSection> sections) =>
        sections.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(sections, Json);
}
