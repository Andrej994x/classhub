using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Usage.Contracts;
using ClassHub.Core.Modules.Usage.Domain;
using ClassHub.Core.Modules.Usage.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClassHub.Core.Modules.Usage;

/// <summary>
/// Usage analytics (R3-P6): daily counters of tools opened and activities created and reused, per workspace. No user,
/// student or visitor is recorded, only how often per day. Teachers see their workspace's numbers; platform staff see
/// platform-wide totals only (never a workspace's own numbers).
/// </summary>
public sealed class UsageModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<UsageQueries>();
        services.AddScoped<IUsageRecorder, UsageRecorder>();
        services.AddScoped<UsageService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(WorkspacesModule.WorkspaceRoute).WithTags("Usage").RequireAntiforgeryHeader()
            .MapGet("/usage", async (int? days, UsageService service, CancellationToken ct) => TypedResults.Ok(await service.WorkspaceAsync(days ?? 30, ct)))
            .RequireAuthorization(WorkspacePolicies.Read)
            .WithName("GetWorkspaceUsage")
            .WithSummary("How often this workspace opened each tool and created or reused activities on each board in the last 7, 30 or 90 days.");

        endpoints.MapGroup("/admin/usage").WithTags("Admin").RequireAntiforgeryHeader().RequireAuthorization(ClassHubPolicies.PlatformStaff)
            .MapGet("", async (int? days, UsageService service, CancellationToken ct) => TypedResults.Ok(await service.PlatformAsync(days ?? 30, ct)))
            .WithName("GetPlatformUsage")
            .WithSummary("Platform-wide totals per tool and board and per day, and how many workspaces used the toolbox (no workspace's own numbers).");
    }
}

/// <summary>Counts a use in the current workspace (best effort: a failed count never fails what the teacher did).</summary>
internal sealed partial class UsageRecorder(UsageQueries queries, ITenantContext tenant, TimeProvider clock, ILogger<UsageRecorder> logger) : IUsageRecorder
{
    public async Task RecordAsync(UsageEvent usage, string subject, CancellationToken cancellationToken)
    {
        if (subject.Length is 0 or > UsageCounter.SubjectMaxLength || tenant.WorkspaceId is not { } workspaceId)
        {
            return;
        }

        try
        {
            await queries.IncrementAsync(workspaceId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), usage, subject, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotCounted(logger, usage, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A {Usage} use could not be counted.")]
    private static partial void LogNotCounted(ILogger logger, UsageEvent usage, Exception exception);
}

internal sealed class UsageService(ClassHubDbContext db, UsageQueries queries, TimeProvider clock)
{
    public static readonly IReadOnlySet<int> Periods = new HashSet<int> { 7, 30, 90 };

    public async Task<WorkspaceUsageResponse> WorkspaceAsync(int days, CancellationToken cancellationToken)
    {
        var (from, to) = Period(days);
        // The tenant filter keeps this to the workspace's own counters.
        var rows = await db.Set<UsageCounter>().AsNoTracking().Where(c => c.Day >= from && c.Day <= to)
            .GroupBy(c => new { c.Event, c.Subject }).Select(g => new UsageTotal(g.Key.Event, g.Key.Subject, g.Sum(c => c.Count)))
            .ToListAsync(cancellationToken);
        return new WorkspaceUsageResponse(days, from, to, Tools(rows), Boards(rows));
    }

    public async Task<PlatformUsageResponse> PlatformAsync(int days, CancellationToken cancellationToken)
    {
        var (from, to) = Period(days);
        var totals = await queries.PlatformTotalsAsync(from, to, cancellationToken);
        var daily = await queries.PlatformDailyAsync(from, to, cancellationToken);
        var workspaces = await queries.WorkspacesUsingAsync(from, to, cancellationToken);
        var perDay = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(day => new UsageDay(
            day,
            daily.Where(d => d.Day == day && d.Event == UsageEvent.ToolOpened).Sum(d => d.Count),
            daily.Where(d => d.Day == day && d.Event == UsageEvent.ActivityCreated).Sum(d => d.Count),
            daily.Where(d => d.Day == day && d.Event == UsageEvent.ActivityReused).Sum(d => d.Count)));
        return new PlatformUsageResponse(days, from, to, workspaces, Tools(totals), Boards(totals), [.. perDay]);
    }

    private (DateOnly From, DateOnly To) Period(int days)
    {
        if (!Periods.Contains(days))
        {
            throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["days"] = ["Choose 7, 30 or 90 days."] });
        }

        var to = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return (to.AddDays(-(days - 1)), to);
    }

    private static List<ToolUsage> Tools(IEnumerable<UsageTotal> rows) =>
        [.. rows.Where(r => r.Event == UsageEvent.ToolOpened).OrderByDescending(r => r.Count).ThenBy(r => r.Subject, StringComparer.Ordinal)
            .Select(r => new ToolUsage(r.Subject, r.Count))];

    private static List<BoardUsage> Boards(IReadOnlyCollection<UsageTotal> rows) =>
        [.. rows.Where(r => r.Event is UsageEvent.ActivityCreated or UsageEvent.ActivityReused).Select(r => r.Subject).Distinct().Order(StringComparer.Ordinal)
            .Select(board => new BoardUsage(
                board,
                rows.Where(r => r.Subject == board && r.Event == UsageEvent.ActivityCreated).Sum(r => r.Count),
                rows.Where(r => r.Subject == board && r.Event == UsageEvent.ActivityReused).Sum(r => r.Count)))];
}
