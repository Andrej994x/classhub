using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Api.Platform;

/// <summary>
/// E2E environment only: migrates the disposable Playwright database at startup. Migrations are never applied
/// on startup anywhere else (ROADMAP B.3). Refuses any database that is not a throwaway <c>ClassHubE2E_*</c>
/// database reached over TCP with SQL authentication (that is, the container started by <c>scripts/e2e</c>),
/// so a misconfigured run can never migrate or fill the local <c>ClassHubDb</c>.
/// </summary>
public static class E2EDatabase
{
    public const string EnvironmentName = "E2E";
    public const string DatabasePrefix = "ClassHubE2E_";

    public static async Task MigrateIfE2EAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsEnvironment(EnvironmentName))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHubDbContext>();
        AssertDisposable(db.Database.GetConnectionString());
        await db.Database.MigrateAsync();
    }

    public static void AssertDisposable(string? connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString ?? throw new InvalidOperationException("E2E needs ConnectionStrings:ClassHub."));
        var dataSource = builder.DataSource.Trim();
        var problems = new List<string>();
        if (!builder.InitialCatalog.StartsWith(DatabasePrefix, StringComparison.Ordinal))
        {
            problems.Add($"database must start with {DatabasePrefix}");
        }

        if (builder.IntegratedSecurity)
        {
            problems.Add("Windows authentication is not allowed (use the container's sa login)");
        }

        if (!(dataSource.StartsWith("127.0.0.1,", StringComparison.Ordinal) || dataSource.StartsWith("localhost,", StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add("server must be a loopback TCP endpoint with an explicit port (127.0.0.1,<port>)");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Refusing to start the E2E environment: " + string.Join("; ", problems) + ".");
        }
    }
}
