using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.SharedKernel.Tenancy;
using ClassHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.IntegrationTests.Tenancy;

/// <summary>
/// The tenancy machinery on real SQL Server with a probe model (independent of business tables):
/// named tenant filter (fail closed), the save interceptor, and composite tenant foreign keys.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenancyProbeTests(ClassHubApiFixture fixture) : IAsyncLifetime
{
    private static readonly Guid WorkspaceA = Guid.NewGuid();
    private static readonly Guid WorkspaceB = Guid.NewGuid();
    private readonly string _connectionString = fixture.CreateIsolatedDatabaseConnectionString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var db = Context(null);
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = Context(null);
        await db.Database.EnsureDeletedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Queries_see_only_the_current_workspace_and_nothing_without_one()
    {
        await SeedParentAsync(WorkspaceA, "a1");
        await SeedParentAsync(WorkspaceB, "b1");

        await using (var a = Context(WorkspaceA))
        {
            (await a.Parents.Select(p => p.Name).ToListAsync(Ct)).ShouldBe(["a1"]);
        }

        await using (var none = Context(null))
        {
            (await none.Parents.CountAsync(Ct)).ShouldBe(0);
            (await none.Parents.IgnoreQueryFilters([TenancyFilters.Tenant]).CountAsync(Ct)).ShouldBeGreaterThanOrEqualTo(2);
        }
    }

    [Fact]
    public async Task Inserts_are_stamped_with_the_current_workspace()
    {
        await using var a = Context(WorkspaceA);
        var parent = new ProbeParent { Name = "stamped" };
        a.Parents.Add(parent);
        await a.SaveChangesAsync(Ct);

        parent.WorkspaceId.ShouldBe(WorkspaceA);
    }

    [Fact]
    public async Task Writes_for_another_workspace_are_refused()
    {
        await using var a = Context(WorkspaceA);
        a.Parents.Add(new ProbeParent { WorkspaceId = WorkspaceB, Name = "foreign" });

        await Should.ThrowAsync<TenantIsolationException>(() => a.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Workspace_id_of_a_referenced_parent_is_part_of_its_key_and_cannot_change()
    {
        var id = await SeedParentAsync(WorkspaceA, "moving");
        await using var a = Context(WorkspaceA);
        var parent = await a.Parents.SingleAsync(p => p.Id == id, Ct);

        // EF Core itself refuses: (WorkspaceId, Id) is the principal key of the composite tenant FK.
        Should.Throw<InvalidOperationException>(() =>
            a.Entry(parent).Property(nameof(ITenantOwned.WorkspaceId)).CurrentValue = WorkspaceB);
    }

    [Fact]
    public async Task Workspace_id_of_other_rows_can_never_change()
    {
        var parentA = await SeedParentAsync(WorkspaceA, "parent-a");
        Guid childId;
        await using (var arrange = Context(WorkspaceA))
        {
            var child = new ProbeChild { ParentId = parentA, Name = "child" };
            arrange.Children.Add(child);
            await arrange.SaveChangesAsync(Ct);
            childId = child.Id;
        }

        await using var a = Context(WorkspaceA);
        var loaded = await a.Children.SingleAsync(c => c.Id == childId, Ct);
        a.Entry(loaded).Property(nameof(ITenantOwned.WorkspaceId)).CurrentValue = WorkspaceB;

        var error = await Should.ThrowAsync<TenantIsolationException>(() => a.SaveChangesAsync(Ct));
        error.Message.ShouldContain("would move to another workspace");
    }

    [Fact]
    public async Task Rows_without_a_workspace_are_refused_outside_a_workspace_request()
    {
        await using var none = Context(null);
        none.Parents.Add(new ProbeParent { Name = "orphan" });

        await Should.ThrowAsync<TenantIsolationException>(() => none.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task The_database_rejects_references_to_another_workspaces_rows()
    {
        var foreignParent = await SeedParentAsync(WorkspaceB, "b-parent");

        // Even with the interceptor out of the way (explicit ids, no tenant context), the composite
        // (WorkspaceId, ParentId) foreign key makes a cross-workspace link impossible.
        await using var none = Context(null);
        none.Children.Add(new ProbeChild { WorkspaceId = WorkspaceA, ParentId = foreignParent, Name = "cross" });

        await Should.ThrowAsync<DbUpdateException>(() => none.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Same_workspace_references_are_accepted()
    {
        var parent = await SeedParentAsync(WorkspaceA, "a-parent");
        await using var a = Context(WorkspaceA);
        a.Children.Add(new ProbeChild { ParentId = parent, Name = "ok" });

        await a.SaveChangesAsync(Ct);
    }

    private async Task<Guid> SeedParentAsync(Guid workspaceId, string name)
    {
        await using var db = Context(workspaceId);
        var parent = new ProbeParent { Name = name };
        db.Parents.Add(parent);
        await db.SaveChangesAsync(Ct);
        return parent.Id;
    }

    private ProbeDbContext Context(Guid? workspaceId)
    {
        var tenant = new TenantContext();
        if (workspaceId is not null)
        {
            tenant.Set(workspaceId.Value, Guid.NewGuid(), WorkspaceRole.Owner);
        }

        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(TenantSaveChangesInterceptor.Instance)
            .Options;
        return new ProbeDbContext(options, tenant);
    }

    internal sealed class ProbeParent : ITenantOwned
    {
        public Guid Id { get; set; }
        public Guid WorkspaceId { get; set; }
        public required string Name { get; set; }
    }

    internal sealed class ProbeChild : ITenantOwned
    {
        public Guid Id { get; set; }
        public Guid WorkspaceId { get; set; }
        public Guid ParentId { get; set; }
        public required string Name { get; set; }
    }

    internal sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options, ITenantContext tenant)
        : DbContext(options), ITenantAwareDbContext
    {
        public ITenantContext? TenantContext { get; } = tenant;
        public Guid CurrentTenantId => TenantContext?.WorkspaceId ?? Guid.Empty;
        public DbSet<ProbeParent> Parents => Set<ProbeParent>();
        public DbSet<ProbeChild> Children => Set<ProbeChild>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            ClassHubModelConventions.ConfigureConventions(configurationBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeParent>(b =>
            {
                b.ToTable("TenantProbeParents");
                b.Property(x => x.Name).HasMaxLength(50);
            });
            modelBuilder.Entity<ProbeChild>(b =>
            {
                b.ToTable("TenantProbeChildren");
                b.Property(x => x.Name).HasMaxLength(50);
                b.HasTenantReference<ProbeChild, ProbeParent>(nameof(ProbeChild.ParentId));
            });
            TenancyModelConventions.Apply(modelBuilder, this);
            ClassHubModelConventions.ApplyModelConventions(modelBuilder);
        }
    }
}
