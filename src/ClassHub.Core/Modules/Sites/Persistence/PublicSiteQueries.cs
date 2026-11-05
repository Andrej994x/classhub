using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Sites.Persistence;

/// <summary>
/// Public, anonymous lookups of published sites (allow-listed tenant-filter bypass: a visitor's request belongs to no
/// workspace until the slug is resolved). Only published sites are ever returned, and only the columns the public
/// page needs. Moderation (R1-P11) adds its "hidden" condition here, so every public read path inherits it.
/// </summary>
internal sealed class PublicSiteQueries(ClassHubDbContext db)
{
    internal sealed record PublishedSite(Guid Id, Guid WorkspaceId, string Slug, string PublishedJson, DateTime PublishedAtUtc);

    internal sealed record SitemapRow(Guid WorkspaceId, string Slug, DateTime PublishedAtUtc);

    private IQueryable<Site> Published() =>
        db.Set<Site>().IgnoreQueryFilters([TenancyFilters.Tenant]).AsNoTracking()
            .Where(s => s.PublishedAtUtc != null && s.PublishedJson != null);

    public Task<PublishedSite?> FindPublishedAsync(string slug, CancellationToken cancellationToken) =>
        Published().Where(s => s.Slug == slug)
            .Select(s => new PublishedSite(s.Id, s.WorkspaceId, s.Slug, s.PublishedJson!, s.PublishedAtUtc!.Value))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SitemapRow>> ListPublishedAsync(CancellationToken cancellationToken) =>
        await Published().OrderBy(s => s.Slug)
            .Select(s => new SitemapRow(s.WorkspaceId, s.Slug, s.PublishedAtUtc!.Value))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Adds one view of the published site for <paramref name="day"/> in a single atomic statement (HOLDLOCK makes the
    /// insert-or-increment safe under concurrent first views of the day). Unknown or unpublished slugs change nothing.
    /// </summary>
    public async Task RecordViewAsync(string slug, DateOnly day, CancellationToken cancellationToken)
    {
        var site = await Published().Where(s => s.Slug == slug).Select(s => new { s.Id, s.WorkspaceId }).SingleOrDefaultAsync(cancellationToken);
        if (site is null)
        {
            return;
        }

        await db.Database.ExecuteSqlAsync(
            $"""
            MERGE [SitePageViews] WITH (HOLDLOCK) AS t
            USING (SELECT {site.WorkspaceId} AS [WorkspaceId], {site.Id} AS [SiteId], {day} AS [Day]) AS s
            ON t.[SiteId] = s.[SiteId] AND t.[Day] = s.[Day]
            WHEN MATCHED THEN UPDATE SET t.[Views] = t.[Views] + 1
            WHEN NOT MATCHED THEN INSERT ([WorkspaceId], [SiteId], [Day], [Views]) VALUES (s.[WorkspaceId], s.[SiteId], s.[Day], 1);
            """,
            cancellationToken);
    }
}
