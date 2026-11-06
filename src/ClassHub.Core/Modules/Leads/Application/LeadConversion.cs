using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Leads.Persistence;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Leads.Application;

/// <summary>The lead side of the student conversion (D-12): read the request, mark it converted.</summary>
internal sealed class LeadConversion(LeadQueries queries, TimeProvider clock) : ILeadConversion
{
    public async Task<ConvertibleLead?> FindAsync(Guid leadId, CancellationToken cancellationToken) =>
        await queries.WithArchived().Where(l => l.Id == leadId)
            .Select(l => new ConvertibleLead(l.Id, l.Name, l.Email, l.Phone, l.SubjectId, l.StudentId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task MarkConvertedAsync(Guid leadId, Guid studentId, CancellationToken cancellationToken)
    {
        var lead = await queries.WithArchived(tracking: true).SingleOrDefaultAsync(l => l.Id == leadId, cancellationToken) ?? throw new NotFoundException();
        lead.StudentId = studentId;
        lead.Status = LeadStatus.Converted;
        lead.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        lead.Version++;
    }
}
