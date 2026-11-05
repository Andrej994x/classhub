using System.Text.RegularExpressions;

namespace ClassHub.Core.Modules.Sites.Contracts;

/// <summary>
/// A section of the teacher's public page. One flat shape for all types (structured plain text only, never HTML):
/// <list type="bullet">
/// <item><c>hero</c>: Title = headline, Text = short message, ShowPhoto.</item>
/// <item><c>about</c>, <c>subjects</c>, <c>offers</c>, <c>contact</c>: Title and Text (subjects and offers come from the
/// profile and the offers marked "show on public site").</item>
/// <item><c>experience</c>: Items (Title = role, Subtitle = period, Text = description).</item>
/// <item><c>testimonials</c>: Items entered by the teacher (Title = author, Text = quote) — D-17.</item>
/// <item><c>faq</c>: Items (Title = question, Text = answer).</item>
/// </list>
/// </summary>
public sealed record SiteSection(string? Type, bool Visible, string? Title, string? Text, bool ShowPhoto = false, IReadOnlyList<SiteSectionItem>? Items = null);

public sealed record SiteSectionItem(string? Title, string? Subtitle, string? Text);

/// <summary>The page content: every section type exactly once, in display order.</summary>
public sealed record SiteContent(IReadOnlyList<SiteSection>? Sections);

public sealed record SiteResponse(
    bool Exists,
    string Slug,
    string Template,
    string Palette,
    string Language,
    SiteContent Draft,
    bool IsPublished,
    DateTime? UpdatedAtUtc,
    int? Version,
    /// <summary>When the current public version was published; null while unpublished.</summary>
    DateTime? PublishedAtUtc,
    /// <summary>True when the saved draft (template, palette, language or sections) differs from the public version.</summary>
    bool HasUnpublishedChanges,
    /// <summary>Page views of the public page in the last 30 days (UTC days; D-32: no cookies, no IP addresses).</summary>
    int ViewsLast30Days);

/// <summary>Publish or unpublish the saved draft. <c>Version</c> is the version the client loaded (409 if stale).</summary>
public sealed record PublishSiteRequest(int? Version);

/// <summary>Saves the draft. <c>Version</c>: null for the first save, then the version the client loaded (409 if stale).</summary>
public sealed record SaveSiteRequest(string? Slug, string? Template, string? Palette, string? Language, SiteContent? Draft, int? Version = null);

public sealed record SlugAvailabilityResponse(string Slug, bool Available, string? Reason);

public sealed record SitePreviewResponse(string Url, DateTime ExpiresAtUtc);

public static class SiteSectionTypes
{
    public const string Hero = "hero";
    public const string About = "about";
    public const string Subjects = "subjects";
    public const string Experience = "experience";
    public const string Offers = "offers";
    public const string Testimonials = "testimonials";
    public const string Faq = "faq";
    public const string Contact = "contact";

    public static IReadOnlyList<string> All { get; } = [Hero, About, Subjects, Experience, Offers, Testimonials, Faq, Contact];

    /// <summary>Section types whose content is a list of items.</summary>
    public static IReadOnlySet<string> WithItems { get; } = new HashSet<string>(StringComparer.Ordinal) { Experience, Testimonials, Faq };
}

/// <summary>Templates (layouts) of the public page.</summary>
public static class SiteTemplates
{
    public const string Classic = "classic";
    public const string Modern = "modern";
    public const string Minimal = "minimal";

    public static IReadOnlyList<string> All { get; } = [Classic, Modern, Minimal];
}

/// <summary>A curated colour palette. Text/background and button text/primary meet WCAG AA (4.5:1, tested).</summary>
public sealed record SitePalette(string Key, string Primary, string OnPrimary, string Background, string Surface, string Text, string Muted);

public static class SitePalettes
{
    public static IReadOnlyList<SitePalette> All { get; } =
    [
        new("ocean", "#0B5FA5", "#FFFFFF", "#F7FAFC", "#FFFFFF", "#102A43", "#486581"),
        new("forest", "#1F6F43", "#FFFFFF", "#F6FAF7", "#FFFFFF", "#132A1E", "#4A6355"),
        new("sunset", "#B23A1E", "#FFFFFF", "#FFF8F3", "#FFFFFF", "#2B1A12", "#6B4A3A"),
        new("plum", "#6B2D8C", "#FFFFFF", "#FAF7FC", "#FFFFFF", "#24132E", "#5E4A6B"),
        new("graphite", "#1F2933", "#FFFFFF", "#F5F7FA", "#FFFFFF", "#1F2933", "#52606D"),
    ];

    public static SitePalette? Find(string? key) => All.FirstOrDefault(p => p.Key == key);
}

/// <summary>
/// Public page addresses (<c>classhub.app/{slug}</c>). Lowercase letters, digits and single hyphens, 3–40 characters,
/// no leading/trailing hyphen; reserved words (app routes, system paths, common pages) are refused.
/// </summary>
public static partial class SiteSlugs
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    /// <summary>Kept in sync with the SPA prefixes and server routes (a test checks the SPA prefixes).</summary>
    public static IReadOnlySet<string> Reserved { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "app", "student", "admin", "api", "login", "logout", "register", "signup", "verify-email", "forgot-password", "reset-password",
        "dev", "dist", "assets", "static", "health", "openapi", "scalar", "preview", "account", "settings", "dashboard",
        "about", "help", "support", "contact", "terms", "privacy", "pricing", "blog", "docs", "status", "legal", "cookies",
        "classhub", "www", "mail", "email", "root", "system", "null", "undefined", "index", "home", "new", "edit",
        "sitemap", "robots", "favicon", "manifest", "sw", "service-worker", "teachers", "teacher", "students", "tests", "test",
        "book", "booking", "offers", "s", "t", "p", "u", "w",
    };

    // \z, not $: in .NET "$" also matches before a final newline ("admin\n").
    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9]))*\z")]
    private static partial Regex Shape();

    /// <summary>Null when valid; otherwise a reason code: <c>length</c>, <c>format</c> or <c>reserved</c>.</summary>
    public static string? Problem(string? slug)
    {
        if (slug is null || slug.Length < MinLength || slug.Length > MaxLength)
        {
            return "length";
        }

        if (!Shape().IsMatch(slug))
        {
            return "format";
        }

        return Reserved.Contains(slug) ? "reserved" : null;
    }
}

