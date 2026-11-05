using System.Security.Claims;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Admin.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Admin.Application;

/// <summary>
/// Platform administration (M15, R1-P9): aggregates, account metadata, suspension, the Platform Admin role and the
/// audit log. Never workspace content (profiles, students, leads, notes, materials) and no impersonation.
/// </summary>
internal sealed class AdminService(
    ClassHubDbContext db,
    UserManager<AppUser> users,
    IUserAccountStatus accountStatus,
    IMfaStepUp stepUp,
    IWorkspaceDirectory workspaces,
    ISiteDirectory sites,
    ISubscriptionStats subscriptions,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int PageSize = 20;
    public const int HistorySize = 20;
    public const int ActiveTeacherDays = 30;

    public async Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var all = db.Users.AsNoTracking();
        var total = await all.CountAsync(cancellationToken);
        var suspended = await all.CountAsync(u => u.Status == UserStatus.Suspended, cancellationToken);
        var week = await all.CountAsync(u => u.CreatedAtUtc >= now.AddDays(-7), cancellationToken);
        var month = await all.CountAsync(u => u.CreatedAtUtc >= now.AddDays(-30), cancellationToken);
        var teachers = await workspaces.CountTeachersAsync(now.AddDays(-ActiveTeacherDays), cancellationToken);
        var published = await sites.CountPublishedAsync(cancellationToken);
        var staff = await db.UserRoles.AsNoTracking().Select(r => r.UserId).Distinct().CountAsync(cancellationToken);
        var plans = await subscriptions.CountAsync(now.AddDays(-30), cancellationToken);
        return new AdminOverviewResponse(total, suspended, teachers.Teachers, teachers.ActiveTeachers, week, month, published, staff, plans.ProWorkspaces, plans.RevenueInPeriod);
    }

    public async Task<AdminUserPage> ListUsersAsync(string? search, string? status, bool? staff, int page, CancellationToken cancellationToken)
    {
        page = ValidPage(page);
        var query = db.Users.AsNoTracking();
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = Guid.TryParse(term, out var id) ? query.Where(u => u.Id == id) : query.Where(u => u.Email!.Contains(term));
        }

        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<UserStatus>(status, ignoreCase: false, out var wanted) || !Enum.IsDefined(wanted))
            {
                throw new BadRequestException("Unknown status.");
            }

            query = query.Where(u => u.Status == wanted);
        }

        if (staff is { } onlyStaff)
        {
            query = query.Where(u => db.UserRoles.Any(r => r.UserId == u.Id) == onlyStaff);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(u => u.CreatedAtUtc).ThenBy(u => u.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync(cancellationToken);
        return new AdminUserPage(await SummarizeAsync(rows, cancellationToken), total, page, PageSize);
    }

    public async Task<AdminUserDetail> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) ?? throw new NotFoundException();
        var summary = (await SummarizeAsync([user], cancellationToken))[0];
        var key = userId.ToString();
        var history = await ToEntriesAsync(
            db.Set<AuditLogEntry>().AsNoTracking()
                .Where(e => e.TargetType == "User" && e.TargetId == key)
                .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
                .Take(HistorySize),
            cancellationToken);
        var lockedOut = user.LockoutEnd is { } end && end > clock.GetUtcNow() ? end.UtcDateTime : (DateTime?)null;
        return new AdminUserDetail(summary, lockedOut, history);
    }

    public async Task<AdminUserDetail> SuspendAsync(ClaimsPrincipal actor, Guid userId, AdminStatusRequest request, CancellationToken cancellationToken)
    {
        await EnsureMayManageAsync(actor, userId);
        await accountStatus.SuspendAsync(userId, request.Reason!.Trim(), cancellationToken);
        return await GetUserAsync(userId, cancellationToken);
    }

    public async Task<AdminUserDetail> ReactivateAsync(ClaimsPrincipal actor, Guid userId, AdminStatusRequest request, CancellationToken cancellationToken)
    {
        await EnsureMayManageAsync(actor, userId);
        await accountStatus.ReactivateAsync(userId, request.Reason!.Trim(), cancellationToken);
        return await GetUserAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Grants or revokes Platform Admin (Platform Owner only, with a fresh authenticator code). The target's security
    /// stamp is rotated: the change applies at the next sign-in, and a new admin's session gets the shorter lifetime.
    /// </summary>
    public async Task<AdminUserDetail> SetAdminRoleAsync(ClaimsPrincipal actor, Guid userId, bool grant, AdminRoleRequest request, CancellationToken cancellationToken)
    {
        await stepUp.VerifyAsync(actor, request.Code, cancellationToken);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var target = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
            if (target.Id == ActorId(actor) || await users.IsInRoleAsync(target, PlatformRoles.Owner))
            {
                throw TargetForbidden("The Platform Owner role is managed only with the command-line tool.");
            }

            if (grant && target.Status != UserStatus.Active)
            {
                throw TargetForbidden("A suspended account cannot become an admin.");
            }

            var isAdmin = await users.IsInRoleAsync(target, PlatformRoles.Admin);
            if (isAdmin == grant)
            {
                throw new ConflictException(grant ? "The account is already a Platform Admin." : "The account is not a Platform Admin.");
            }

            try
            {
                ThrowIfFailed(grant
                    ? await users.AddToRoleAsync(target, PlatformRoles.Admin)
                    : await users.RemoveFromRoleAsync(target, PlatformRoles.Admin));
                ThrowIfFailed(await users.UpdateSecurityStampAsync(target));
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
            {
                throw new ConflictException("The account is already a Platform Admin.");
            }

            audit.Record(grant ? "platform.role.granted" : "platform.role.revoked", "User", target.Id.ToString(), after: new { role = PlatformRoles.Admin, via = "admin" });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetUserAsync(userId, cancellationToken);
    }

    public async Task<AdminAuditPage> ListAuditAsync(string? action, Guid? userId, Guid? workspaceId, DateOnly? from, DateOnly? to, int page, CancellationToken cancellationToken)
    {
        page = ValidPage(page);
        if (from is { } start && to is { } end && end < start)
        {
            throw new BadRequestException("The end date is before the start date.");
        }

        var query = db.Set<AuditLogEntry>().AsNoTracking();
        var prefix = action?.Trim();
        if (!string.IsNullOrEmpty(prefix))
        {
            query = query.Where(e => e.Action.StartsWith(prefix));
        }

        if (userId is { } user)
        {
            var key = user.ToString();
            query = query.Where(e => e.ActorUserId == user || (e.TargetType == "User" && e.TargetId == key));
        }

        if (workspaceId is { } workspace)
        {
            query = query.Where(e => e.WorkspaceId == workspace);
        }

        if (from is { } since)
        {
            var sinceUtc = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(e => e.OccurredAtUtc >= sinceUtc);
        }

        if (to is { } until)
        {
            var untilUtc = until.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(e => e.OccurredAtUtc < untilUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await ToEntriesAsync(
            query.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).Skip((page - 1) * PageSize).Take(PageSize),
            cancellationToken);
        return new AdminAuditPage(items, total, page, PageSize);
    }

    /// <summary>
    /// Nobody changes their own account here; Platform Owners are managed only with the command-line tool; only the
    /// Platform Owner may suspend or reactivate a Platform Admin.
    /// </summary>
    private async Task EnsureMayManageAsync(ClaimsPrincipal actor, Guid userId)
    {
        var target = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException();
        if (target.Id == ActorId(actor))
        {
            throw TargetForbidden("You cannot change your own account here.");
        }

        var roles = await users.GetRolesAsync(target);
        if (roles.Contains(PlatformRoles.Owner) || (roles.Contains(PlatformRoles.Admin) && !actor.IsInRole(PlatformRoles.Owner)))
        {
            throw TargetForbidden("Only the Platform Owner manages platform staff, and owners only with the command-line tool.");
        }
    }

    private async Task<List<AdminUserSummary>> SummarizeAsync(IReadOnlyList<AppUser> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(u => u.Id).ToList();
        var roles = (await db.UserRoles.AsNoTracking()
                .Where(r => ids.Contains(r.UserId))
                .Join(db.Roles.AsNoTracking(), r => r.RoleId, role => role.Id, (r, role) => new { r.UserId, role.Name })
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.UserId, r => r.Name!);
        var owned = await workspaces.OwnedByAsync(ids, cancellationToken);
        var siteByWorkspace = await sites.ForWorkspacesAsync([.. owned.Values.Select(w => w.WorkspaceId)], cancellationToken);

        return [.. rows.Select(u =>
        {
            AdminWorkspaceSummary? workspace = null;
            if (owned.TryGetValue(u.Id, out var w))
            {
                siteByWorkspace.TryGetValue(w.WorkspaceId, out var site);
                workspace = new AdminWorkspaceSummary(w.WorkspaceId, w.Name, w.CreatedAtUtc, site?.Slug, site?.Published ?? false);
            }

            return new AdminUserSummary(u.Id, u.Email!, u.EmailConfirmed, u.Status.ToString(), [.. roles[u.Id].Order(StringComparer.Ordinal)],
                u.TwoFactorEnabled, u.CreatedAtUtc, u.LastLoginAtUtc, workspace);
        })];
    }

    private async Task<List<AdminAuditEntry>> ToEntriesAsync(IQueryable<AuditLogEntry> query, CancellationToken cancellationToken)
    {
        var entries = await query.ToListAsync(cancellationToken);
        var actorIds = entries.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var emails = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email!, cancellationToken);
        return [.. entries.Select(e => new AdminAuditEntry(
            e.Id,
            e.OccurredAtUtc,
            e.ActorUserId,
            e.ActorUserId is { } actor && emails.TryGetValue(actor, out var email) ? email : null,
            e.Action,
            e.TargetType,
            e.TargetId,
            e.WorkspaceId,
            // Workspace entries can carry the teacher's own values: platform staff see what happened, not the data.
            e.WorkspaceId is null ? e.DetailsJson : null))];
    }

    private static int ValidPage(int page) =>
        page is >= 1 and <= 10_000 ? page : throw new BadRequestException("The page must be between 1 and 10000.");

    private static Guid? ActorId(ClaimsPrincipal actor) =>
        Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static ForbiddenOperationException TargetForbidden(string message) => new(message, ErrorCodes.AdminTargetForbidden);

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
