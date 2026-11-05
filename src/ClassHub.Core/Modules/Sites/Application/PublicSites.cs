using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Persistence;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>
/// The public read side. A slug resolves to a published site of a visible owner; only then is the request bound to
/// that workspace, read-only, so the normal tenant filters serve the profile and offers and no write is possible.
/// </summary>
internal sealed partial class PublicSites(
    PublicSiteQueries queries,
    IWorkspacePublicVisibility visibility,
    TenantContext tenant,
    SiteRenderer renderer,
    ITeacherPhoto photos,
    IPublicTests tests,
    IOptions<AppLinkOptions> links,
    TimeProvider clock,
    ILogger<PublicSites> logger) : IPublicSites
{
    public async Task<PublicSitePage?> FindAsync(string slug, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(slug, cancellationToken) is not (var site, var snapshot))
        {
            return null;
        }

        var model = await renderer.RenderAsync(site.Slug, snapshot, version => $"/{site.Slug}/photo?v={Uri.EscapeDataString(version)}", cancellationToken);
        return new PublicSitePage(model, SiteSeo.Build(model, links.Value.PublicBaseUrl), PublicSiteCaching.WorkspaceTag(site.WorkspaceId));
    }

    public async Task<(Stream Content, string ContentType)?> OpenPhotoAsync(string slug, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(slug, cancellationToken) is not (_, var snapshot)
            || !snapshot.Sections.Any(s => s.Type == SiteSectionTypes.Hero && s.Visible && s.ShowPhoto))
        {
            return null;
        }

        return await photos.OpenAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SitemapEntry>> ListForSitemapAsync(CancellationToken cancellationToken)
    {
        var rows = await queries.ListPublishedAsync(cancellationToken);
        var visible = await visibility.VisibleAsync([.. rows.Select(r => r.WorkspaceId).Distinct()], cancellationToken);
        var published = (await tests.ListPublishedForWorkspacesAsync([.. visible], cancellationToken)).ToLookup(t => t.WorkspaceId, t => t.Test);
        return
        [
            .. rows.Where(r => visible.Contains(r.WorkspaceId)).Select(r => new SitemapEntry(r.Slug, r.PublishedAtUtc,
                [.. published[r.WorkspaceId].Select(t => new SitemapEntry(t.Slug, t.PublishedAtUtc))])),
        ];
    }

    public async Task RecordViewAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            await queries.RecordViewAsync(slug, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lost count must never break the page.
            LogViewNotRecorded(logger, ex);
        }
    }

    /// <summary>The published test page of a visible site, or null.</summary>
    public async Task<PublicTestPage?> FindTestAsync(string slug, string testSlug, CancellationToken cancellationToken)
    {
        if (await FindAsync(slug, cancellationToken) is not { } page || await tests.FindAsync(testSlug, page.Site.Language, cancellationToken) is not { } test)
        {
            return null;
        }

        var baseUrl = links.Value.PublicBaseUrl.TrimEnd('/');
        var description = test.Description ?? page.Seo.Description;
        var title = page.Site.TeacherName is { } name ? $"{test.Title} — {name}" : test.Title;
        return new PublicTestPage(page.Site, test, new SiteSeoHead(
            SiteSeo.Truncate(title, SiteSeo.TitleMaxLength),
            SiteSeo.Truncate(description, SiteSeo.DescriptionMaxLength),
            $"{baseUrl}/{page.Site.Slug}/tests/{test.Slug}",
            page.Seo.ImageUrl,
            page.Seo.OgLocale,
            "{}"), page.CacheTag);
    }

    public async Task<PublishedSiteRef?> FindPublishedWorkspaceAsync(string slug, CancellationToken cancellationToken)
    {
        if (SiteSlugs.Problem(slug) is not null || await queries.FindPublishedAsync(slug, cancellationToken) is not { } site
            || !(await visibility.VisibleAsync([site.WorkspaceId], cancellationToken)).Contains(site.WorkspaceId)
            || SiteSnapshot.Read(site.PublishedJson) is not { } snapshot)
        {
            return null;
        }

        return new PublishedSiteRef(site.WorkspaceId, snapshot.Language == SiteLanguages.English ? SiteLanguages.English : SiteLanguages.Macedonian);
    }

    private async Task<(PublicSiteQueries.PublishedSite Site, SiteSnapshot Snapshot)?> ResolveAsync(string slug, CancellationToken cancellationToken)
    {
        if (SiteSlugs.Problem(slug) is not null || await queries.FindPublishedAsync(slug, cancellationToken) is not { } site)
        {
            return null;
        }

        if (!(await visibility.VisibleAsync([site.WorkspaceId], cancellationToken)).Contains(site.WorkspaceId))
        {
            return null;
        }

        tenant.SetPublicReadOnly(site.WorkspaceId);
        return SiteSnapshot.Read(site.PublishedJson) is { } snapshot ? (site, snapshot) : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A public page view could not be recorded.")]
    private static partial void LogViewNotRecorded(ILogger logger, Exception exception);
}

/// <summary>Evicts the output-cache entries of the current workspace's public page.</summary>
internal sealed class PublicSiteCache(ITenantContext tenant, IOutputCacheStore store) : IPublicSiteCache
{
    // Not the request's token: the change is committed, so the eviction must run even if the client went away.
    public async Task EvictCurrentWorkspaceAsync(CancellationToken cancellationToken) =>
        await store.EvictByTagAsync(PublicSiteCaching.WorkspaceTag(tenant.RequireWorkspaceId()), CancellationToken.None);

    public async Task EvictSitemapAsync(CancellationToken cancellationToken = default) =>
        await store.EvictByTagAsync(PublicSiteCaching.SitemapTag, CancellationToken.None);

    public async Task EvictAllAsync(CancellationToken cancellationToken)
    {
        await store.EvictByTagAsync(PublicSiteCaching.AllSitesTag, CancellationToken.None);
        await EvictSitemapAsync(cancellationToken);
    }
}
