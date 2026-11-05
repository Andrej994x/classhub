using System.Text.RegularExpressions;
using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Catalog.Domain;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Catalog;

/// <summary>
/// Reference data: subject categories, subjects, education levels and teaching languages (mk/en). Teachers see the
/// active entries; platform staff manage them (R1-P11): create, rename, reorder, deactivate (never delete).
/// </summary>
public sealed class CatalogModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICatalogLookup, CatalogLookup>();
        services.AddScoped<CatalogAdminService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/catalog", async (ClassHubDbContext db, CancellationToken ct) =>
            {
                var categories = await db.Set<SubjectCategory>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder)
                    .Select(c => new CatalogItem(c.Id, c.Code, c.NameMk, c.NameEn)).ToListAsync(ct);
                var activeCategories = categories.Select(c => c.Id).ToList();
                var subjects = await db.Set<Subject>().AsNoTracking().Where(s => s.IsActive && activeCategories.Contains(s.CategoryId)).OrderBy(s => s.SortOrder)
                    .Select(s => new CatalogSubject(s.Id, s.Code, s.CategoryId, s.NameMk, s.NameEn)).ToListAsync(ct);
                var levels = await db.Set<EducationLevel>().AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.SortOrder)
                    .Select(l => new CatalogItem(l.Id, l.Code, l.NameMk, l.NameEn)).ToListAsync(ct);
                return TypedResults.Ok(new CatalogResponse(categories, subjects, levels, TeachingLanguages.All, [.. Countries.All.Order(StringComparer.Ordinal)]));
            })
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithTags("Catalog")
            .WithName("GetCatalog")
            .WithSummary("Active subject categories, subjects, education levels and teaching languages (mk/en names).");

        var admin = endpoints.MapGroup("/admin/catalog").WithTags("Admin").RequireAntiforgeryHeader().RequireAuthorization(ClassHubPolicies.PlatformStaff);

        admin.MapGet("", async (CatalogAdminService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .WithName("GetAdminCatalog");

        admin.MapPost("/{kind:regex(^(categories|subjects|levels)$)}", async (string kind, CatalogEntryRequest request, CatalogAdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CreateAsync(kind, request, ct)))
            .WithValidation<CatalogEntryRequest>()
            .WithName("CreateCatalogEntry")
            .WithSummary("Add a category, subject or level (the code is permanent).");

        admin.MapPut("/{kind:regex(^(categories|subjects|levels)$)}/{id:guid}", async (string kind, Guid id, CatalogEntryRequest request, CatalogAdminService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(kind, id, request, ct)))
            .WithValidation<CatalogEntryRequest>()
            .WithName("UpdateCatalogEntry")
            .WithSummary("Rename, reorder, activate or deactivate (entries are never deleted).");
    }
}

internal sealed class CatalogLookup(ClassHubDbContext db) : ICatalogLookup
{
    /// <summary>Subjects that are active and in an active category; others are not offered for new choices.</summary>
    private IQueryable<Subject> ActiveSubjects() =>
        db.Set<Subject>().AsNoTracking().Where(s => s.IsActive && db.Set<SubjectCategory>().Any(c => c.Id == s.CategoryId && c.IsActive));

    public async Task<IReadOnlyList<Guid>> UnknownSubjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var known = await ActiveSubjects().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken);
        return [.. ids.Except(known)];
    }

    public async Task<IReadOnlyList<Guid>> UnknownLevelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var known = await db.Set<EducationLevel>().AsNoTracking().Where(l => l.IsActive && ids.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken);
        return [.. ids.Except(known)];
    }

    public async Task<IReadOnlyList<CatalogItem>> SubjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await ActiveSubjects().Where(s => ids.Contains(s.Id)).OrderBy(s => s.SortOrder)
                .Select(s => new CatalogItem(s.Id, s.Code, s.NameMk, s.NameEn)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogItem>> LevelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await db.Set<EducationLevel>().AsNoTracking().Where(l => l.IsActive && ids.Contains(l.Id)).OrderBy(l => l.SortOrder)
                .Select(l => new CatalogItem(l.Id, l.Code, l.NameMk, l.NameEn)).ToListAsync(cancellationToken);
}

