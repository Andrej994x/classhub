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
    int? Version);

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

/// <summary>Templates (layouts) of the public page; rendered in full by R1-P4.</summary>
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

/// <summary>Renders the draft for the preview page (Razor, R1-P3) from a signed preview token.</summary>
public interface ISitePreview
{
    /// <summary>The preview model, or null when the token is invalid, expired or no longer authorized.</summary>
    Task<SitePreviewModel?> ReadAsync(string token, CancellationToken cancellationToken);

    /// <summary>The teacher photo for a valid preview token (same checks as <see cref="ReadAsync"/>), or null.</summary>
    Task<(Stream Content, string ContentType)?> OpenPhotoAsync(string token, CancellationToken cancellationToken);
}

public sealed record SitePreviewModel(
    string Slug,
    string Template,
    SitePalette Palette,
    string Language,
    IReadOnlyList<SiteSection> Sections,
    string? TeacherName,
    string? PhotoUrl);
