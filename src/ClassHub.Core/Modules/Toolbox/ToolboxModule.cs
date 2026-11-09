using System.Text.RegularExpressions;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Toolbox.Contracts;
using ClassHub.Core.Modules.Toolbox.Domain;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Modules.Toolbox;

/// <summary>Which hosts an external tool's official link may point at (subdomains included).</summary>
public sealed class ToolboxOptions
{
    public const string SectionName = "Toolbox";

    public string[] AllowedHosts { get; set; } = ["geogebra.org", "desmos.com", "phet.colorado.edu", "molview.org"];
}

/// <summary>
/// Teaching Toolbox catalog (M09, R3-P1): platform staff manage the tools (audited, deactivated never deleted); teachers
/// search the active ones by name, subject and category and open external tools by their official links.
/// </summary>
public sealed class ToolboxModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ToolboxOptions>(configuration.GetSection(ToolboxOptions.SectionName));
        services.AddScoped<ToolboxService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(WorkspacesModule.WorkspaceRoute).WithTags("Toolbox").RequireAntiforgeryHeader()
            .MapGet("/toolbox", async (string? q, Guid? subjectId, string? category, ToolboxService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SearchAsync(q, subjectId, category, ct)))
            .RequireAuthorization(WorkspacePolicies.Read)
            .WithName("SearchToolbox")
            .WithSummary("Active tools matching a search (names and descriptions in both languages), a subject and a category; featured first.");

        var admin = endpoints.MapGroup("/admin/toolbox").WithTags("Admin").RequireAntiforgeryHeader().RequireAuthorization(ClassHubPolicies.PlatformStaff);
        admin.MapGet("", async (ToolboxService service, CancellationToken ct) => TypedResults.Ok(await service.ListAllAsync(ct)))
            .WithName("ListAdminTools");
        admin.MapPost("", async (SaveToolRequest request, ToolboxService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CreateAsync(request, ct)))
            .WithValidation<SaveToolRequest>()
            .WithName("CreateTool")
            .WithSummary("Add a tool (its code is permanent).");
        admin.MapPut("/{toolId:guid}", async (Guid toolId, SaveToolRequest request, ToolboxService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(toolId, request, ct)))
            .WithValidation<SaveToolRequest>()
            .WithName("UpdateTool")
            .WithSummary("Change, feature, activate or deactivate a tool (tools are never deleted).");
    }
}

internal sealed class ToolboxService(ClassHubDbContext db, ICatalogLookup catalog, IAuditLog audit, IOptions<ToolboxOptions> options, TimeProvider clock)
{
    public const int SearchMaxLength = 100;

    public async Task<ToolboxResponse> SearchAsync(string? q, Guid? subjectId, string? category, CancellationToken cancellationToken)
    {
        // Active tools only; a board that has not shipped stays hidden even if a row says otherwise.
        var available = ToolRules.AvailableBuiltInKeys.ToList();
        var tools = db.Set<Tool>().AsNoTracking().Where(t => t.IsActive && (t.Kind == ToolKind.External || available.Contains(t.BuiltInKey!)));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            if (text.Length > SearchMaxLength)
            {
                throw Invalid("q", $"Search for at most {SearchMaxLength} characters.");
            }

            tools = tools.Where(t => t.NameMk.Contains(text) || t.NameEn.Contains(text) || t.DescriptionMk.Contains(text) || t.DescriptionEn.Contains(text));
        }

        if (subjectId is { } subject)
        {
            // Tools for that subject, and tools for any subject (none listed).
            tools = tools.Where(t => db.Set<ToolSubject>().Any(s => s.ToolId == t.Id && s.SubjectId == subject) || !db.Set<ToolSubject>().Any(s => s.ToolId == t.Id));
        }

        if (category is not null)
        {
            if (!Names<ToolCategory>().Contains(category))
            {
                throw Invalid("category", "Unknown category.");
            }

            var parsed = Enum.Parse<ToolCategory>(category);
            tools = tools.Where(t => t.Category == parsed);
        }

