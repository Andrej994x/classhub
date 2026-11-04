using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClassHub.IntegrationTests.Database;

/// <summary>Upgrading a populated R0-P4 database gives every existing teacher a workspace (R0-P5 acceptance).</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkspaceBackfillMigrationTests(ClassHubApiFixture fixture)
{
    private const string LastR0P4Migration = "20261104122953_AddIdentityAndEmailOutbox";

    [Fact]
    public async Task Accounts_created_before_workspaces_get_an_owned_workspace_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = fixture.CreateIsolatedDatabaseConnectionString();
        TestDatabaseGuard.AssertIsolated(connectionString, fixture.ExpectedDataSource);
        var options = new DbContextOptionsBuilder<ClassHubDbContext>().UseSqlServer(connectionString).Options;

        await using var db = new ClassHubDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(LastR0P4Migration, cancellationToken: ct);
            var macedonian = Guid.NewGuid();
            var english = Guid.NewGuid();
            await InsertR0P4UserAsync(db, macedonian, "mk", "Europe/Skopje", ct);
            await InsertR0P4UserAsync(db, english, "en", "UTC", ct);

            await db.Database.MigrateAsync(ct);

            var rows = await db.Database.SqlQueryRaw<BackfilledRow>(
                    """
                    SELECT m.UserId, m.Role, w.Name, w.TimeZone, w.Currency, w.Locale,
                           (SELECT COUNT(*) FROM AuditLog a WHERE a.WorkspaceId = w.Id AND a.Action = N'workspace.created') AS AuditEntries
                    FROM WorkspaceMembers m JOIN Workspaces w ON w.Id = m.WorkspaceId
                    """)
                .ToListAsync(ct);

            rows.Count.ShouldBe(2);
            rows.Single(r => r.UserId == macedonian).ShouldBe(new BackfilledRow(macedonian, "Owner", "Мој работен простор", "Europe/Skopje", "MKD", "mk", 1));
            rows.Single(r => r.UserId == english).ShouldBe(new BackfilledRow(english, "Owner", "My workspace", "UTC", "MKD", "en", 1));

            // Re-running the backfill (as the idempotent migration script may) creates nothing new.
            // (Braces doubled: ExecuteSqlRaw treats {n} as parameter placeholders.)
            var backfill = Core.Infrastructure.Persistence.Migrations.AddWorkspacesTenancyAndAudit.BackfillWorkspacesSql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(backfill, ct);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM Workspaces").SingleAsync(ct)).ShouldBe(2);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    private static Task InsertR0P4UserAsync(ClassHubDbContext db, Guid id, string locale, string timeZone, CancellationToken ct)
    {
        var email = $"{id:N}@classhub.test";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO AspNetUsers (Id, Status, Locale, TimeZone, CreatedAtUtc, UserName, NormalizedUserName, Email, NormalizedEmail,
                                     EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, SecurityStamp, ConcurrencyStamp)
            VALUES ({id}, N'Active', {locale}, {timeZone}, SYSUTCDATETIME(), {email}, {email.ToUpperInvariant()}, {email}, {email.ToUpperInvariant()},
                    1, 0, 0, 1, 0, {Guid.NewGuid().ToString()}, {Guid.NewGuid().ToString()})
            """,
            ct);
    }

    private sealed record BackfilledRow(Guid UserId, string Role, string Name, string TimeZone, string Currency, string Locale, int AuditEntries);
}
