using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Globalization;
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
internal sealed class WorkspaceService(ClassHubDbContext db, ITenantContext tenant, IAuditLog audit) : IWorkspaceSettings
{
    public async Task ApplyAsync(string? timeZone, string? currency, CancellationToken cancellationToken)
    {
        if (timeZone is null && currency is null)
        {
            return;
        }

        var workspace = await LoadCurrentAsync(cancellationToken);
        if (!Apply(workspace, new UpdateWorkspaceRequest(null, timeZone, currency, null), out var before, out var after))
        {
            return;
        }

        audit.Record("workspace.settings.updated", "Workspace", workspace.Id.ToString(), workspace.Id, before, after);
    }

    public async Task<WorkspaceResponse> GetAsync(CancellationToken cancellationToken)
    {
        var workspace = await LoadCurrentAsync(cancellationToken);
        return ToResponse(workspace);
    }

    public async Task<WorkspaceResponse> UpdateAsync(UpdateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var workspace = await LoadCurrentAsync(cancellationToken);
        if (Apply(workspace, request, out var before, out var after))
        {
            audit.Record("workspace.settings.updated", "Workspace", workspace.Id.ToString(), workspace.Id, before, after);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(workspace);
    }

    /// <summary>Applies the non-null values; true (with before/after for the audit) when anything changed.</summary>
    private static bool Apply(Workspace workspace, UpdateWorkspaceRequest request, out object before, out object after)
    {
        var old = new { workspace.Name, workspace.TimeZone, workspace.Currency, workspace.Locale };

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

        var updated = new { workspace.Name, workspace.TimeZone, workspace.Currency, workspace.Locale };
        before = old;
        after = updated;
        // Value equality (anonymous types compare by value with Equals, by reference with !=).
        return !old.Equals(updated);
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
            .Must(Currencies.IsActiveIsoCode).WithMessage("Currency must be an active ISO 4217 code.")
            .When(x => x.Currency is not null);
        RuleFor(x => x.Locale)
            .Must(SupportedLocales.IsSupported).WithMessage("Locale must be 'mk' or 'en'.")
            .When(x => x.Locale is not null);
    }

    internal static bool BeAnIanaTimeZone(string id) => id.Length <= Workspace.TimeZoneMaxLength && TimeZones.IsIanaId(id);
}
