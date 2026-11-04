using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules;

/// <summary>Discovers, registers and maps the business modules of the modular monolith.</summary>
public static class ModuleRegistration
{
    /// <summary>Route prefix of every module endpoint.</summary>
    public const string ApiPrefix = "/api";

    /// <summary>All concrete <see cref="IModule"/> types in the assembly, in a stable order.</summary>
    public static IReadOnlyList<IModule> Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToList();
    }

    /// <summary>Registers every module's services and all FluentValidation validators of ClassHub.Core.</summary>
    public static IServiceCollection AddClassHubModules(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var module in Discover(CoreAssembly.Assembly))
        {
            module.AddServices(services, configuration);
            services.AddSingleton(module);
        }

        services.AddValidatorsFromAssembly(CoreAssembly.Assembly, includeInternalTypes: true);
        return services;
    }

    /// <summary>Maps every registered module (including ones added by the host) under <c>/api</c>.</summary>
    public static IEndpointRouteBuilder MapClassHubModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapGroup(ApiPrefix);
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.MapEndpoints(api);
        }

        return endpoints;
    }
}