/// <summary>Site state of the current workspace for other modules (onboarding).</summary>
public interface ISiteStatus
{
    /// <summary>(Exists, IsPublished) of the current workspace's site.</summary>
    Task<(bool Exists, bool IsPublished)> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>Renders the draft for the preview page (Razor) from a signed preview token.</summary>
public interface ISitePreview
{
    /// <summary>The preview model, or null when the token is invalid, expired or no longer authorized.</summary>
    Task<SiteRenderModel?> ReadAsync(string token, CancellationToken cancellationToken);

    /// <summary>The teacher photo for a valid preview token (same checks as <see cref="ReadAsync"/>), or null.</summary>
    Task<(Stream Content, string ContentType)?> OpenPhotoAsync(string token, CancellationToken cancellationToken);
}

/// <summary>
/// Everything a site template may render (preview and public page use the same renderer). It is an allow-list:
/// only visible sections, and only the public profile and offer fields, ever reach a template.
/// </summary>
public sealed record SiteRenderModel(
    string Slug,
    string Template,
    SitePalette Palette,
    string Language,
    /// <summary>The visible sections that have content, in order (hidden and empty ones are not part of the model).</summary>
    IReadOnlyList<SiteSection> Sections,
    string? TeacherName,
    /// <summary>Relative photo URL, or null when the hero does not show the photo or there is none.</summary>
    string? PhotoUrl,
    SiteTeaching Teaching,
    IReadOnlyList<SiteOfferView> Offers);

/// <summary>From the teacher profile: subject, level and language names in the site language, and the teaching mode.</summary>
public sealed record SiteTeaching(IReadOnlyList<string> Subjects, IReadOnlyList<string> Levels, IReadOnlyList<string> Languages, string? Mode);

/// <summary>A public offer as shown on the site. <c>Price</c> is in major units of <c>Currency</c>.</summary>
public sealed record SiteOfferView(
    string Title,
    string? Description,
    string Kind,
    string? Subject,
    int DurationMinutes,
    decimal Price,
    string Currency,
    string Mode,
    int? LessonCount,
    int? Seats);

/// <summary>The SEO head of a public page (absolute URLs from the configured public base URL, never the Host header).</summary>
public sealed record SiteSeoHead(string Title, string Description, string CanonicalUrl, string? ImageUrl, string OgLocale, string JsonLd);

/// <summary>A published, visible public page and the output-cache tag it is stored under.</summary>
public sealed record PublicSitePage(SiteRenderModel Site, SiteSeoHead Seo, string CacheTag);

public sealed record SitemapEntry(string Slug, DateTime LastModifiedUtc);

/// <summary>
/// Read side of the public sites (Razor pages, photo, sitemap). Only published sites of visible owners (active
/// owner account) are returned; everything else is "not found".
/// </summary>
public interface IPublicSites
{
    /// <summary>The published page for <paramref name="slug"/>, or null (unknown, unpublished, hidden owner).</summary>
    Task<PublicSitePage?> FindAsync(string slug, CancellationToken cancellationToken);

    /// <summary>The teacher photo of a published site whose hero shows it; null otherwise.</summary>
    Task<(Stream Content, string ContentType)?> OpenPhotoAsync(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyList<SitemapEntry>> ListForSitemapAsync(CancellationToken cancellationToken);

    /// <summary>Counts one page view of a published site for today (UTC). Never throws.</summary>
    Task RecordViewAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// The workspace and language of a published site of a visible owner, or null. Binds nothing: callers (lead intake)
    /// decide how to read and write for that workspace.
    /// </summary>
    Task<PublishedSiteRef?> FindPublishedWorkspaceAsync(string slug, CancellationToken cancellationToken);
}

public sealed record PublishedSiteRef(Guid WorkspaceId, string Language);

/// <summary>
/// Clears cached public pages after a change that shows on them (site, profile, photo, offers). Call it after the
/// change committed.
/// </summary>
public interface IPublicSiteCache
{
    /// <summary>The current workspace's public page.</summary>
    Task EvictCurrentWorkspaceAsync(CancellationToken cancellationToken);

    /// <summary>Every public site page and the sitemap (account suspension and reactivation; rare).</summary>
    Task EvictAllAsync(CancellationToken cancellationToken);
}

/// <summary>Output-cache policy names and tags of the public pages.</summary>
public static class PublicSiteCaching
{
    public const string SitePolicy = "public-site";
    public const string StaticPolicy = "public-static";
    public const string SitemapPolicy = "public-sitemap";
    public const string SitemapTag = "public-sitemap";
    public const string LandingTag = "public-landing";

    /// <summary>Carried by every cached teacher site page (next to its workspace tag).</summary>
    public const string AllSitesTag = "public-sites";

    /// <summary>
    /// Cache entries vary by this query key only, which no page uses: arbitrary query strings (<c>?utm=…</c>) share
    /// one entry instead of filling the cache.
    /// </summary>
    public const string NoQueryKey = "__none";

    public static readonly TimeSpan SiteLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StaticLifetime = TimeSpan.FromHours(1);

    public static string WorkspaceTag(Guid workspaceId) => $"public-site:{workspaceId:N}";
}
