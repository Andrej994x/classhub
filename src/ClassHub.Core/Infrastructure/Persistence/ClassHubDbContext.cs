using ClassHub.Core.Infrastructure.Persistence.Technical;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context of the modular monolith. Each module contributes its entity
/// configurations (<see cref="IEntityTypeConfiguration{TEntity}"/>) from this assembly; there is one
/// migration history for the whole database.
/// </summary>
public class ClassHubDbContext(DbContextOptions<ClassHubDbContext> options) : DbContext(options)
{
    public DbSet<SchemaInfo> SchemaInfo => Set<SchemaInfo>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ClassHubModelConventions.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClassHubDbContext).Assembly);
        ClassHubModelConventions.ApplyModelConventions(modelBuilder);
    }
}
