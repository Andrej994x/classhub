using System.Security.Cryptography;
using System.Text.Json;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Sites.Application;

/// <summary>
/// Short-lived, signed preview tokens (Data Protection: authenticated encryption). A token names one site, its
/// workspace and the member who requested it, and expires after <see cref="Lifetime"/>. It is a capability for one
/// read-only, noindex preview of the draft; it grants nothing else.
/// </summary>
internal sealed class SitePreviewTokens(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("ClassHub.Sites.Preview.v1");

    internal sealed record Payload(Guid SiteId, Guid WorkspaceId, Guid UserId, long ExpiresUnix);

    public (string Token, DateTime ExpiresAtUtc) Issue(Guid siteId, Guid workspaceId, Guid userId, TimeSpan? lifetime = null)
    {
        var expires = clock.GetUtcNow().Add(lifetime ?? Lifetime);
        var json = JsonSerializer.Serialize(new Payload(siteId, workspaceId, userId, expires.ToUnixTimeSeconds()));
        return (_protector.Protect(json), expires.UtcDateTime);
    }

    /// <summary>The payload of a valid, unexpired token; null for tampered, foreign or expired tokens.</summary>
    public Payload? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
            return payload is not null && payload.ExpiresUnix > clock.GetUtcNow().ToUnixTimeSeconds() ? payload : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads the draft for the preview page. The token is validated first, the requesting user must still be an active
/// member, and only then is the request bound to that workspace: from there on the normal tenant filters apply.
/// </summary>
internal sealed class SitePreview(
    SitePreviewTokens tokens,
    TenantContext tenant,
    IWorkspaceMembershipLookup memberships,
    ClassHubDbContext db,
    ITeacherSummary teacher) : ISitePreview
{
    public async Task<SitePreviewModel?> ReadAsync(string token, CancellationToken cancellationToken)
    {
        if (tokens.Validate(token) is not { } payload)
        {
            return null;
        }

        var role = await memberships.GetActiveRoleAsync(payload.WorkspaceId, payload.UserId, cancellationToken);
        if (role is not { } activeRole || !WorkspaceRolePermissions.Has(activeRole, WorkspacePermissions.ProfileRead))
        {
            return null;
        }

        tenant.Set(payload.WorkspaceId, payload.UserId, activeRole);
        var site = await db.Set<Site>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == payload.SiteId, cancellationToken);
        if (site is null)
        {
            return null;
        }

        var summary = await teacher.GetSummaryAsync(cancellationToken);
        var name = $"{summary.FirstName} {summary.LastName}".Trim();
        var photo = summary.PhotoVersion is { } version ? $"/api/w/{payload.WorkspaceId}/profile/photo?v={Uri.EscapeDataString(version)}" : null;
        return new SitePreviewModel(
            site.Slug,
            site.Template,
            SitePalettes.Find(site.Palette) ?? SitePalettes.All[0],
            site.Language,
            [.. SiteService.Read(site.DraftJson).Sections ?? []],
            name.Length == 0 ? null : name,
            photo);
    }
}
