using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassHub.IntegrationTests.Safety;

/// <summary>From inside the running host: the API really uses the disposable container database.</summary>
[Collection(ApiCollection.Name)]
public sealed class DatabaseIsolationTests(ClassHubApiFixture fixture)
{
    [Fact]
    public void Host_runs_in_the_testing_environment()
    {
        var environment = fixture.Factory.Services.GetRequiredService<IWebHostEnvironment>();
        environment.IsEnvironment(ClassHubApiFactory.EnvironmentName).ShouldBeTrue();
        environment.IsDevelopment().ShouldBeFalse();
    }

    [Fact]
    public async Task Api_dbcontext_targets_the_container_test_database()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClassHub.Core.Infrastructure.Persistence.ClassHubDbContext>();

        TestDatabaseGuard.AssertIsolated(db.Database.GetConnectionString(), fixture.ExpectedDataSource);

        var databaseName = await db.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").SingleAsync(ct);
        databaseName.ShouldStartWith(TestDatabaseGuard.DatabasePrefix);

        var machineName = await db.Database
            .SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) AS [Value]")
            .SingleAsync(ct);
        fixture.ContainerId.ShouldStartWith(machineName, Case.Insensitive);
    }
}
