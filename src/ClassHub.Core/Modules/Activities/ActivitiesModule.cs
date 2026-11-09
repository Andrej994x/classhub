using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Activities.Contracts;
using ClassHub.Core.Modules.Activities.Domain;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Subscriptions.Contracts;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.Core.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Activities;

public sealed class ActivityOptions
{
    public const string SectionName = "Activities";

    /// <summary>The largest document a board may save (UTF-8 bytes of its JSON).</summary>
    public int MaxDocumentBytes { get; set; } = 2 * 1024 * 1024;
}

/// <summary>
/// The Activity Library (M09, R3-P2): the teacher's own saved board activities. List and search, open, save and change
/// (versions), duplicate and delete (soft). New activities count toward the plan's activity limit.
/// </summary>
public sealed class ActivitiesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ActivityOptions>(configuration.GetSection(ActivityOptions.SectionName));
        services.AddScoped<ActivityService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var activities = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/activities").WithTags("Activities").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.ActivitiesRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.ActivitiesManage);

        activities.MapGet("", async (string? q, Guid? subjectId, string? toolType, int? page, int? pageSize, ActivityService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(q, subjectId, toolType, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(read)
            .WithName("ListActivities")
            .WithSummary("The workspace's activities (without documents), most recently changed first; search by title, filter by subject and board.");
        activities.MapGet("/{activityId:guid}", async (Guid activityId, ActivityService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(activityId, ct)))
            .RequireAuthorization(read)
            .WithName("GetActivity");
        activities.MapPost("", async (SaveActivityRequest request, ActivityService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created($"activities/{created.Id}", created);
            })
            .RequireAuthorization(manage)
            .WithValidation<SaveActivityRequest>()
            .WithName("CreateActivity")
            .WithSummary("Save a new activity (counts toward the plan's activities).");
        activities.MapPut("/{activityId:guid}", async (Guid activityId, SaveActivityRequest request, ActivityService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(activityId, request, ct)))
            .RequireAuthorization(manage)
            .WithValidation<SaveActivityRequest>()
            .WithName("UpdateActivity");
        activities.MapPost("/{activityId:guid}/duplicate", async (Guid activityId, ActivityService service, CancellationToken ct) =>
            {
                var copy = await service.DuplicateAsync(activityId, ct);
                return TypedResults.Created($"activities/{copy.Id}", copy);
            })
            .RequireAuthorization(manage)
            .WithName("DuplicateActivity")
            .WithSummary("A copy of the activity (counts toward the plan's activities).");
        activities.MapPost("/{activityId:guid}/delete", async (Guid activityId, ActivityVersionRequest request, ActivityService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(activityId, request, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(manage)
            .WithName("DeleteActivity")
            .WithSummary("Delete the activity (soft: it disappears from the library and no longer counts toward the plan).");
    }
}

internal sealed class ActivityService(
    ClassHubDbContext db,
    ITenantContext tenant,
    IEntitlementService entitlements,
    ICatalogLookup catalog,
    IWorkspaceSettings workspace,
    IOptions<ActivityOptions> options,
    TimeProvider clock)
{
    public async Task<ActivityListResponse> ListAsync(string? q, Guid? subjectId, string? toolType, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 10_000 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("The page must be 1 to 10000 and the page size 1 to 100.");
        }

        var activities = db.Set<Activity>().AsNoTracking();
        if (q?.Trim() is { Length: > 0 } term)
        {
            if (term.Length > 100)
            {
                throw Invalid("q", "Search for at most 100 characters.");
            }

            activities = activities.Where(a => a.Title.Contains(term));
        }

        if (subjectId is { } subject)
        {
            activities = activities.Where(a => a.SubjectId == subject);
        }

        if (toolType is not null)
        {
            if (!ActivityRules.ToolTypes.Contains(toolType))
            {
                throw Invalid("toolType", "Unknown board.");
            }

            activities = activities.Where(a => a.ToolType == toolType);
        }

        var total = await activities.CountAsync(cancellationToken);
        var items = await activities.OrderByDescending(a => a.UpdatedAtUtc).ThenBy(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new ActivityListItem(a.Id, a.Title, a.SubjectId, a.ToolType, a.SchemaVersion, a.SizeBytes, a.UpdatedAtUtc, a.Version))
            .ToListAsync(cancellationToken);
        return new ActivityListResponse(items, total, page, pageSize);
    }

    public async Task<ActivityResponse> GetAsync(Guid activityId, CancellationToken cancellationToken) =>
        ToResponse(await db.Set<Activity>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == activityId, cancellationToken) ?? throw new NotFoundException());

    public async Task<ActivityResponse> CreateAsync(SaveActivityRequest request, CancellationToken cancellationToken)
    {
        var (json, size) = Document(request);
        await ThrowIfUnknownSubjectAsync(request.SubjectId, null, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var activity = new Activity
        {
            Id = Guid.CreateVersion7(),
            Title = request.Title!.Trim(),
            SubjectId = request.SubjectId,
            ToolType = request.ToolType!,
            SchemaVersion = request.SchemaVersion!.Value,
            DocumentJson = json,
            SizeBytes = size,
            CreatedByUserId = tenant.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1,
        };
        await AddWithinLimitAsync(activity, cancellationToken);
        return ToResponse(activity);
    }

    public async Task<ActivityResponse> UpdateAsync(Guid activityId, SaveActivityRequest request, CancellationToken cancellationToken)
    {
        var activity = await LoadForChangeAsync(activityId, request.Version, cancellationToken);
        if (request.ToolType != activity.ToolType)
        {
            throw Invalid("toolType", "An activity stays on its board.");
        }

        var (json, size) = Document(request);
        await ThrowIfUnknownSubjectAsync(request.SubjectId, activity.SubjectId, cancellationToken);
        activity.Title = request.Title!.Trim();
        activity.SubjectId = request.SubjectId;
        activity.SchemaVersion = request.SchemaVersion!.Value;
        activity.DocumentJson = json;
        activity.SizeBytes = size;
        activity.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        activity.Version++;
        await SaveAsync(cancellationToken);
        return ToResponse(activity);
    }

    public async Task<ActivityResponse> DuplicateAsync(Guid activityId, CancellationToken cancellationToken)
    {
        var source = await db.Set<Activity>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == activityId, cancellationToken) ?? throw new NotFoundException();
        var suffix = await workspace.GetLocaleAsync(cancellationToken) == SupportedLocales.English ? " (copy)" : " (копија)";
        var now = clock.GetUtcNow().UtcDateTime;
        var copy = new Activity
        {
            Id = Guid.CreateVersion7(),
            Title = ActivityRules.CopyTitle(source.Title, suffix),
            SubjectId = source.SubjectId,
            ToolType = source.ToolType,
            SchemaVersion = source.SchemaVersion,
            DocumentJson = source.DocumentJson,
            SizeBytes = source.SizeBytes,
            CreatedByUserId = tenant.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1,
        };
        await AddWithinLimitAsync(copy, cancellationToken);
        return ToResponse(copy);
    }

    public async Task DeleteAsync(Guid activityId, ActivityVersionRequest request, CancellationToken cancellationToken)
    {
        var activity = await LoadForChangeAsync(activityId, request.Version, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        activity.ArchivedAtUtc = now;
        activity.UpdatedAtUtc = now;
        activity.Version++;
        await SaveAsync(cancellationToken);
    }

    /// <summary>Adds the activity in a transaction that holds the plan-limit lock, so parallel saves cannot exceed the plan.</summary>
    private async Task AddWithinLimitAsync(Activity activity, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await entitlements.EnsureWithinLimitAsync(PlanLimits.Activities, ct => db.Set<Activity>().CountAsync(ct), adding: 1, cancellationToken);
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private (string Json, int Size) Document(SaveActivityRequest request)
    {
        if (!ActivityRules.IsSupported(request.ToolType, request.SchemaVersion))
        {
            throw Invalid("schemaVersion", "This board does not read this version of the document.");
        }

        return ActivityRules.Document(request.Document, options.Value.MaxDocumentBytes)
            ?? throw Invalid("document", $"The document must be a JSON object of at most {options.Value.MaxDocumentBytes / 1024} KB.");
    }

    /// <summary>A new subject must be an active catalog subject; the one the activity already has may stay.</summary>
    private async Task ThrowIfUnknownSubjectAsync(Guid? subjectId, Guid? current, CancellationToken cancellationToken)
    {
        if (subjectId is { } id && id != current && (await catalog.UnknownSubjectsAsync([id], cancellationToken)).Count > 0)
        {
            throw Invalid("subjectId", "Choose a subject from the catalog.");
        }
    }

    private async Task<Activity> LoadForChangeAsync(Guid activityId, int? version, CancellationToken cancellationToken)
    {
        var activity = await db.Set<Activity>().SingleOrDefaultAsync(a => a.Id == activityId, cancellationToken) ?? throw new NotFoundException();
        return activity.Version == version
            ? activity
            : throw new ConflictException("The activity was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The activity was changed elsewhere. Reload it and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }
    }

    private static ActivityResponse ToResponse(Activity a)
    {
        using var document = System.Text.Json.JsonDocument.Parse(a.DocumentJson);
        return new ActivityResponse(a.Id, a.Title, a.SubjectId, a.ToolType, a.SchemaVersion, document.RootElement.Clone(), a.SizeBytes, a.CreatedAtUtc, a.UpdatedAtUtc, a.Version);
    }

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}

internal sealed class SaveActivityRequestValidator : AbstractValidator<SaveActivityRequest>
{
    public SaveActivityRequestValidator()
    {
        RuleFor(x => x.Title).Must(t => t is not null && t.Trim().Length is >= 1 and <= Activity.TitleMaxLength).WithMessage("Enter a title (up to 150 characters).");
        RuleFor(x => x.ToolType).Must(t => t is not null && ActivityRules.ToolTypes.Contains(t)).WithMessage("Choose a board.");
        RuleFor(x => x.SchemaVersion).NotNull();
        RuleFor(x => x.Document).NotNull().WithMessage("The board's document is required.");
    }
}
