using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Offers.Application;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Offers;

/// <summary>Offers & programs (M05, R1-P2): single lessons, packages and group programs of a workspace.</summary>
public sealed class OffersModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<OfferService>();
        services.AddScoped<IOfferLookup>(sp => sp.GetRequiredService<OfferService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var offers = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/offers").WithTags("Offers").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.OffersRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.OffersManage);

        offers.MapGet("/", async (OfferService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)))
            .RequireAuthorization(read).WithName("ListOffers");

        offers.MapPost("/", async (SaveOfferRequest request, OfferService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return TypedResults.Created($"offers/{created.Id}", created);
            })
            .WithValidation<SaveOfferRequest>()
            .RequireAuthorization(manage).WithName("CreateOffer");

        offers.MapGet("/{offerId:guid}", async (Guid offerId, OfferService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(offerId, ct)))
            .RequireAuthorization(read).WithName("GetOffer");

        offers.MapPut("/{offerId:guid}", async (Guid offerId, SaveOfferRequest request, OfferService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(offerId, request, ct)))
            .WithValidation<SaveOfferRequest>()
            .RequireAuthorization(manage).WithName("UpdateOffer");

        offers.MapDelete("/{offerId:guid}", async (Guid offerId, OfferService service, CancellationToken ct) =>
            {
                await service.ArchiveAsync(offerId, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(manage).WithName("ArchiveOffer")
            .WithSummary("Archive the offer (hidden, never bookable again; history is kept).");

        offers.MapPut("/order", async (ReorderOffersRequest request, OfferService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ReorderAsync(request.OfferIds ?? [], ct)))
            .RequireAuthorization(manage).WithName("ReorderOffers");
    }
}
