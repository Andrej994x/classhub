using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Leads.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Leads.Application;

/// <summary>
/// Dashboard numbers (tenant-filtered). "Contacted" in the funnel means the lead reached Contacted or Converted;
/// "converted" means status Converted (the conversion into a student record comes with R2-P1).
/// </summary>
internal sealed class LeadStatsService(LeadQueries queries) : ILeadStats
{
    public async Task<LeadStats> GetAsync(DateTime fromUtc, CancellationToken cancellationToken)
    {
        var current = await queries.WithArchived().Where(l => l.ArchivedAtUtc == null)
            .GroupBy(l => l.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var period = await queries.WithArchived().Where(l => l.CreatedAtUtc >= fromUtc)
            .GroupBy(l => new { l.Type, l.Status }).Select(g => new { g.Key.Type, g.Key.Status, Count = g.Count() }).ToListAsync(cancellationToken);

        var byStatus = Enum.GetValues<LeadStatus>().ToDictionary(s => s.ToString(), s => current.Where(c => c.Key == s).Sum(c => c.Count));
        var byType = Enum.GetValues<LeadType>().ToDictionary(t => t.ToString(), t => period.Where(p => p.Type == t).Sum(p => p.Count));
        return new LeadStats(
            byStatus[nameof(LeadStatus.New)],
            byStatus,
            period.Sum(p => p.Count),
            byType,
            period.Where(p => p.Status is LeadStatus.Contacted or LeadStatus.Converted).Sum(p => p.Count),
            period.Where(p => p.Status == LeadStatus.Converted).Sum(p => p.Count));
    }
}
