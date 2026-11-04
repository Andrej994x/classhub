using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClassHub.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline in memory (TestServer) against the isolated container database.
/// </summary>
public sealed class ClassHubApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Not "Development": user-secrets and appsettings.Development.json (the local SQLEXPRESS
    /// connection string) are not loaded.
    /// </summary>
    public const string EnvironmentName = "Testing";

    private readonly string _connectionString;
    private readonly string _expectedDataSource;

    public ClassHubApiFactory(string connectionString, string expectedDataSource)
    {
        TestDatabaseGuard.AssertIsolated(connectionString, expectedDataSource);
        _connectionString = connectionString;
        _expectedDataSource = expectedDataSource;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        // Program.cs reads the connection string while building services, so it is a host setting.
        builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}", _connectionString);

        builder.ConfigureTestServices(services =>
        {
            // Replace the DbContext configuration outright, so no other configuration source can
            // redirect the API to another database.
            services.RemoveAll<DbContextOptions<ClassHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ClassHubDbContext>>();
            services.AddDbContext<ClassHubDbContext>(options => options.UseSqlServer(_connectionString));
        });
    }

    /// <summary>
    /// Builds the host, re-checks the connection string the DbContext really uses, and only then applies
    /// the migrations to the isolated test database.
    /// </summary>
    public async Task MigrateIsolatedDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();

        TestDatabaseGuard.AssertIsolated(db.Database.GetConnectionString(), _expectedDataSource);

        await db.Database.MigrateAsync(cancellationToken);
    }
}
