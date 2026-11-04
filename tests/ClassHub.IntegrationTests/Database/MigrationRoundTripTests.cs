using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClassHub.IntegrationTests.Database;

/// <summary>Every migration applies to an empty database, reverts completely, and re-applies.</summary>
[Collection(ApiCollection.Name)]
public sealed class MigrationRoundTripTests(ClassHubApiFixture fixture)
{
    [Fact]
    public async Task Migrations_apply_revert_and_reapply_on_an_empty_database()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = fixture.CreateIsolatedDatabaseConnectionString();
        TestDatabaseGuard.AssertIsolated(connectionString, fixture.ExpectedDataSource);
        var options = new DbContextOptionsBuilder<ClassHubDbContext>().UseSqlServer(connectionString).Options;

        await using var db = new ClassHubDbContext(options);
        try
        {
            var migrator = db.GetService<IMigrator>();

            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
            (await TableExistsAsync(db, "SchemaInfo", ct)).ShouldBeTrue();

            await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken: ct);
            (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBeEmpty();
            (await TableExistsAsync(db, "SchemaInfo", ct)).ShouldBeFalse();
            (await ReadCommittedSnapshotAsync(db, ct)).ShouldBeFalse();

            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
            (await ReadCommittedSnapshotAsync(db, ct)).ShouldBeTrue();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> TableExistsAsync(ClassHubDbContext db, string table, CancellationToken ct) =>
        await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = {table}")
            .SingleAsync(ct) == 1;

    private static Task<bool> ReadCommittedSnapshotAsync(ClassHubDbContext db, CancellationToken ct) =>
        db.Database
            .SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .SingleAsync(ct);
}
