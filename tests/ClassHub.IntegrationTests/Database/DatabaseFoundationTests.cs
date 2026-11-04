using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Persistence.Technical;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Database;

/// <summary>R0-P2: the migrated database has the agreed settings and the conventions round-trip data.</summary>
[Collection(ApiCollection.Name)]
public sealed class DatabaseFoundationTests(ClassHubApiFixture fixture)
{
    [Fact]
    public async Task Database_uses_the_configured_collation_and_read_committed_snapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateDbContext();

        var collation = await db.Database
            .SqlQueryRaw<string>("SELECT collation_name AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .SingleAsync(ct);
        var rcsi = await db.Database
            .SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .SingleAsync(ct);

        collation.ShouldBe(ClassHubModelConventions.DatabaseCollation);
        rcsi.ShouldBeTrue();
    }

    [Fact]
    public async Task All_migrations_are_applied_and_the_model_has_no_pending_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateDbContext();

        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldBe(db.Database.GetMigrations());
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task Seeded_technical_row_exists()
    {
        await using var db = fixture.CreateDbContext();

        var row = await db.SchemaInfo.SingleAsync(x => x.Key == "schema.foundation", TestContext.Current.CancellationToken);

        row.Value.ShouldBe("R0-P2");
        row.UpdatedAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Cyrillic_text_and_utc_times_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = $"test.{Guid.NewGuid():N}";
        var localTime = new DateTime(2026, 3, 29, 2, 30, 15, 123, DateTimeKind.Local);
        const string Macedonian = "Ѓорѓи Ќосевски — ѕвезда, љубов, њива, џемпер (𝛑)";

        await using (var write = fixture.CreateDbContext())
        {
            write.SchemaInfo.Add(new SchemaInfo { Key = key, Value = Macedonian, UpdatedAtUtc = localTime });
            await write.SaveChangesAsync(ct);
        }

        await using var read = fixture.CreateDbContext();
        var stored = await read.SchemaInfo.AsNoTracking().SingleAsync(x => x.Key == key, ct);

        stored.Value.ShouldBe(Macedonian);
        stored.UpdatedAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
        stored.UpdatedAtUtc.ShouldBe(localTime.ToUniversalTime());
    }

    [Fact]
    public async Task Datetime_columns_are_datetime2_with_millisecond_precision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateDbContext();

        var columnType = await db.Database.SqlQueryRaw<string>(
                "SELECT CONCAT(DATA_TYPE, '(', DATETIME_PRECISION, ')') AS [Value] FROM INFORMATION_SCHEMA.COLUMNS " +
                "WHERE TABLE_NAME = 'SchemaInfo' AND COLUMN_NAME = 'UpdatedAtUtc'")
            .SingleAsync(ct);

        columnType.ShouldBe("datetime2(3)");
    }
}