        var rows = await tools.OrderByDescending(t => t.IsFeatured).ThenBy(t => t.SortOrder).ThenBy(t => t.Code).ToListAsync(cancellationToken);
        var subjects = await SubjectsOfAsync(rows.Select(t => t.Id).ToList(), cancellationToken);
        return new ToolboxResponse([.. rows.Select(t => ToResponse(t, subjects))], [.. Enum.GetNames<ToolCategory>()]);
    }

    public async Task<IReadOnlyList<AdminToolResponse>> ListAllAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Set<Tool>().AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Code).ToListAsync(cancellationToken);
        var subjects = await SubjectsOfAsync(rows.Select(t => t.Id).ToList(), cancellationToken);
        return [.. rows.Select(t => ToAdmin(t, subjects))];
    }

    public async Task<AdminToolResponse> CreateAsync(SaveToolRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Code))
        {
            throw Invalid("code", "A code is required for a new tool.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tool = new Tool
        {
            Id = Guid.CreateVersion7(),
            Code = request.Code,
            NameMk = string.Empty,
            NameEn = string.Empty,
            DescriptionMk = string.Empty,
            DescriptionEn = string.Empty,
            CreatedAtUtc = now,
            Version = 1,
        };
        var subjectIds = await ApplyAsync(tool, request, now, [], cancellationToken);
        db.Set<Tool>().Add(tool);
        db.Set<ToolSubject>().AddRange(subjectIds.Select(id => new ToolSubject { ToolId = tool.Id, SubjectId = id }));
        audit.Record("toolbox.tool.created", nameof(Tool), tool.Code, after: Snapshot(tool, subjectIds));
        await SaveAsync(cancellationToken);
        return await GetAsync(tool.Id, cancellationToken);
    }

    public async Task<AdminToolResponse> UpdateAsync(Guid toolId, SaveToolRequest request, CancellationToken cancellationToken)
    {
        var tool = await db.Set<Tool>().SingleOrDefaultAsync(t => t.Id == toolId, cancellationToken) ?? throw new NotFoundException();
        if (tool.Version != request.Version)
        {
            throw new ConflictException("The tool was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }

        if (request.Code is not null && request.Code != tool.Code)
        {
            throw Invalid("code", "The code of a tool never changes.");
        }

        var links = await db.Set<ToolSubject>().Where(s => s.ToolId == tool.Id).ToListAsync(cancellationToken);
        var before = Snapshot(tool, [.. links.Select(l => l.SubjectId)]);
        var subjectIds = await ApplyAsync(tool, request, clock.GetUtcNow().UtcDateTime, [.. links.Select(l => l.SubjectId)], cancellationToken);
        tool.Version++;
        db.Set<ToolSubject>().RemoveRange(links.Where(l => !subjectIds.Contains(l.SubjectId)));
        db.Set<ToolSubject>().AddRange(subjectIds.Where(id => links.All(l => l.SubjectId != id)).Select(id => new ToolSubject { ToolId = tool.Id, SubjectId = id }));
        audit.Record("toolbox.tool.updated", nameof(Tool), tool.Code, before: before, after: Snapshot(tool, subjectIds));
        await SaveAsync(cancellationToken);
        return await GetAsync(tool.Id, cancellationToken);
    }

    /// <summary>Copies the request onto the tool after the checks that need data or configuration; returns its subjects.</summary>
    private async Task<IReadOnlyList<Guid>> ApplyAsync(Tool tool, SaveToolRequest request, DateTime now, IReadOnlyList<Guid> existing, CancellationToken cancellationToken)
    {
        var kind = Enum.Parse<ToolKind>(request.Kind!);
        var url = request.Url?.Trim();
        if (kind == ToolKind.External && !ToolRules.IsAllowedUrl(url, options.Value.AllowedHosts))
        {
            throw Invalid("url", "Enter the tool's official HTTPS link (on an approved site).");
        }

        var subjectIds = (request.SubjectIds ?? []).Distinct().ToList();
        // A subject the tool already has may stay even if it was deactivated since; new ones must be active.
        if ((await catalog.UnknownSubjectsAsync([.. subjectIds.Except(existing)], cancellationToken)).Count > 0)
        {
            throw Invalid("subjectIds", "Choose subjects from the catalog.");
        }

        tool.NameMk = request.NameMk!.Trim();
        tool.NameEn = request.NameEn!.Trim();
        tool.DescriptionMk = request.DescriptionMk!.Trim();
        tool.DescriptionEn = request.DescriptionEn!.Trim();
        tool.Kind = kind;
        tool.Category = Enum.Parse<ToolCategory>(request.Category!);
        tool.Url = kind == ToolKind.External ? url : null;
        tool.BuiltInKey = kind == ToolKind.BuiltIn ? request.BuiltInKey : null;
        tool.LicenseNoteMk = string.IsNullOrWhiteSpace(request.LicenseNoteMk) ? null : request.LicenseNoteMk.Trim();
        tool.LicenseNoteEn = string.IsNullOrWhiteSpace(request.LicenseNoteEn) ? null : request.LicenseNoteEn.Trim();
        tool.IsActive = request.IsActive ?? tool.IsActive;
        if (tool.IsActive && !ToolRules.CanBeActive(kind, tool.BuiltInKey))
        {
            throw Invalid("isActive", "This board is not available yet; keep it inactive.");
        }

        tool.IsFeatured = request.IsFeatured ?? tool.IsFeatured;
        tool.SortOrder = request.SortOrder ?? tool.SortOrder;
        tool.UpdatedAtUtc = now;
        return subjectIds;
    }

    private async Task<AdminToolResponse> GetAsync(Guid toolId, CancellationToken cancellationToken) =>
        (await ListAllAsync(cancellationToken)).SingleOrDefault(t => t.Tool.Id == toolId) ?? throw new NotFoundException();

    private async Task<ILookup<Guid, Guid>> SubjectsOfAsync(IReadOnlyList<Guid> toolIds, CancellationToken cancellationToken) =>
        (await db.Set<ToolSubject>().AsNoTracking().Where(s => toolIds.Contains(s.ToolId)).ToListAsync(cancellationToken)).ToLookup(s => s.ToolId, s => s.SubjectId);

    private static ToolResponse ToResponse(Tool t, ILookup<Guid, Guid> subjects) =>
        new(t.Id, t.Code, t.NameMk, t.NameEn, t.DescriptionMk, t.DescriptionEn, t.Kind.ToString(), t.Category.ToString(), t.Url, t.BuiltInKey,
            [.. subjects[t.Id].Order()], t.IsFeatured, t.LicenseNoteMk, t.LicenseNoteEn);

    private static AdminToolResponse ToAdmin(Tool t, ILookup<Guid, Guid> subjects) => new(ToResponse(t, subjects), t.IsActive, t.SortOrder, t.UpdatedAtUtc, t.Version);

    /// <summary>Every field staff manage, so an audit entry shows what changed and what it was before.</summary>
    private static object Snapshot(Tool t, IReadOnlyList<Guid> subjectIds) =>
        new
        {
            t.NameMk, t.NameEn, t.DescriptionMk, t.DescriptionEn, Kind = t.Kind.ToString(), Category = t.Category.ToString(), t.Url, t.BuiltInKey,
            t.LicenseNoteMk, t.LicenseNoteEn, t.IsActive, t.IsFeatured, t.SortOrder, SubjectIds = subjectIds,
        };

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The tool was changed elsewhere. Reload and apply your changes again.", ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("Another tool already uses this code or board.");
        }
    }

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    /// <summary>An enum's names (never numbers: "2" is not a kind).</summary>
    internal static string[] Names<TEnum>()
        where TEnum : struct, Enum => Enum.GetNames<TEnum>();
}

