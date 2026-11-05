using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Leads.Persistence;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Leads.Application;

/// <summary>
/// Anonymous lead intake from a public site. The slug resolves to a published site of a visible owner; the request is
/// then bound to that workspace <b>read-only</b> for every check (offers, subjects, limits). The lead itself is written
/// in a separate scope with the workspace set explicitly, so the public request never gets write access to tenant
/// data in general. Nothing the visitor sent is echoed back.
/// </summary>
internal sealed partial class LeadIntake(
    IPublicSites sites,
    TenantContext tenant,
    IWorkspaceOwnerContact owners,
    IOfferLookup offers,
    ITeacherPublicProfile profiles,
    ICatalogLookup catalog,
    LeadFormTokens tokens,
    IServiceScopeFactory scopes,
    IOptions<LeadOptions> options,
    IOptions<AppLinkOptions> links,
    TimeProvider clock,
    ILogger<LeadIntake> logger)
{
    public async Task<LeadFormResponse> GetFormAsync(string slug, CancellationToken cancellationToken)
    {
        var site = await ResolveAsync(slug, cancellationToken);
        var language = site.Language;
        var consentText = LeadConsent.Text(language);
        var minorNotice = LeadConsent.MinorNotice(language, options.Value.MinorAgeThreshold);
        // D-10b: an unconfirmed teacher does not receive public leads; the form is hidden.
        if (await owners.GetOwnerAsync(site.WorkspaceId, cancellationToken) is not { EmailConfirmed: true })
        {
            return new LeadFormResponse(false, null, [], [], LeadConsent.Version, consentText, minorNotice, options.Value.MinorAgeThreshold);
        }

        var publicOffers = await offers.ListPublicAsync(cancellationToken);
        var subjects = await catalog.SubjectsAsync((await profiles.GetPublicProfileAsync(cancellationToken)).SubjectIds, cancellationToken);
        return new LeadFormResponse(
            true,
            tokens.Issue(slug),
            [.. publicOffers.Select(o => new LeadFormOption(o.Id, o.Title))],
            [.. subjects.Select(s => new LeadFormOption(s.Id, language == "en" ? s.NameEn : s.NameMk))],
            LeadConsent.Version,
            consentText,
            minorNotice,
            options.Value.MinorAgeThreshold);
    }

    public async Task SubmitAsync(string slug, SubmitLeadRequest request, CancellationToken cancellationToken)
    {
        var site = await ResolveAsync(slug, cancellationToken);
        var now = clock.GetUtcNow();
        if (tokens.Validate(request.FormToken, slug) is not { } loadedAt)
        {
            throw new BusinessRuleException("The form has expired. Reload the page and send it again.", ErrorCodes.FormExpired);
        }

        // Honeypot filled or sent faster than a person can type: accepted on the surface, silently dropped (D-19).
        if (!string.IsNullOrEmpty(request.Website) || now - loadedAt < TimeSpan.FromSeconds(options.Value.MinimumFillSeconds))
        {
            LogDropped(logger, slug);
            return;
        }

        var owner = await owners.GetOwnerAsync(site.WorkspaceId, cancellationToken);
        if (owner is not { EmailConfirmed: true })
        {
            throw new BusinessRuleException("This teacher cannot receive requests at the moment.", ErrorCodes.LeadsUnavailable);
        }

        // Only public, active, non-archived offers of this teacher can be requested.
        string? offerTitle = null;
        if (request.OfferId is { } offerId)
        {
            offerTitle = (await offers.ListPublicAsync(cancellationToken)).FirstOrDefault(o => o.Id == offerId)?.Title
                ?? throw Invalid("offerId", "This offer cannot be requested.");
        }

        if (request.SubjectId is { } subjectId && !(await profiles.GetPublicProfileAsync(cancellationToken)).SubjectIds.Contains(subjectId))
        {
            throw Invalid("subjectId", "Choose one of the teacher's subjects.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        var intake = scope.ServiceProvider.GetRequiredService<LeadQueries>();
        // Quota check and insert under one per-workspace lock: parallel submissions cannot exceed the limit.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await intake.LockIntakeAsync(site.WorkspaceId, cancellationToken);
        if (await intake.CountCreatedSinceAsync(site.WorkspaceId, now.UtcDateTime.AddHours(-1), cancellationToken) >= options.Value.MaxPerSitePerHour)
        {
            throw new TooManyRequestsException("This teacher has received many requests in the last hour. Please try again later.");
        }

        var type = Enum.Parse<LeadType>(request.Type!);
        db.Set<Lead>().Add(new Lead
        {
            Id = Guid.NewGuid(),
            WorkspaceId = site.WorkspaceId,
            Type = type,
            Status = LeadStatus.New,
            Name = request.Name!.Trim(),
            Email = request.Email!.Trim(),
            Phone = Clean(request.Phone),
            Message = Clean(request.Message),
            OfferId = request.OfferId,
            OfferTitle = offerTitle,
            SubjectId = request.SubjectId,
            ConsentVersion = request.ConsentVersion!,
            ConsentedAtUtc = now.UtcDateTime,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime,
        });
        var inbox = $"{links.Value.PublicBaseUrl.TrimEnd('/')}/app/w/{site.WorkspaceId}/leads";
        scope.ServiceProvider.GetRequiredService<IEmailOutbox>().Enqueue(LeadEmails.NewLead(owner.Email, owner.Locale, inbox, type == LeadType.LessonRequest));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<PublishedSiteRef> ResolveAsync(string slug, CancellationToken cancellationToken)
    {
        var site = await sites.FindPublishedWorkspaceAsync(slug, cancellationToken) ?? throw new NotFoundException();
        tenant.SetPublicReadOnly(site.WorkspaceId);
        return site;
    }

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "A public request for site {Slug} was dropped by the spam rules.")]
    private static partial void LogDropped(ILogger logger, string slug);
}
