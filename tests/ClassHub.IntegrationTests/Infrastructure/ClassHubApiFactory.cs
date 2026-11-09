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

    /// <summary>Per-run folder for uploaded files (deleted with the factory).</summary>
    public string FileRoot { get; } = Path.Combine(Path.GetTempPath(), "classhub-tests", Guid.NewGuid().ToString("N"));

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

        // All requests share one partition in TestServer (no client IP); rate limits have their own tests.
        builder.UseSetting("RateLimiting:DefaultPermitLimit", "1000000");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicFormPermitLimit", "100000");
        builder.UseSetting("RateLimiting:PublicReadPermitLimit", "100000");

        // Emails are delivered to the dev mailbox table quickly so tests can read links from them.
        builder.UseSetting("Email:Provider", "DevMailbox");
        builder.UseSetting("Email:DispatchIntervalMilliseconds", "100");
        builder.UseSetting("App:PublicBaseUrl", "http://classhub.test");
        // Re-validate session cookies on every request so suspension/reset tests need no waiting.
        builder.UseSetting("Identity:SecurityStampValidationIntervalSeconds", "0");
        // Uploaded files go to a per-run temporary folder, never into the repository's App_Data.
        builder.UseSetting("Files:RootPath", FileRoot);
        // Lesson consumption runs when a test asks for it (LessonConsumptionJob.RunOnceAsync), never in between.
        builder.UseSetting("Finance:ConsumptionIntervalSeconds", "86400");

        builder.ConfigureTestServices(services =>
        {
            // Replace the DbContext configuration outright, so no other configuration source can
            // redirect the API to another database.
            services.RemoveAll<DbContextOptions<ClassHubDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ClassHubDbContext>>();
            services.AddDbContext<ClassHubDbContext>(options => options.UseSqlServer(_connectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(FileRoot))
        {
            Directory.Delete(FileRoot, recursive: true);
        }
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