internal sealed partial class SaveToolRequestValidator : AbstractValidator<SaveToolRequest>
{
    public SaveToolRequestValidator()
    {
        RuleFor(x => x.Code).Must(c => c is null || (c.Length is >= 2 and <= Tool.CodeMaxLength && CodePattern().IsMatch(c)))
            .WithMessage("Lowercase letters, digits and single hyphens (2 to 40 characters).");
        RuleFor(x => x.NameMk).Must(n => Fits(n, Tool.NameMaxLength)).WithMessage("Enter a name (up to 100 characters).");
        RuleFor(x => x.NameEn).Must(n => Fits(n, Tool.NameMaxLength)).WithMessage("Enter a name (up to 100 characters).");
        RuleFor(x => x.DescriptionMk).Must(n => Fits(n, Tool.DescriptionMaxLength)).WithMessage("Enter a description (up to 600 characters).");
        RuleFor(x => x.DescriptionEn).Must(n => Fits(n, Tool.DescriptionMaxLength)).WithMessage("Enter a description (up to 600 characters).");
        RuleFor(x => x.Kind).Must(k => k is not null && ToolboxService.Names<ToolKind>().Contains(k)).WithMessage("Choose External or BuiltIn.");
        RuleFor(x => x.Category).Must(c => c is not null && ToolboxService.Names<ToolCategory>().Contains(c)).WithMessage("Choose a category.");
        RuleFor(x => x.BuiltInKey).Must(k => k is not null && ToolRules.BuiltInKeys.Contains(k)).When(x => x.Kind == nameof(ToolKind.BuiltIn))
            .WithMessage("Choose one of the built-in boards.");
        RuleFor(x => x.LicenseNoteMk).MaximumLength(Tool.LicenseNoteMaxLength);
        RuleFor(x => x.LicenseNoteEn).MaximumLength(Tool.LicenseNoteMaxLength);
        RuleFor(x => x.SubjectIds).Must(s => s is null || s.Count <= 20).WithMessage("Choose at most 20 subjects.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }

    private static bool Fits(string? value, int max) => value is not null && value.Trim().Length is >= 1 && value.Trim().Length <= max;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();
}
