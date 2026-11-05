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

    [Fact]
    public async Task Rolling_back_diagnostic_leads_keeps_the_requests_as_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = fixture.CreateIsolatedDatabaseConnectionString();
        TestDatabaseGuard.AssertIsolated(connectionString, fixture.ExpectedDataSource);
        var options = new DbContextOptionsBuilder<ClassHubDbContext>().UseSqlServer(connectionString).Options;

        await using var db = new ClassHubDbContext(options);
        try
        {
            await db.Database.MigrateAsync(ct);
            Guid workspace = Guid.NewGuid(), test = Guid.NewGuid(), attempt = Guid.NewGuid(), lead = Guid.NewGuid();
            var subject = await db.Database.SqlQuery<Guid>($"SELECT TOP (1) Id AS [Value] FROM Subjects").SingleAsync(ct);
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlAsync($"INSERT INTO Workspaces (Id, Name, TimeZone, Currency, Locale, CreatedAtUtc) VALUES ({workspace}, N'W', 'Europe/Skopje', 'MKD', 'mk', {now})", ct);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO DiagnosticTests (Id, WorkspaceId, Title, Slug, SubjectId, Difficulty, Status, AttemptCount, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ({test}, {workspace}, N'T', 'test-1', {subject}, 1, 1, 1, {now}, {now}, 0)", ct);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO TestAttempts (Id, WorkspaceId, TestId, AnswersJson, Points, MaxPoints, CorrectQuestions, TotalQuestions, CompletedAtUtc) VALUES ({attempt}, {workspace}, {test}, '{{}}', 0, 0, 0, 0, {now})", ct);
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO Leads (Id, WorkspaceId, Type, Status, Name, Email, AttemptId, ConsentVersion, ConsentedAtUtc, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ({lead}, {workspace}, 3, 1, N'Марија', 'm@example.test', {attempt}, 'v1', {now}, {now}, {now}, 0)", ct);
            await db.Database.ExecuteSqlAsync($"UPDATE TestAttempts SET LeadId = {lead} WHERE Id = {attempt}", ct);

            await db.GetService<IMigrator>().MigrateAsync("20261105131423_AddDiagnosticTests", cancellationToken: ct);

            var kept = await db.Database.SqlQuery<int>($"SELECT [Type] AS [Value] FROM Leads WHERE Id = {lead}").SingleAsync(ct);
            kept.ShouldBe(1); // the request survives as a message
            (await db.Database.SqlQuery<string>($"SELECT Email AS [Value] FROM Leads WHERE Id = {lead}").SingleAsync(ct)).ShouldBe("m@example.test");
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
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
