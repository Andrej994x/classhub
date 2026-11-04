using ClassHub.Core.Infrastructure.Audit;
using ClassHub.Core.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClassHub.Core.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Name of the connection string in configuration (<c>ConnectionStrings:ClassHub</c>).</summary>
    public const string ConnectionStringName = "ClassHub";

    /// <summary>Health check tag for checks that verify external dependencies (database).</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Registers <see cref="ClassHubDbContext"/> on SQL Server. Fails fast when the connection string
    /// is missing. No retry strategy (it conflicts with explicit transactions; see docs/DATABASE.md).
    /// </summary>
    public static IServiceCollection AddClassHubPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. " +
                "Development uses appsettings.Development.json (Windows authentication on .\\SQLEXPRESS); " +
                "other environments set the environment variable ConnectionStrings__ClassHub.");
        }

        services.AddDbContext<ClassHubDbContext>(options => options.UseSqlServer(connectionString));
        services.AddClassHubTenancy();
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditLog, AuditLog>();

        services.AddHealthChecks()
            .AddDbContextCheck<ClassHubDbContext>("database", tags: [ReadinessTag]);

        return services;
    }
}
