using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Database;

/// <summary>
/// Checks the shared model conventions on real SQL Server with a probe model (no business tables
/// exist yet): optional unique columns allow many NULLs, and ClassHub foreign keys never cascade.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConventionProbeTests(ClassHubApiFixture fixture) : IAsyncLifetime
{
    private readonly string _connectionString = fixture.CreateIsolatedDatabaseConnectionString();

    public async ValueTask InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Optional_unique_column_allows_many_nulls_but_no_duplicate_values()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var parent = new ProbeParent();
        db.Parents.Add(parent);
        db.Children.AddRange(
            new ProbeChild { Parent = parent, Code = null },
            new ProbeChild { Parent = parent, Code = null },
            new ProbeChild { Parent = parent, Code = "A" });
        await db.SaveChangesAsync(ct);

        db.Children.Add(new ProbeChild { Parent = parent, Code = "A" });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Deleting_a_referenced_parent_is_rejected_instead_of_cascading()
    {
        var ct = TestContext.Current.CancellationToken;
        int parentId;
        await using (var arrange = CreateContext())
        {
            var parent = new ProbeParent();
            arrange.Children.Add(new ProbeChild { Parent = parent });
            await arrange.SaveChangesAsync(ct);
            parentId = parent.Id;
        }

        await using var db = CreateContext();
        db.Model.FindEntityType(typeof(ProbeChild))!.GetForeignKeys().Single().DeleteBehavior
            .ShouldBe(DeleteBehavior.NoAction);

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM ProbeParents WHERE Id = {parentId}", ct));
    }

    private ProbeDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ProbeDbContext>().UseSqlServer(_connectionString).Options);

    internal sealed class ProbeParent
    {
        public int Id { get; set; }
    }

    internal sealed class ProbeChild
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public ProbeParent? Parent { get; set; }
        public string? Code { get; set; }
    }

    internal sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeParent> Parents => Set<ProbeParent>();
        public DbSet<ProbeChild> Children => Set<ProbeChild>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            ClassHubModelConventions.ConfigureConventions(configurationBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeParent>().ToTable("ProbeParents");
            modelBuilder.Entity<ProbeChild>(b =>
            {
                b.ToTable("ProbeChildren");
                b.Property(x => x.Code).HasMaxLength(20);
                b.HasIndex(x => x.Code).IsUnique();
                b.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId);
            });
            ClassHubModelConventions.ApplyModelConventions(modelBuilder);
        }
    }
}
