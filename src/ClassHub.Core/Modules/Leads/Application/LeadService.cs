using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Leads.Persistence;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Leads.Application;

/// <summary>The lead inbox of the current (verified) workspace: list, detail, status, notes, archive. Tenant-filtered.</summary>
internal sealed class LeadService(ClassHubDbContext db, LeadQueries queries, ITenantContext tenant, TimeProvider clock)
{
    public const int MaxPageSize = 50;
    public const int PreviewLength = 140;

    public async Task<LeadListResponse> ListAsync(string? status, string? type, string? q, bool archived, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var leads = queries.WithArchived().Where(l => archived ? l.ArchivedAtUtc != null : l.ArchivedAtUtc == null);
        if (status is not null)
        {
            var parsed = Parse<LeadStatus>(status, "status");
            leads = leads.Where(l => l.Status == parsed);
        }

        if (type is not null)
        {
            var parsed = Parse<LeadType>(type, "type");
            leads = leads.Where(l => l.Type == parsed);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (term.Length > 100)
            {
                throw Invalid("q", "At most 100 characters.");
            }

            leads = leads.Where(l => l.Name.Contains(term) || l.Email.Contains(term) || (l.Message != null && l.Message.Contains(term)));
        }

        var total = await leads.CountAsync(cancellationToken);
        var items = await leads.OrderByDescending(l => l.CreatedAtUtc).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LeadListItem(
                l.Id, l.Type.ToString(), l.Status.ToString(), l.Name, l.Email, l.OfferTitle, l.SubjectId,
                l.Message == null ? null : l.Message.Substring(0, l.Message.Length > PreviewLength ? PreviewLength : l.Message.Length),
                l.CreatedAtUtc, l.ArchivedAtUtc != null))
            .ToListAsync(cancellationToken);
        var newCount = await db.Set<Lead>().CountAsync(l => l.Status == LeadStatus.New, cancellationToken);
        return new LeadListResponse(items, total, newCount, page, pageSize);
    }

    public async Task<LeadResponse> GetAsync(Guid leadId, CancellationToken cancellationToken) =>
        await ToResponseAsync(await LoadAsync(leadId, tracking: false, cancellationToken), cancellationToken);

    public async Task<LeadResponse> ChangeStatusAsync(Guid leadId, ChangeLeadStatusRequest request, CancellationToken cancellationToken)
    {
        var lead = await LoadForChangeAsync(leadId, request.Version, cancellationToken);
        var target = Enum.Parse<LeadStatus>(request.Status!);
        if (target != lead.Status)
        {
            if (!LeadStatusRules.CanChange(lead.Status, target))
            {
                throw new BusinessRuleException($"A lead cannot go from {lead.Status} to {target}.", ErrorCodes.InvalidStatusTransition);
            }

            lead.Status = target;
            Touch(lead);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ToResponseAsync(lead, cancellationToken);
    }

    public async Task<LeadNoteResponse> AddNoteAsync(Guid leadId, AddLeadNoteRequest request, CancellationToken cancellationToken)
    {
        var lead = await LoadAsync(leadId, tracking: false, cancellationToken);
        var note = new LeadNote
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Text = request.Text!.Trim(),
            AuthorUserId = tenant.UserId,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Set<LeadNote>().Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return new LeadNoteResponse(note.Id, note.Text, note.CreatedAtUtc);
    }

    public async Task<LeadResponse> SetArchivedAsync(Guid leadId, LeadVersionRequest request, bool archived, CancellationToken cancellationToken)
    {
        var lead = await LoadForChangeAsync(leadId, request.Version, cancellationToken);
        if ((lead.ArchivedAtUtc is not null) != archived)
        {
            lead.ArchivedAtUtc = archived ? clock.GetUtcNow().UtcDateTime : null;
            Touch(lead);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ToResponseAsync(lead, cancellationToken);
    }

    private void Touch(Lead lead)
    {
        lead.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        lead.Version++;
    }

    private async Task<Lead> LoadForChangeAsync(Guid leadId, int? version, CancellationToken cancellationToken)
    {
        var lead = await LoadAsync(leadId, tracking: true, cancellationToken);
        if (version != lead.Version)
        {
            throw new ConflictException("The request was changed elsewhere. Reload it and try again.", ErrorCodes.ConcurrencyConflict);
        }

        return lead;
    }

    private async Task<Lead> LoadAsync(Guid leadId, bool tracking, CancellationToken cancellationToken) =>
        await queries.WithArchived(tracking).SingleOrDefaultAsync(l => l.Id == leadId, cancellationToken) ?? throw new NotFoundException();

    private async Task<LeadResponse> ToResponseAsync(Lead lead, CancellationToken cancellationToken)
    {
        var notes = await db.Set<LeadNote>().AsNoTracking().Where(n => n.LeadId == lead.Id).OrderBy(n => n.CreatedAtUtc)
            .Select(n => new LeadNoteResponse(n.Id, n.Text, n.CreatedAtUtc)).ToListAsync(cancellationToken);
        return new LeadResponse(
            lead.Id, lead.Type.ToString(), lead.Status.ToString(), lead.Name, lead.Email, lead.Phone, lead.Message, lead.OfferId, lead.OfferTitle,
            lead.SubjectId, lead.ConsentVersion, lead.ConsentedAtUtc, lead.CreatedAtUtc, lead.UpdatedAtUtc, lead.ArchivedAtUtc is not null, notes, lead.Version);
    }

    private static T Parse<T>(string value, string field)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw Invalid(field, $"Unknown {field}.");

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

internal sealed class SubmitLeadRequestValidator : AbstractValidator<SubmitLeadRequest>
{
    public SubmitLeadRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t is nameof(LeadType.Message) or nameof(LeadType.LessonRequest)).WithMessage("Type must be Message or LessonRequest.");
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop).NotEmpty().Must(n => n!.Trim().Length > 0).MaximumLength(Lead.NameMaxLength);
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(Lead.EmailMaxLength).EmailAddress();
        RuleFor(x => x.Phone).MaximumLength(Lead.PhoneMaxLength).Matches(@"\A[0-9+()\-./ ]{5,40}\z").When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Use digits, spaces and + ( ) - only.");
        RuleFor(x => x.Message).MaximumLength(Lead.MessageMaxLength);
        RuleFor(x => x.Message).Must(m => !string.IsNullOrWhiteSpace(m)).When(x => x.Type == nameof(LeadType.Message)).WithMessage("Write a message.");
        RuleFor(x => x.Consent).Equal(true).WithMessage("Consent is required.");
        RuleFor(x => x.AgeConfirmed).Equal(true).WithMessage("Confirm your age or that you are a parent or guardian.");
        RuleFor(x => x.ConsentVersion).Equal(LeadConsent.Version).WithMessage("The consent text has changed. Reload the page.");
        RuleFor(x => x.FormToken).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Website).MaximumLength(500);
    }
}

internal sealed class ChangeLeadStatusRequestValidator : AbstractValidator<ChangeLeadStatusRequest>
{
    public ChangeLeadStatusRequestValidator()
    {
        RuleFor(x => x.Status).Must(s => Enum.TryParse<LeadStatus>(s, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage("Status must be New, Contacted, Converted or Closed.");
        RuleFor(x => x.Version).NotNull();
    }
}

internal sealed class AddLeadNoteRequestValidator : AbstractValidator<AddLeadNoteRequest>
{
    public AddLeadNoteRequestValidator()
    {
        RuleFor(x => x.Text).Cascade(CascadeMode.Stop).NotEmpty().Must(t => t!.Trim().Length > 0).MaximumLength(LeadNote.TextMaxLength);
    }
}

internal sealed class LeadVersionRequestValidator : AbstractValidator<LeadVersionRequest>
{
    public LeadVersionRequestValidator() => RuleFor(x => x.Version).NotNull();
}
