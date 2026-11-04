using Microsoft.Data.SqlClient;

namespace ClassHub.IntegrationTests.Infrastructure;

/// <summary>
/// Fail-fast check that a connection string points at the disposable test container and nowhere else.
/// It runs before migrations and again on the connection string the API host actually uses, so a
/// misconfiguration can never migrate or write to the local ClassHubDb or any shared database.
/// </summary>
public static class TestDatabaseGuard
{
    /// <summary>Every integration test database name starts with this prefix.</summary>
    public const string DatabasePrefix = "ClassHubTest_";

    private static readonly HashSet<string> ForbiddenDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClassHubDb", "ClassHub", "ClassHubCi", "master", "model", "msdb", "tempdb",
    };

    /// <param name="connectionString">The connection string about to be used.</param>
    /// <param name="expectedDataSource">The container's <c>host,mappedPort</c>.</param>
    /// <exception cref="InvalidOperationException">The target is not an isolated test database.</exception>
    public static void AssertIsolated(string? connectionString, string expectedDataSource)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw Unsafe("the connection string is empty");
        }

        if (string.IsNullOrWhiteSpace(expectedDataSource))
        {
            throw Unsafe("no expected container data source was supplied");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw Unsafe("the connection string cannot be parsed");
        }

        var dataSource = builder.DataSource.Trim();

        if (dataSource.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ||
            dataSource.StartsWith("np:", StringComparison.OrdinalIgnoreCase) ||
            dataSource.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase) ||
            dataSource is "." or "(local)" ||
            dataSource.Contains('\\', StringComparison.Ordinal))
        {
            throw Unsafe($"'{dataSource}' is a local or named SQL Server instance, not the test container");
        }

        if (!string.Equals(dataSource, expectedDataSource.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Unsafe($"data source '{dataSource}' is not the test container '{expectedDataSource}'");
        }

        if (builder.IntegratedSecurity)
        {
            throw Unsafe("Integrated Security is not allowed; the container uses a per-run SQL login");
        }

        if (!string.IsNullOrEmpty(builder.AttachDBFilename))
        {
            throw Unsafe("AttachDbFilename is not allowed");
        }

        var database = builder.InitialCatalog;
        if (ForbiddenDatabases.Contains(database) ||
            !database.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            database.Length <= DatabasePrefix.Length)
        {
            throw Unsafe($"database '{database}' is not a '{DatabasePrefix}*' test database");
        }
    }

    private static InvalidOperationException Unsafe(string reason) =>
        new($"Refusing to use the database for integration tests: {reason}. " +
            "Integration tests only run against a disposable Testcontainers SQL Server.");
}
