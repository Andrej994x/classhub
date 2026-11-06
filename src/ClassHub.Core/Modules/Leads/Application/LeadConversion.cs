using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Leads.Persistence;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Leads.Application;

/// <summary>The lead side of the student conversion (D-12): read the request, mark it converted.</summary>
internal sealed class LeadConversion(LeadQueries queries, TimeProvider clock) : ILeadConversion
{
    public async Task<ConvertibleLead?> FindAsync(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await queries.WithArchived().Where(l => l.Id == leadId)
            .Select(l => new { l.Id, l.Name, l.Email, l.Phone, l.SubjectId, l.StudentId, l.Status })
            .SingleOrDefaultAsync(cancellationToken);
        return lead is null
            ? null
            : new ConvertibleLead(lead.Id, lead.Name, lead.Email, lead.Phone, lead.SubjectId, lead.StudentId,
                lead.Status == LeadStatus.Converted || LeadStatusRules.CanChange(lead.Status, LeadStatus.Converted));
    }

    public async Task MarkConvertedAsync(Guid leadId, Guid studentId, CancellationToken cancellationToken)
    {
        var lead = await queries.WithArchived(tracking: true).SingleOrDefaultAsync(l => l.Id == leadId, cancellationToken) ?? throw new NotFoundException();
        if (lead.Status != LeadStatus.Converted && !LeadStatusRules.CanChange(lead.Status, LeadStatus.Converted))
        {
            throw new BusinessRuleException("Reopen the request before turning it into a student.", ErrorCodes.InvalidStatusTransition);
        }

        lead.StudentId = studentId;
        lead.Status = LeadStatus.Converted;
        lead.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        lead.Version++;
    }
}
