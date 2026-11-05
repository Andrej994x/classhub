using System.Net;
using System.Security.Claims;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Moderation.Contracts;
using ClassHub.Core.Modules.Moderation.Domain;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Moderation.Application;

/// <summary>Moderation settings (section <c>Moderation</c>).</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>Reports stored per site per hour; more are accepted but not stored (queue flooding), on top of the per-IP limit.</summary>
    public int MaxReportsPerSitePerHour { get; set; } = 20;
}

/// <summary>
/// Anonymous reports from the public pages (R1-P11): rate-limited per IP (<c>public-form</c>), a honeypot, and a cap per
/// site. Only content a visitor can see now can be reported.
/// </summary>
internal sealed class ReportIntake(ClassHubDbContext db, ISiteModeration sites, ITestModeration tests, IOptions<ModerationOptions> options, TimeProvider clock)
{
    public async Task SubmitAsync(string slug, SubmitReportRequest request, CancellationToken cancellationToken)
    {
        var site = await sites.FindPublicAsync(slug, cancellationToken) ?? throw new NotFoundException();
        if (!string.IsNullOrEmpty(request.Website))
        {
            return; // a bot filled the hidden field: same answer, nothing stored
        }

        var target = ReportNames.Targets[request.Target!];
        Guid? testId = null;
        if (target == ReportTarget.Section && !site.VisibleSectionTypes.Contains(request.SectionType!))
        {
            throw Invalid("sectionType", "This section is not on the page.");
        }

        if (target == ReportTarget.Test)
        {
            testId = (await tests.FindPublicAsync(site.WorkspaceId, request.TestSlug!, cancellationToken) ?? throw new NotFoundException()).TestId;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var recent = await db.Set<AbuseReport>().CountAsync(r => r.WorkspaceId == site.WorkspaceId && r.CreatedAtUtc >= now.AddHours(-1), cancellationToken);
        if (recent >= options.Value.MaxReportsPerSitePerHour)
        {
            return; // enough reports about this site to act on; the queue is not flooded
        }

        db.Set<AbuseReport>().Add(new AbuseReport
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = site.WorkspaceId,
            Target = target,
            SectionType = target == ReportTarget.Section ? request.SectionType : null,
            TestId = testId,
            Reason = ReportNames.Reasons[request.Reason!],
            Details = Clean(request.Details),
            ReporterEmail = Clean(request.Email)?.ToLowerInvariant(),
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}

/// <summary>
/// The moderation queue for platform staff: hide the reported content (site, section or test) with a reason, which
/// resolves every open report about it and tells the teacher; dismiss; and show hidden content again. Hidden content is
/// excluded by the public queries themselves; the public pages are evicted after each commit.
/// </summary>
internal sealed class ModerationService(
    ClassHubDbContext db,
    ISiteModeration sites,
    ITestModeration tests,
    IWorkspaceOwnerContact owners,
    IPublicSiteCache publicSites,
    IEmailOutbox outbox,
    IAuditLog audit,
    IOptions<AppLinkOptions> links,
    TimeProvider clock)
{
    public const int PageSize = 20;

    public async Task<AdminReportPage> ListAsync(string? status, int page, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 10_000)
        {
            throw new BadRequestException("The page must be between 1 and 10000.");
        }

        var query = db.Set<AbuseReport>().AsNoTracking();
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<ReportStatus>(status, ignoreCase: false, out var wanted) || !Enum.IsDefined(wanted))
            {
                throw new BadRequestException("Unknown status.");
            }

            query = query.Where(r => r.Status == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        var items = new List<AdminReportResponse>();
        foreach (var row in rows)
        {
            items.Add(await ToResponseAsync(row, cancellationToken));
        }

        return new AdminReportPage(items, total, page, PageSize);
    }

    public async Task<AdminReportResponse> GetAsync(Guid reportId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await db.Set<AbuseReport>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken) ?? throw new NotFoundException(), cancellationToken);

    /// <summary>Hides the reported content (if it is not hidden yet), resolves every open report about it, tells the teacher.</summary>
    public async Task<AdminReportResponse> HideAsync(ClaimsPrincipal actor, Guid reportId, HideContentRequest request, CancellationToken cancellationToken)
    {
        var reason = request.Reason!.Trim();
        var now = clock.GetUtcNow().UtcDateTime;
        AbuseReport report;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            report = await OpenReportAsync(reportId, cancellationToken);
            var hidden = report.Target switch
            {
                ReportTarget.Test => await tests.HideAsync(report.WorkspaceId, report.TestId!.Value, reason, now, cancellationToken),
                ReportTarget.Section => await sites.HideAsync(report.WorkspaceId, report.SectionType, reason, now, cancellationToken),
                _ => await sites.HideAsync(report.WorkspaceId, null, reason, now, cancellationToken),
            };

            var sameTarget = await db.Set<AbuseReport>()
                .Where(r => r.Status == ReportStatus.Open && r.WorkspaceId == report.WorkspaceId && r.Target == report.Target && r.SectionType == report.SectionType && r.TestId == report.TestId)
                .ToListAsync(cancellationToken);
            foreach (var open in sameTarget)
            {
                Resolve(open, ReportStatus.Resolved, actor, reason, now);
            }

            if (hidden)
            {
                audit.Record("moderation.content.hidden", AuditTarget(report.Target), AuditKey(report), report.WorkspaceId,
                    after: new { target = ReportNames.Of(report.Target), report.SectionType, report.TestId, reason, reportId = report.Id });
                await NotifyTeacherAsync(report.WorkspaceId, report.Target, report.SectionType, report.TestId, reason, hiddenNow: true, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // After the commit: no visitor request can cache the content again in between.
        await publicSites.EvictAllAsync(cancellationToken);
        return await GetAsync(report.Id, cancellationToken);
    }

    public async Task<AdminReportResponse> DismissAsync(ClaimsPrincipal actor, Guid reportId, ModerationDecisionRequest request, CancellationToken cancellationToken)
    {
        var report = await OpenReportAsync(reportId, cancellationToken);
        var note = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        Resolve(report, ReportStatus.Dismissed, actor, note, clock.GetUtcNow().UtcDateTime);
        audit.Record("moderation.report.dismissed", "AbuseReport", report.Id.ToString(), report.WorkspaceId, after: new { note });
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(report.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<HiddenContentResponse>> ListHiddenAsync(CancellationToken cancellationToken)
    {
        var hiddenSites = await sites.ListHiddenAsync(cancellationToken);
        var hiddenTests = await tests.ListHiddenAsync(cancellationToken);
        var slugs = new Dictionary<Guid, string?>();
        foreach (var workspaceId in hiddenTests.Select(t => t.WorkspaceId).Distinct())
        {
            slugs[workspaceId] = (await sites.FindByWorkspaceAsync(workspaceId, cancellationToken))?.Slug;
        }

        return
        [
            .. hiddenSites.Select(s => new HiddenContentResponse(s.WorkspaceId, s.SectionType is null ? "site" : "section", s.Slug, s.SectionType, null, null, s.Reason, s.HiddenAtUtc)),
            .. hiddenTests.Select(t => new HiddenContentResponse(t.WorkspaceId, "test", slugs.GetValueOrDefault(t.WorkspaceId), null, t.TestId, t.Title, t.HiddenReason!, t.HiddenAtUtc!.Value)),
        ];
    }

    /// <summary>Shows hidden content again (audited with the reason; the teacher is told).</summary>
    public async Task RestoreAsync(RestoreContentRequest request, CancellationToken cancellationToken)
    {
        var target = ReportNames.Targets[request.Target!];
        var workspaceId = request.WorkspaceId!.Value;
        var reason = request.Reason!.Trim();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var restored = target switch
            {
                ReportTarget.Test => await tests.UnhideAsync(workspaceId, request.TestId!.Value, cancellationToken),
                ReportTarget.Section => await sites.UnhideAsync(workspaceId, request.SectionType, cancellationToken),
                _ => await sites.UnhideAsync(workspaceId, null, cancellationToken),
            };
            if (!restored)
            {
                throw new NotFoundException("This content is not hidden.");
            }

            var key = target == ReportTarget.Test ? request.TestId!.Value.ToString() : target == ReportTarget.Section ? $"{workspaceId}:{request.SectionType}" : workspaceId.ToString();
            audit.Record("moderation.content.restored", AuditTarget(target), key, workspaceId,
                after: new { target = request.Target, request.SectionType, request.TestId, reason });
            await NotifyTeacherAsync(workspaceId, target, request.SectionType, request.TestId, reason, hiddenNow: false, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await publicSites.EvictAllAsync(cancellationToken);
    }

    private async Task<AbuseReport> OpenReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await db.Set<AbuseReport>().SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken) ?? throw new NotFoundException();
        return report.Status == ReportStatus.Open
            ? report
            : throw new ConflictException("The report was already handled.", ErrorCodes.InvalidStatusTransition);
    }

    private static void Resolve(AbuseReport report, ReportStatus status, ClaimsPrincipal actor, string? note, DateTime nowUtc)
    {
        report.Status = status;
        report.ResolvedAtUtc = nowUtc;
        report.ResolvedByUserId = Guid.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        report.ResolutionNote = note;
    }

    private async Task NotifyTeacherAsync(Guid workspaceId, ReportTarget target, string? sectionType, Guid? testId, string reason, bool hiddenNow, CancellationToken cancellationToken)
    {
        if (await owners.GetOwnerAsync(workspaceId, cancellationToken) is not { } owner)
        {
            return;
        }

        var testTitle = testId is { } id ? (await tests.FindAsync(workspaceId, id, cancellationToken))?.Title : null;
        outbox.Enqueue(ModerationEmails.Changed(owner.Email, owner.Locale, target, sectionType, testTitle, reason, hiddenNow, links.Value.PublicBaseUrl.TrimEnd('/') + "/app"));
    }

    private async Task<AdminReportResponse> ToResponseAsync(AbuseReport report, CancellationToken cancellationToken)
    {
        var site = await sites.FindByWorkspaceAsync(report.WorkspaceId, cancellationToken);
        var test = report.TestId is { } testId ? await tests.FindAsync(report.WorkspaceId, testId, cancellationToken) : null;
        var hidden = report.Target switch
        {
            ReportTarget.Test => test?.HiddenAtUtc is not null,
            ReportTarget.Section => site?.Moderation?.Sections.Any(s => s.Type == report.SectionType) == true,
            _ => site?.Moderation?.SiteHidden == true,
        };
        var open = await db.Set<AbuseReport>().CountAsync(
            r => r.Status == ReportStatus.Open && r.WorkspaceId == report.WorkspaceId && r.Target == report.Target && r.SectionType == report.SectionType && r.TestId == report.TestId,
            cancellationToken);
        return new AdminReportResponse(report.Id, report.CreatedAtUtc, report.Status.ToString(), ReportNames.Of(report.Target), ReportNames.Of(report.Reason),
            report.Details, report.ReporterEmail, report.WorkspaceId, site?.Slug, report.SectionType, report.TestId, test?.Title, hidden, open,
            report.ResolvedAtUtc, report.ResolvedByUserId, report.ResolutionNote);
    }

    private static string AuditTarget(ReportTarget target) => target switch
    {
        ReportTarget.Test => "DiagnosticTest",
        ReportTarget.Section => "SiteSection",
        _ => "Site",
    };

    private static string AuditKey(AbuseReport report) => report.Target switch
    {
        ReportTarget.Test => report.TestId!.Value.ToString(),
        ReportTarget.Section => $"{report.WorkspaceId}:{report.SectionType}",
        _ => report.WorkspaceId.ToString(),
    };
}

/// <summary>The teacher's notice when moderation hides or shows content again; never names the reporter.</summary>
internal static class ModerationEmails
{
    public const string HiddenTemplate = "moderation.hidden";
    public const string RestoredTemplate = "moderation.restored";

    public static EmailMessage Changed(string to, string locale, ReportTarget target, string? sectionType, string? testTitle, string reason, bool hidden, string appLink)
    {
        var en = locale == "en";
        var what = (en, target) switch
        {
            (true, ReportTarget.Test) => $"your test \"{testTitle}\"",
            (true, ReportTarget.Section) => $"the \"{sectionType}\" section of your public page",
            (true, _) => "your public page",
            (false, ReportTarget.Test) => $"вашиот тест „{testTitle}“",
            (false, ReportTarget.Section) => $"делот „{sectionType}“ од вашата јавна страница",
            (false, _) => "вашата јавна страница",
        };
        var subject = (en, hidden) switch
        {
            (true, true) => "Part of your ClassHub content was hidden",
            (true, false) => "Your ClassHub content is visible again",
            (false, true) => "Дел од вашата содржина на ClassHub е сокриен",
            (false, false) => "Вашата содржина на ClassHub повторно е видлива",
        };
        var intro = (en, hidden) switch
        {
            (true, true) => $"After a review, the ClassHub team hid {what} from visitors. Nothing was deleted: you can still see and edit it.",
            (true, false) => $"The ClassHub team made {what} visible to visitors again.",
            (false, true) => $"По проверка, тимот на ClassHub ја сокри {what} од посетителите. Ништо не е избришано: сè уште можете да ја гледате и уредувате.",
            (false, false) => $"Тимот на ClassHub повторно ја направи видлива {what} за посетителите.",
        };
        var reasonLabel = en ? "Reason" : "Причина";
        var action = en ? "Open ClassHub" : "Отворете го ClassHub";
        var html = $"<p>{WebUtility.HtmlEncode(intro)}</p><p><strong>{WebUtility.HtmlEncode(reasonLabel)}:</strong> {WebUtility.HtmlEncode(reason)}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(appLink)}\">{WebUtility.HtmlEncode(action)}</a></p>";
        var text = $"{intro}\n\n{reasonLabel}: {reason}\n\n{action}: {appLink}\n";
        return new EmailMessage(to, subject, html, text, hidden ? HiddenTemplate : RestoredTemplate);
    }
}
