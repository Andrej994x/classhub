using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Finance.Contracts;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Dashboard;

/// <summary>
/// The teacher's dashboard (M13, R1-P8): the workspace's own numbers only, gathered from the other modules through their
/// contracts (all tenant-filtered). Periods are whole UTC days ending today, like the page-view counter (D-32).
/// </summary>
public sealed class DashboardModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<DashboardService>();
        services.AddScoped<TeachingDashboardService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var dashboard = endpoints.MapGroup(WorkspacesModule.WorkspaceRoute).WithTags("Dashboard").RequireAntiforgeryHeader();
        dashboard.MapGet("/dashboard", async (int? days, DashboardService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(days ?? 30, ct)))
            .RequireAuthorization(WorkspacePolicies.Read)
            .WithName("GetDashboard")
            .WithSummary("Leads, tests, page visits, offers, onboarding and the conversion funnel for the last 7, 30 or 90 days.");

        // R2-P12: lessons, students and money, each under its own read permission.
        dashboard.MapGet("/dashboard/teaching", async (TeachingDashboardService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .RequireAuthorization(
                WorkspacePolicies.For(WorkspacePermissions.LessonsRead),
                WorkspacePolicies.For(WorkspacePermissions.StudentsRead),
                WorkspacePolicies.For(WorkspacePermissions.FinanceRead))
            .WithName("GetTeachingDashboard")
            .WithSummary("Lessons of the next 7 days, active students, what students owe, lessons left in packages and payments received per month.");
    }
}

/// <summary>A lesson ahead, with its students' names (only this workspace's students).</summary>
public sealed record DashboardLesson(Guid Id, string Title, DateTime StartUtc, DateTime EndUtc, string TimeZone, string Kind, IReadOnlyList<string> Students);

/// <summary>The scheduled lessons not over yet that start within the next 7 days: how many, and the first five.</summary>
public sealed record DashboardLessons(int Next7Days, IReadOnlyList<DashboardLesson> Next);

/// <summary>The teacher's teaching numbers (R2-P12). Money is the teacher's own (recorded payments), per currency.</summary>
public sealed record TeachingDashboardResponse(DashboardLessons Lessons, int ActiveStudents, FinanceSnapshotResponse Finance);

internal sealed class TeachingDashboardService(ILessonOutlook lessons, IStudentLookup students, IFinanceStats finance, TimeProvider clock)
{
    public const int NextLessons = 5;
    public const int IncomeMonths = 6;

    public async Task<TeachingDashboardResponse> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ahead = await lessons.AheadAsync(now, now.AddDays(7), NextLessons, cancellationToken);
        var names = await students.FindAsync(ahead.First.SelectMany(l => l.StudentIds).ToHashSet(), cancellationToken);
        var next = ahead.First.Select(l => new DashboardLesson(l.Id, l.Title, l.StartUtc, l.EndUtc, l.TimeZone, l.Kind.ToString(),
            [.. l.StudentIds.Where(names.ContainsKey).Select(id => names[id]).Select(s => string.Join(' ', new[] { s.FirstName, s.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))))
                .Order(StringComparer.CurrentCulture)]));
        return new TeachingDashboardResponse(
            new DashboardLessons(ahead.Count, [.. next]),
            await students.CountActiveAsync(cancellationToken),
            await finance.SnapshotAsync(IncomeMonths, cancellationToken));
    }
}

public sealed record DashboardResponse(
    int Days,
    DateOnly From,
    DateOnly To,
    DashboardLeads Leads,
    DashboardTests Tests,
    DashboardSite Site,
    DashboardOffers Offers,
    DashboardOnboarding Onboarding,
    DashboardFunnel Funnel);

public sealed record CountByKey(string Key, int Count);

/// <summary><c>New</c> and <c>ByStatus</c>: now (not archived). <c>InPeriod</c> and <c>ByTypeInPeriod</c>: arrived in the period.</summary>
public sealed record DashboardLeads(int New, int InPeriod, IReadOnlyList<CountByKey> ByStatus, IReadOnlyList<CountByKey> ByTypeInPeriod);

public sealed record DashboardTests(int Published, int AttemptsInPeriod, int SharedResultsInPeriod);

/// <summary>Page visits (D-32): one entry per day of the period, zero included.</summary>
public sealed record DashboardSite(bool Exists, bool Published, int VisitsInPeriod, IReadOnlyList<DailyCount> Daily);

public sealed record DailyCount(DateOnly Day, int Count);

public sealed record DashboardOffers(int Active, int Public);

public sealed record DashboardOnboarding(int Completed, int Total);

/// <summary>
/// The conversion funnel of the period: page visits → leads that arrived → of those, contacted (Contacted or Converted)
/// → converted (status Converted; the conversion into a student record comes with R2-P1).
/// </summary>
public sealed record DashboardFunnel(int Visits, int Leads, int Contacted, int Converted);

internal sealed class DashboardService(
    ILeadStats leads,
    ITestStats tests,
    ISiteStatus site,
    ISitePageViewStats views,
    IOfferLookup offers,
    IOnboardingProgress onboarding,
    TimeProvider clock)
{
    public static readonly IReadOnlySet<int> Periods = new HashSet<int> { 7, 30, 90 };

    public async Task<DashboardResponse> GetAsync(int days, CancellationToken cancellationToken)
    {
        if (!Periods.Contains(days))
        {
            throw new RequestValidationException(new Dictionary<string, string[]> { ["days"] = ["Choose 7, 30 or 90 days."] });
        }

        var to = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = to.AddDays(-(days - 1));
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var leadStats = await leads.GetAsync(fromUtc, cancellationToken);
        var testStats = await tests.GetAsync(fromUtc, cancellationToken);
        var (exists, published) = await site.GetStatusAsync(cancellationToken);
        var perDay = await views.DailyAsync(from, to, cancellationToken);
        var daily = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(d => new DailyCount(d, perDay.GetValueOrDefault(d))).ToList();
        var (activeOffers, publicOffers) = await offers.CountActiveAsync(cancellationToken);
        var checklist = await onboarding.GetOnboardingAsync(cancellationToken);
        var visits = daily.Sum(d => d.Count);

        return new DashboardResponse(
            days,
            from,
            to,
            new DashboardLeads(
                leadStats.New,
                leadStats.InPeriod,
                [.. leadStats.ByStatus.Select(s => new CountByKey(s.Key, s.Value))],
                [.. leadStats.ByTypeInPeriod.Select(t => new CountByKey(t.Key, t.Value))]),
            new DashboardTests(testStats.Published, testStats.AttemptsInPeriod, testStats.SharedResultsInPeriod),
            new DashboardSite(exists, published, visits, daily),
            new DashboardOffers(activeOffers, publicOffers),
            new DashboardOnboarding(checklist.Completed, checklist.Total),
            new DashboardFunnel(visits, leadStats.InPeriod, leadStats.ContactedInPeriod, leadStats.ConvertedInPeriod));
    }
}
