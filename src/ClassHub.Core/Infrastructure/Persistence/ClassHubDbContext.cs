using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence.Technical;
using ClassHub.Core.Modules.Identity.Contracts;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClassHub.Core.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context of the modular monolith: ASP.NET Core Identity stores, Data Protection keys,
/// and every module's entities (configured from this assembly). One migration history for the database.
/// </summary>
public class ClassHubDbContext(DbContextOptions<ClassHubDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<SchemaInfo> SchemaInfo => Set<SchemaInfo>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();

    public DbSet<DevMailboxMessage> DevMailbox => Set<DevMailboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ClassHubModelConventions.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ClassHubDbContext).Assembly);
        ClassHubModelConventions.ApplyModelConventions(builder);
    }
}
