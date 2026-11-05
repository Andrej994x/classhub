using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Catalog.Domain;
using ClassHub.Core.SharedKernel.Globalization;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Catalog;

/// <summary>Reference data: subject categories, subjects, education levels and teaching languages (mk/en).</summary>
public sealed class CatalogModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<ICatalogLookup, CatalogLookup>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/catalog", async (ClassHubDbContext db, CancellationToken ct) =>
            {
                var categories = await db.Set<SubjectCategory>().AsNoTracking().OrderBy(c => c.SortOrder)
                    .Select(c => new CatalogItem(c.Id, c.Code, c.NameMk, c.NameEn)).ToListAsync(ct);
                var subjects = await db.Set<Subject>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder)
                    .Select(s => new CatalogSubject(s.Id, s.Code, s.CategoryId, s.NameMk, s.NameEn)).ToListAsync(ct);
                var levels = await db.Set<EducationLevel>().AsNoTracking().OrderBy(l => l.SortOrder)
                    .Select(l => new CatalogItem(l.Id, l.Code, l.NameMk, l.NameEn)).ToListAsync(ct);
                return TypedResults.Ok(new CatalogResponse(categories, subjects, levels, TeachingLanguages.All, [.. Countries.All.Order(StringComparer.Ordinal)]));
            })
            .RequireAuthorization(ClassHubPolicies.Authenticated)
            .WithTags("Catalog")
            .WithName("GetCatalog")
            .WithSummary("Subject categories, subjects, education levels and teaching languages (mk/en names).");
    }
}

internal sealed class CatalogLookup(ClassHubDbContext db) : ICatalogLookup
{
    public async Task<IReadOnlyList<Guid>> UnknownSubjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var known = await db.Set<Subject>().AsNoTracking().Where(s => s.IsActive && ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken);
        return [.. ids.Except(known)];
    }

    public async Task<IReadOnlyList<Guid>> UnknownLevelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var known = await db.Set<EducationLevel>().AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken);
        return [.. ids.Except(known)];
    }
}
