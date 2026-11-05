using System.Text.Json;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.Modules.Sites.Persistence;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>The site of the current (verified) workspace: draft editing, slug checks and preview tokens.</summary>
internal sealed class SiteService(
    ClassHubDbContext db,
    ITenantContext tenant,
    ITeacherSummary teacher,
    IWorkspaceSettings workspace,
    SitePreviewTokens previewTokens,
    SiteSlugQueries slugs,
    PublicSiteCache cache,
    IAuditLog audit,
    TimeProvider clock)
{
    /// <summary>Page views are summed over this many UTC days (today included).</summary>
    public const int ViewWindowDays = 30;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SiteResponse> GetAsync(CancellationToken cancellationToken)
    {
        var site = await db.Set<Site>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (site is not null)
        {
            return await ToResponseAsync(site, cancellationToken);
        }

        // Not created yet: suggestions the editor starts from (nothing is stored until the first save).
        var summary = await teacher.GetSummaryAsync(cancellationToken);
        var language = await workspace.GetLocaleAsync(cancellationToken);
        return new SiteResponse(false, SuggestSlug(summary), SiteTemplates.Classic, SitePalettes.All[0].Key, language,
            DefaultContent(language), false, null, null, null, false, 0);
    }

    public async Task<SiteResponse> SaveAsync(SaveSiteRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var site = await db.Set<Site>().SingleOrDefaultAsync(cancellationToken);
        if ((request.Version ?? 0) != (site?.Version ?? 0))
        {
            throw new ConflictException("The site was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        var draftJson = JsonSerializer.Serialize(Normalize(request.Draft!), Json);
        var previousSlug = site?.Slug;
        if (site is null)
        {
            site = new Site
            {
                Id = Guid.NewGuid(),
                WorkspaceId = tenant.RequireWorkspaceId(),
                Slug = request.Slug!,
                Template = request.Template!,
                Palette = request.Palette!,
                Language = request.Language!,
                DraftJson = draftJson,
                CreatedAtUtc = now,
            };
            db.Set<Site>().Add(site);
        }
        else
        {
            site.Slug = request.Slug!;
            site.Template = request.Template!;
            site.Palette = request.Palette!;
            site.Language = request.Language!;
            site.DraftJson = draftJson;
        }

        site.UpdatedAtUtc = now;
        site.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            // The slug index (or, in a race, the one-site-per-workspace index) refused the write: another teacher
            // claimed this address first. The database decides, so parallel claims can never both win.
            throw new ConflictException("This address is already taken. Choose another one.", ErrorCodes.SlugTaken);
        }

        // Draft edits do not change the public page, but the address does (the slug applies at once).
        if (site.PublishedAtUtc is not null && previousSlug != site.Slug)
        {
            await cache.EvictCurrentWorkspaceAsync(cancellationToken);
            await cache.EvictSitemapAsync();
        }

        return await ToResponseAsync(site, cancellationToken);
    }

    /// <summary>
    /// Publishes the saved draft: template, palette, language and sections are copied to the public version, which
    /// visitors see at <c>/{slug}</c> from now on. Audited.
    /// </summary>
    public async Task<SiteResponse> PublishAsync(PublishSiteRequest request, CancellationToken cancellationToken)
    {
        var site = await LoadForChangeAsync(request.Version, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var firstPublication = site.PublishedAtUtc is null;
        site.PublishedJson = SiteSnapshot.FromDraft(site).Serialize();
        site.PublishedAtUtc = now;
        site.UpdatedAtUtc = now;
        site.Version++;
        audit.Record("site.published", "Site", site.Id.ToString(), site.WorkspaceId, after: new { site.Slug, site.Template, site.Palette, site.Language });
        await db.SaveChangesAsync(cancellationToken);
        await cache.EvictCurrentWorkspaceAsync(cancellationToken);
        if (firstPublication)
        {
            await cache.EvictSitemapAsync();
        }

        return await ToResponseAsync(site, cancellationToken);
    }

    /// <summary>Takes the page offline: <c>/{slug}</c> answers 404 and leaves the sitemap. The draft is kept. Audited.</summary>
    public async Task<SiteResponse> UnpublishAsync(PublishSiteRequest request, CancellationToken cancellationToken)
    {
        var site = await LoadForChangeAsync(request.Version, cancellationToken);
        if (site.PublishedAtUtc is null)
        {
            return await ToResponseAsync(site, cancellationToken);
        }

        site.PublishedJson = null;
        site.PublishedAtUtc = null;
        site.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        site.Version++;
        audit.Record("site.unpublished", "Site", site.Id.ToString(), site.WorkspaceId, before: new { site.Slug });
        await db.SaveChangesAsync(cancellationToken);
        await cache.EvictCurrentWorkspaceAsync(cancellationToken);
        await cache.EvictSitemapAsync();
        return await ToResponseAsync(site, cancellationToken);
    }

    private async Task<Site> LoadForChangeAsync(int? version, CancellationToken cancellationToken)
    {
        var site = await db.Set<Site>().SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("Save the site once before publishing it.", ErrorCodes.SiteNotCreated);
        if (version != site.Version)
        {
            throw new ConflictException("The site was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        return site;
    }

    public async Task<SlugAvailabilityResponse> CheckSlugAsync(string? slug, CancellationToken cancellationToken)
    {
        var candidate = (slug ?? string.Empty).Trim();
        if (SiteSlugs.Problem(candidate) is { } problem)
        {
            return new SlugAvailabilityResponse(candidate, false, problem);
        }

        var ownSiteId = await db.Set<Site>().Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
        var taken = await slugs.IsTakenByAnotherSiteAsync(candidate, ownSiteId, cancellationToken);
        return new SlugAvailabilityResponse(candidate, !taken, taken ? "taken" : null);
    }

    public async Task<SitePreviewResponse> CreatePreviewAsync(CancellationToken cancellationToken)
    {
        var site = await db.Set<Site>().AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("Save the site once before previewing it.", ErrorCodes.SiteNotCreated);
        var (token, expires) = previewTokens.Issue(site.Id, site.WorkspaceId, tenant.UserId!.Value);
        return new SitePreviewResponse($"/preview/{token}", expires);
    }

    internal static SiteContent Read(string json) =>
        JsonSerializer.Deserialize<SiteContent>(json, Json) ?? new SiteContent([]);

    /// <summary>Trims texts, drops empty optional values, and keeps only the fields each section type uses.</summary>
    internal static SiteContent Normalize(SiteContent content) => new(
        [.. content.Sections!.Select(s => s with
        {
            Title = Clean(s.Title),
            Text = Clean(s.Text),
            ShowPhoto = s.Type == SiteSectionTypes.Hero && s.ShowPhoto,
            Items = SiteSectionTypes.WithItems.Contains(s.Type!)
                ? [.. (s.Items ?? []).Select(i => new SiteSectionItem(Clean(i.Title), Clean(i.Subtitle), Clean(i.Text)))]
                : null,
        })]);

    internal static SiteContent DefaultContent(string language)
    {
        var en = language == "en";
        return new SiteContent(
        [
            new(SiteSectionTypes.Hero, true, en ? "Lessons that make sense" : "Часови што имаат смисла", en ? "Tell visitors in one sentence how you help." : "Во една реченица кажете како помагате.", ShowPhoto: true),
            new(SiteSectionTypes.About, true, en ? "About me" : "За мене", null),
            new(SiteSectionTypes.Subjects, true, en ? "What I teach" : "Што предавам", null),
            new(SiteSectionTypes.Experience, false, en ? "Teaching experience" : "Наставно искуство", null, Items: []),
            new(SiteSectionTypes.Offers, true, en ? "Offers & pricing" : "Понуди и цени", null),
            new(SiteSectionTypes.Testimonials, false, en ? "What students say" : "Што велат учениците", null, Items: []),
            new(SiteSectionTypes.Faq, false, en ? "Frequently asked questions" : "Често поставувани прашања", null, Items: []),
            new(SiteSectionTypes.Contact, true, en ? "Contact" : "Контакт", en ? "Send me a message and I will get back to you." : "Испратете ми порака и ќе ви одговорам."),
        ]);
    }

    /// <summary>"Ана Петровска" → "ana-petrovska" (Cyrillic transliterated), falling back to "teacher-xxxx".</summary>
    internal static string SuggestSlug(TeacherSummary summary)
    {
        var name = $"{summary.FirstName} {summary.LastName}".Trim().ToLowerInvariant();
        var slug = string.Concat(name.Select(Transliterate));
        slug = string.Join('-', slug.Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > SiteSlugs.MaxLength)
        {
            slug = slug[..SiteSlugs.MaxLength].TrimEnd('-');
        }

        return SiteSlugs.Problem(slug) is null ? slug : $"teacher-{Guid.NewGuid().ToString("N")[..6]}";
    }

    private static string Transliterate(char c) => c switch
    {
        >= 'a' and <= 'z' or >= '0' and <= '9' => c.ToString(),
        'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'ѓ' => "gj", 'е' => "e", 'ж' => "zh", 'з' => "z", 'ѕ' => "dz",
        'и' => "i", 'ј' => "j", 'к' => "k", 'л' => "l", 'љ' => "lj", 'м' => "m", 'н' => "n", 'њ' => "nj", 'о' => "o", 'п' => "p",
        'р' => "r", 'с' => "s", 'т' => "t", 'ќ' => "kj", 'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "c", 'ч' => "ch", 'џ' => "dj", 'ш' => "sh",
        _ => "-",
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<SiteResponse> ToResponseAsync(Site site, CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-(ViewWindowDays - 1));
        var views = await db.Set<SitePageView>().Where(v => v.SiteId == site.Id && v.Day >= from).SumAsync(v => (int?)v.Views, cancellationToken) ?? 0;
        var published = site.PublishedAtUtc is not null;
        var changed = published && site.PublishedJson != SiteSnapshot.FromDraft(site).Serialize();
        return new(true, site.Slug, site.Template, site.Palette, site.Language, Read(site.DraftJson), published, site.UpdatedAtUtc, site.Version,
            site.PublishedAtUtc, changed, views);
    }
}

/// <summary>
/// Site state for other modules (onboarding). Deliberately its own small class with no module dependencies: the
/// teacher profile asks for it, and the site service itself depends on the teacher summary.
/// </summary>
internal sealed class SiteStatus(ClassHubDbContext db) : ISiteStatus
{
    public async Task<(bool Exists, bool IsPublished)> GetStatusAsync(CancellationToken cancellationToken)
    {
        var published = await db.Set<Site>().Select(s => (DateTime?)s.PublishedAtUtc).ToListAsync(cancellationToken);
        return (published.Count > 0, published.Any(p => p is not null));
    }
}
