using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules;

/// <summary>
/// A business module of the modular monolith. Each module registers its own services and maps its
/// own Minimal API route group. Other modules may only use a module through its <c>Contracts</c> namespace.
/// </summary>
public interface IModule
{
    /// <summary>Registers the module's services.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's endpoints.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
