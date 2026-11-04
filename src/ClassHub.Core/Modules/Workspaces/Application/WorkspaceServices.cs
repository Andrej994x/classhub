using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Modules.Workspaces.Application;

internal sealed class WorkspaceProvisioning(ClassHubDbContext db, IAuditLog audit, TimeProvider clock) : IWorkspaceProvisioning
{
    public Task<Guid> ProvisionForNewTeacherAsync(Guid userId, string locale, string timeZone, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var language = SupportedLocales.OrDefault(locale);
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = language == SupportedLocales.English ? "My workspace" : "Мој работен простор",
            TimeZone = timeZone,
            Currency = Workspace.DefaultCurrency,
            Locale = language,
            CreatedAtUtc = now,
        };

        db.Set<Workspace>().Add(workspace);
        db.Set<WorkspaceMember>().Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRole.Owner,
            CreatedAtUtc = now,
        });
        audit.Record("workspace.created", "Workspace", workspace.Id.ToString(), workspace.Id, actorUserId: userId);
        return Task.FromResult(workspace.Id);
    }
}

/// <summary>Workspace settings of the current (verified) workspace.</summary>
internal sealed class WorkspaceService(ClassHubDbContext db, ITenantContext tenant, IAuditLog audit)
{
    public async Task<WorkspaceResponse> GetAsync(CancellationToken cancellationToken)
    {
        var workspace = await LoadCurrentAsync(cancellationToken);
        return ToResponse(workspace);
    }

    public async Task<WorkspaceResponse> UpdateAsync(UpdateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var workspace = await LoadCurrentAsync(cancellationToken);
        var before = new { workspace.Name, workspace.TimeZone, workspace.Currency, workspace.Locale };

        if (request.Name is not null)
        {
            workspace.Name = request.Name.Trim();
        }

        if (request.TimeZone is not null)
        {
            workspace.TimeZone = request.TimeZone;
        }

        if (request.Currency is not null)
        {
            workspace.Currency = request.Currency.ToUpperInvariant();
        }

        if (request.Locale is not null)
        {
            workspace.Locale = request.Locale;
        }

        var after = new { workspace.Name, workspace.TimeZone, workspace.Currency, workspace.Locale };
        if (before != after)
        {
            audit.Record("workspace.settings.updated", "Workspace", workspace.Id.ToString(), workspace.Id, before, after);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(workspace);
    }

    private async Task<Workspace> LoadCurrentAsync(CancellationToken cancellationToken)
    {
        // The workspace row itself is the tenant; it is loaded only by the verified tenant id.
        var id = tenant.RequireWorkspaceId();
        return await db.Set<Workspace>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException();
    }

    private WorkspaceResponse ToResponse(Workspace w) =>
        new(w.Id, w.Name, w.TimeZone, w.Currency, w.Locale, (tenant.Role ?? WorkspaceRole.Owner).ToString(), w.CreatedAtUtc);
}

internal sealed class UpdateWorkspaceRequestValidator : AbstractValidator<UpdateWorkspaceRequest>
{
    public UpdateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(Workspace.NameMaxLength).When(x => x.Name is not null);
        RuleFor(x => x.TimeZone!)
            .Must(BeAnIanaTimeZone).WithMessage("Unknown time zone.")
            .When(x => x.TimeZone is not null);
        RuleFor(x => x.Currency!)
            .Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO 4217 code.")
            .When(x => x.Currency is not null);
        RuleFor(x => x.Locale)
            .Must(SupportedLocales.IsSupported).WithMessage("Locale must be 'mk' or 'en'.")
            .When(x => x.Locale is not null);
    }

    internal static bool BeAnIanaTimeZone(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= Workspace.TimeZoneMaxLength && id.Contains('/', StringComparison.Ordinal) &&
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
