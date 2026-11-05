using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Sites.Application;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Sites.Persistence;

/// <summary>
/// Moderation reads and changes of sites across tenants (allow-listed tenant-filter bypass; platform staff with MFA, or
/// the anonymous report endpoint for <see cref="FindPublicAsync"/>). Every query filters by slug or workspace.
/// </summary>
internal sealed class SiteModerationQueries(ClassHubDbContext db, IWorkspacePublicVisibility visibility) : ISiteModeration
{
    private IQueryable<Site> Sites() => db.Set<Site>().IgnoreQueryFilters([TenancyFilters.Tenant]);

    /// <summary>
    /// Serializes moderation changes of one site until the caller's transaction ends: the hidden sections are a JSON
    /// list (read, change, write), so two staff hiding different sections at once must not overwrite each other.
    /// </summary>
    private async Task<Site?> LockedSiteAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Moderation changes run inside the caller's transaction.");
        }

        var resource = $"classhub:site-moderation:{workspaceId:N}";
        await db.Database.ExecuteSqlAsync(
            $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 50005, 'Could not acquire the site moderation lock.', 1;",
            cancellationToken);
        return await Sites().SingleOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken);
    }

    public async Task<ModeratedSite?> FindPublicAsync(string slug, CancellationToken cancellationToken)
    {
        if (SiteSlugs.Problem(slug) is not null)
        {
            return null;
        }

        var site = await Sites().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Slug == slug && s.PublishedAtUtc != null && s.PublishedJson != null && s.HiddenAtUtc == null, cancellationToken);
        if (site is null || !(await visibility.VisibleAsync([site.WorkspaceId], cancellationToken)).Contains(site.WorkspaceId))
        {
            return null;
        }

        return ToModerated(site);
    }

    public async Task<ModeratedSite?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await Sites().AsNoTracking().SingleOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken) is { } site ? ToModerated(site) : null;

    public async Task<bool> HideAsync(Guid workspaceId, string? sectionType, string reason, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var site = await LockedSiteAsync(workspaceId, cancellationToken);
        if (site is null)
        {
            return false;
        }

        if (sectionType is null)
        {
            if (site.HiddenAtUtc is not null)
            {
                return false;
            }

            site.HiddenAtUtc = nowUtc;
            site.HiddenReason = reason;
            return true;
        }

        var sections = HiddenSection.Read(site.HiddenSectionsJson).ToList();
        if (sections.Any(h => h.Type == sectionType))
        {
            return false;
        }

        sections.Add(new HiddenSection(sectionType, reason, nowUtc));
        site.HiddenSectionsJson = HiddenSection.Write(sections);
        return true;
    }

    public async Task<bool> UnhideAsync(Guid workspaceId, string? sectionType, CancellationToken cancellationToken)
    {
        var site = await LockedSiteAsync(workspaceId, cancellationToken);
        if (site is null)
        {
            return false;
        }

        if (sectionType is null)
        {
            if (site.HiddenAtUtc is null)
            {
                return false;
            }

            site.HiddenAtUtc = null;
            site.HiddenReason = null;
            return true;
        }

        var sections = HiddenSection.Read(site.HiddenSectionsJson).ToList();
        if (sections.RemoveAll(h => h.Type == sectionType) == 0)
        {
            return false;
        }

        site.HiddenSectionsJson = HiddenSection.Write(sections);
        return true;
    }

    public async Task<IReadOnlyList<HiddenSiteContent>> ListHiddenAsync(CancellationToken cancellationToken)
    {
        var rows = await Sites().AsNoTracking()
            .Where(s => s.HiddenAtUtc != null || s.HiddenSectionsJson != null)
            .Select(s => new { s.WorkspaceId, s.Slug, s.HiddenAtUtc, s.HiddenReason, s.HiddenSectionsJson })
            .ToListAsync(cancellationToken);
        var hidden = new List<HiddenSiteContent>();
        foreach (var row in rows)
        {
            if (row.HiddenAtUtc is { } at)
            {
                hidden.Add(new HiddenSiteContent(row.WorkspaceId, row.Slug, null, row.HiddenReason!, at));
            }

            hidden.AddRange(HiddenSection.Read(row.HiddenSectionsJson).Select(h => new HiddenSiteContent(row.WorkspaceId, row.Slug, h.Type, h.Reason, h.HiddenAtUtc)));
        }

        return [.. hidden.OrderByDescending(h => h.HiddenAtUtc)];
    }

    private static ModeratedSite ToModerated(Site site)
    {
        var published = site.PublishedAtUtc is not null && site.PublishedJson is not null;
        var hiddenSections = HiddenSection.Read(site.HiddenSectionsJson).Select(h => h.Type).ToHashSet(StringComparer.Ordinal);
        var visible = published && SiteSnapshot.Read(site.PublishedJson) is { } snapshot
            ? snapshot.Sections.Where(s => s.Visible && s.Type is not null && !hiddenSections.Contains(s.Type)).Select(s => s.Type!).Distinct().ToList()
            : [];
        return new ModeratedSite(site.WorkspaceId, site.Id, site.Slug, published, visible, SiteService.ModerationNote(site));
    }
}