/// <summary>Staff management of the reference data (R1-P11): audited; codes are unique and permanent; nothing is deleted.</summary>
internal sealed class CatalogAdminService(ClassHubDbContext db, IAuditLog audit)
{
    public async Task<AdminCatalogResponse> GetAsync(CancellationToken cancellationToken) =>
        new(
            await db.Set<SubjectCategory>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
                .Select(c => new AdminCatalogEntry(c.Id, c.Code, c.NameMk, c.NameEn, c.SortOrder, c.IsActive, null)).ToListAsync(cancellationToken),
            await db.Set<Subject>().AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Code)
                .Select(s => new AdminCatalogEntry(s.Id, s.Code, s.NameMk, s.NameEn, s.SortOrder, s.IsActive, s.CategoryId)).ToListAsync(cancellationToken),
            await db.Set<EducationLevel>().AsNoTracking().OrderBy(l => l.SortOrder).ThenBy(l => l.Code)
                .Select(l => new AdminCatalogEntry(l.Id, l.Code, l.NameMk, l.NameEn, l.SortOrder, l.IsActive, null)).ToListAsync(cancellationToken));

    public async Task<AdminCatalogEntry> CreateAsync(string kind, CatalogEntryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Code))
        {
            throw Invalid("code", "A code is required for a new entry.");
        }

        var id = Guid.CreateVersion7();
        var sort = request.SortOrder ?? 0;
        var active = request.IsActive ?? true;
        switch (kind)
        {
            case "categories":
                db.Set<SubjectCategory>().Add(new SubjectCategory { Id = id, Code = request.Code, NameMk = request.NameMk!.Trim(), NameEn = request.NameEn!.Trim(), SortOrder = sort, IsActive = active });
                break;
            case "subjects":
                await ThrowIfUnknownCategoryAsync(request.CategoryId, cancellationToken);
                db.Set<Subject>().Add(new Subject { Id = id, Code = request.Code, CategoryId = request.CategoryId!.Value, NameMk = request.NameMk!.Trim(), NameEn = request.NameEn!.Trim(), SortOrder = sort, IsActive = active });
                break;
            default:
                db.Set<EducationLevel>().Add(new EducationLevel { Id = id, Code = request.Code, NameMk = request.NameMk!.Trim(), NameEn = request.NameEn!.Trim(), SortOrder = sort, IsActive = active });
                break;
        }

        audit.Record($"catalog.{Singular(kind)}.created", Singular(kind), request.Code, after: new { request.NameMk, request.NameEn, sort, active, request.CategoryId });
        await SaveAsync(cancellationToken);
        return await FindAsync(kind, id, cancellationToken);
    }

    public async Task<AdminCatalogEntry> UpdateAsync(string kind, Guid id, CatalogEntryRequest request, CancellationToken cancellationToken)
    {
        var before = await FindAsync(kind, id, cancellationToken);
        if (request.Code is not null && request.Code != before.Code)
        {
            throw Invalid("code", "The code of an entry never changes.");
        }

        var nameMk = request.NameMk!.Trim();
        var nameEn = request.NameEn!.Trim();
        var sort = request.SortOrder ?? before.SortOrder;
        var active = request.IsActive ?? before.IsActive;
        var categoryAfter = before.CategoryId;
        switch (kind)
        {
            case "categories":
                var category = await db.Set<SubjectCategory>().SingleAsync(c => c.Id == id, cancellationToken);
                (category.NameMk, category.NameEn, category.SortOrder, category.IsActive) = (nameMk, nameEn, sort, active);
                break;
            case "subjects":
                var subject = await db.Set<Subject>().SingleAsync(s => s.Id == id, cancellationToken);
                if (request.CategoryId is { } categoryId && categoryId != subject.CategoryId)
                {
                    await ThrowIfUnknownCategoryAsync(categoryId, cancellationToken);
                    subject.CategoryId = categoryId;
                    categoryAfter = categoryId;
                }

                (subject.NameMk, subject.NameEn, subject.SortOrder, subject.IsActive) = (nameMk, nameEn, sort, active);
                break;
            default:
                var level = await db.Set<EducationLevel>().SingleAsync(l => l.Id == id, cancellationToken);
                (level.NameMk, level.NameEn, level.SortOrder, level.IsActive) = (nameMk, nameEn, sort, active);
                break;
        }

        audit.Record($"catalog.{Singular(kind)}.updated", Singular(kind), before.Code,
            before: new { before.NameMk, before.NameEn, before.SortOrder, before.IsActive, before.CategoryId },
            after: new { NameMk = nameMk, NameEn = nameEn, SortOrder = sort, IsActive = active, CategoryId = categoryAfter });
        await SaveAsync(cancellationToken);
        return await FindAsync(kind, id, cancellationToken);
    }

    private async Task<AdminCatalogEntry> FindAsync(string kind, Guid id, CancellationToken cancellationToken)
    {
        var all = await GetAsync(cancellationToken);
        var list = kind switch { "categories" => all.Categories, "subjects" => all.Subjects, _ => all.Levels };
        return list.SingleOrDefault(e => e.Id == id) ?? throw new NotFoundException();
    }

    private async Task ThrowIfUnknownCategoryAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null || !await db.Set<SubjectCategory>().AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw Invalid("categoryId", "Unknown category.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("Another entry already uses this code.");
        }
    }

    private static string Singular(string kind) => kind switch { "categories" => "category", "subjects" => "subject", _ => "level" };

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}

internal sealed partial class CatalogEntryRequestValidator : AbstractValidator<CatalogEntryRequest>
{
    public CatalogEntryRequestValidator()
    {
        RuleFor(x => x.Code).Must(c => c is null || (c.Length is >= 2 and <= 40 && CodePattern().IsMatch(c)))
            .WithMessage("Lowercase letters, digits and single hyphens (2 to 40 characters).");
        RuleFor(x => x.NameMk).Must(n => n is not null && n.Trim().Length is >= 1 and <= 100).WithMessage("Enter a name (up to 100 characters).");
        RuleFor(x => x.NameEn).Must(n => n is not null && n.Trim().Length is >= 1 and <= 100).WithMessage("Enter a name (up to 100 characters).");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();
}
